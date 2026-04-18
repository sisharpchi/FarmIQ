using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FarmIQ.Admin.Models;
using FarmIQ.Shared;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace FarmIQ.Admin.Services;

public interface IAuthService
{
    Task<(bool Success, string? ErrorMessage)> LoginAsync(LoginRequestModel request);
    Task<(bool Success, string? ErrorMessage)> SignupAsync(SignupRequestModel request);
    Task LogoutAsync();
    Task<AuthSessionModel?> GetSessionAsync();
}

public sealed class BrowserSessionStore(IJSRuntime jsRuntime)
{
    private const string SessionKey = "farmiq.admin.session";

    public ValueTask SaveAsync(AuthSessionModel session) =>
        jsRuntime.InvokeVoidAsync("farmiqAuth.saveSession", SessionKey, session);

    public ValueTask<AuthSessionModel?> LoadAsync() =>
        jsRuntime.InvokeAsync<AuthSessionModel?>("farmiqAuth.loadSession", SessionKey);

    public ValueTask RemoveAsync() =>
        jsRuntime.InvokeVoidAsync("farmiqAuth.clearSession", SessionKey);
}

public sealed class FarmAuthStateProvider(BrowserSessionStore sessionStore) : AuthenticationStateProvider
{
    private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_currentUser));

    public async Task RestoreSessionAsync()
    {
        var session = await sessionStore.LoadAsync();
        _currentUser = CreatePrincipal(session?.User);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task SetSessionAsync(AuthSessionModel session)
    {
        await sessionStore.SaveAsync(session);
        _currentUser = CreatePrincipal(session.User);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task ClearSessionAsync()
    {
        await sessionStore.RemoveAsync();
        _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal CreatePrincipal(UserSessionModel? user)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.UserId))
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Email, user.Email)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "FarmIQAdmin"));
    }
}

public sealed class AuthService(
    IConfiguration configuration,
    BrowserSessionStore sessionStore,
    FarmAuthStateProvider authStateProvider) : IAuthService
{
    private readonly string _apiBaseUrl = configuration["Api:BaseUrl"] ?? "https://localhost:7178";

    public async Task<(bool Success, string? ErrorMessage)> LoginAsync(LoginRequestModel request)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = request.Email,
                ["password"] = request.Password
            });

            var tokenResponse = await client.PostAsync("/connect/token", form);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return (false, "Email or password was invalid.");
            }

            var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return (false, "Token response was empty.");
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            var sessionResponse = await client.GetAsync("/api/admin/session");
            if (!sessionResponse.IsSuccessStatusCode)
            {
                return (false, "Unable to load admin session.");
            }

            var user = await sessionResponse.Content.ReadFromJsonAsync<UserSessionModel>();
            if (user is null)
            {
                return (false, "Unable to read admin session.");
            }

            var session = new AuthSessionModel
            {
                AccessToken = token.AccessToken,
                ExpiresUtc = DateTime.UtcNow.AddSeconds(token.ExpiresIn > 0 ? token.ExpiresIn : 3600),
                User = user
            };

            await authStateProvider.SetSessionAsync(session);
            return (true, null);
        }
        catch (HttpRequestException)
        {
            return (false, "FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct.");
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> SignupAsync(SignupRequestModel request)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
            var response = await client.PostAsJsonAsync("/api/auth/signup", request);
            if (!response.IsSuccessStatusCode)
            {
                var problem = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(problem) ? "Unable to create your account." : problem.Trim('"'));
            }

            return await LoginAsync(new LoginRequestModel
            {
                Email = request.Email,
                Password = request.Password
            });
        }
        catch (HttpRequestException)
        {
            return (false, "FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct.");
        }
    }

    public Task LogoutAsync() => authStateProvider.ClearSessionAsync();

    public async Task<AuthSessionModel?> GetSessionAsync()
    {
        var session = await sessionStore.LoadAsync();
        if (session is null || session.ExpiresUtc <= DateTime.UtcNow)
        {
            if (session is not null)
            {
                await authStateProvider.ClearSessionAsync();
            }

            return null;
        }

        return session;
    }
}

public sealed class FarmAdminApiClient(IConfiguration configuration, IAuthService authService)
{
    private readonly string _apiBaseUrl = configuration["Api:BaseUrl"] ?? "https://localhost:7178";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PagedResponseModel<ConversationSummaryModel>> GetConversationsAsync(int page, int pageSize) =>
        await GetAsync<PagedResponseModel<ConversationSummaryModel>>($"/api/admin/conversations?page={page}&pageSize={pageSize}") ?? new();

    public async Task<ConversationDetailModel?> GetConversationDetailAsync(Guid conversationId) =>
        await GetAsync<ConversationDetailModel>($"/api/admin/conversations/{conversationId}");

    public async Task<PagedResponseModel<ProcessingJobSummaryModel>> GetJobsAsync(ProcessingJobStatus? status, int page, int pageSize)
    {
        var filter = status.HasValue ? $"&status={status}" : string.Empty;
        return await GetAsync<PagedResponseModel<ProcessingJobSummaryModel>>($"/api/admin/jobs?page={page}&pageSize={pageSize}{filter}") ?? new();
    }

    public async Task<PagedResponseModel<StuckJobSummaryModel>> GetStuckJobsAsync(int page, int pageSize) =>
        await GetAsync<PagedResponseModel<StuckJobSummaryModel>>($"/api/admin/jobs/stuck?page={page}&pageSize={pageSize}") ?? new();

    public async Task<PagedResponseModel<DeliveryIssueSummaryModel>> GetDeliveryIssuesAsync(int page, int pageSize) =>
        await GetAsync<PagedResponseModel<DeliveryIssueSummaryModel>>($"/api/admin/deliveries/issues?page={page}&pageSize={pageSize}") ?? new();

    public async Task<PagedResponseModel<AdvisorySummaryModel>> GetAdvisoriesAsync(int page, int pageSize) =>
        await GetAsync<PagedResponseModel<AdvisorySummaryModel>>($"/api/admin/advisories?page={page}&pageSize={pageSize}") ?? new();

    public async Task<AdvisoryDetailModel?> GetAdvisoryDetailAsync(Guid advisoryId) =>
        await GetAsync<AdvisoryDetailModel>($"/api/admin/advisories/{advisoryId}");

    public async Task<AnalyticsSummaryModel?> GetAnalyticsAsync() =>
        await GetAsync<AnalyticsSummaryModel>("/api/admin/analytics");

    public async Task<InsightSummaryModel?> GetInsightsAsync() =>
        await GetAsync<InsightSummaryModel>("/api/admin/insights/anonymized");

    public async Task<SystemStatusModel?> GetStatusAsync() =>
        await GetAsync<SystemStatusModel>("/api/admin/status");

    public async Task<UserSessionModel?> GetSessionInfoAsync() =>
        await GetAsync<UserSessionModel>("/api/admin/session");

    public async Task<bool> RetryJobAsync(Guid jobId)
    {
        var session = await authService.GetSessionAsync();
        if (session is null)
        {
            return false;
        }

        using var client = CreateClient(session.AccessToken);
        var response = await client.PostAsJsonAsync("/api/admin/jobs/retry", new { processingJobId = jobId });
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> IsApiHealthyAsync()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
        var response = await client.GetAsync("/health");
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task<T?> GetAsync<T>(string url)
    {
        var session = await authService.GetSessionAsync();
        if (session is null)
        {
            return default;
        }

        using var client = CreateClient(session.AccessToken);
        var response = await client.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await authService.LogoutAsync();
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private HttpClient CreateClient(string accessToken)
    {
        var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}

public sealed class RefreshCoordinator
{
    public IDisposable StartPolling(Func<Task> refreshAction, TimeSpan interval)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(cts.Token))
                    {
                        break;
                    }

                    await refreshAction();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, cts.Token);

        return new PollingHandle(cts);
    }

    private sealed class PollingHandle(CancellationTokenSource cts) : IDisposable
    {
        public void Dispose()
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }

            cts.Dispose();
        }
    }
}
