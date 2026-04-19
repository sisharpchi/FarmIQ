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

public enum RefreshArea
{
    Dashboard,
    Jobs,
    Deliveries
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
    public bool IsInitialized { get; private set; }
    public AuthSessionModel? CurrentSession { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_currentUser));

    public async Task RestoreSessionAsync()
    {
        var session = await sessionStore.LoadAsync();
        if (session is null || session.ExpiresUtc <= DateTime.UtcNow)
        {
            await sessionStore.RemoveAsync();
            CurrentSession = null;
        }
        else
        {
            CurrentSession = session;
        }

        _currentUser = CreatePrincipal(CurrentSession?.User);
        IsInitialized = true;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task SetSessionAsync(AuthSessionModel session)
    {
        await sessionStore.SaveAsync(session);
        CurrentSession = session;
        IsInitialized = true;
        _currentUser = CreatePrincipal(session.User);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task ClearSessionAsync()
    {
        await sessionStore.RemoveAsync();
        CurrentSession = null;
        IsInitialized = true;
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
    FarmAuthStateProvider authStateProvider,
    AdminLocalizer localizer) : IAuthService
{
    private readonly string _apiBaseUrl = configuration["Api:BaseUrl"] ?? "https://localhost:7127";

    public async Task<(bool Success, string? ErrorMessage)> LoginAsync(LoginRequestModel request)
    {
        try
        {
            using var client = CreateClient();
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = request.Email,
                ["password"] = request.Password
            });

            var tokenResponse = await client.PostAsync("/connect/token", form);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return (false, await ReadErrorMessageAsync(tokenResponse));
            }

            var token = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>();
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return (false, localizer["Token response was empty."]);
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            var sessionResponse = await client.GetAsync("/api/admin/session");
            if (!sessionResponse.IsSuccessStatusCode)
            {
                return (false, localizer["Unable to load admin session."]);
            }

            var user = await sessionResponse.Content.ReadFromJsonAsync<UserSessionModel>();
            if (user is null)
            {
                return (false, localizer["Unable to read admin session."]);
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
            return (false, localizer["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."]);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> SignupAsync(SignupRequestModel request)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.PostAsJsonAsync("/api/auth/signup", request);
            if (!response.IsSuccessStatusCode)
            {
                var problem = await response.Content.ReadAsStringAsync();
                return (false, string.IsNullOrWhiteSpace(problem) ? localizer["The request could not be completed."] : localizer.NormalizeApiMessage(null, problem));
            }

            return await LoginAsync(new LoginRequestModel
            {
                Email = request.Email,
                Password = request.Password
            });
        }
        catch (HttpRequestException)
        {
            return (false, localizer["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."]);
        }
    }

    public Task LogoutAsync() => authStateProvider.ClearSessionAsync();

    public async Task<AuthSessionModel?> GetSessionAsync()
    {
        if (authStateProvider.IsInitialized)
        {
            return authStateProvider.CurrentSession;
        }

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

    private HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(localizer.CurrentLanguage);
        return client;
    }

    private async Task<string> ReadErrorMessageAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return localizer["Email or password was invalid."];
        }

        try
        {
            var problem = JsonSerializer.Deserialize<ApiErrorModel>(raw);
            return localizer.NormalizeApiMessage(
                problem?.Error,
                problem?.ErrorDescription ?? problem?.Detail ?? problem?.Title ?? problem?.Error ?? raw);
        }
        catch (JsonException)
        {
        }

        return localizer.NormalizeApiMessage(null, raw.Trim().Trim('"'));
    }
}

public sealed class FarmAdminApiClient(IConfiguration configuration, IAuthService authService, AdminLocalizer localizer)
{
    private readonly string _apiBaseUrl = configuration["Api:BaseUrl"] ?? "https://localhost:7127";
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

    public async Task<PagedResponseModel<AdminUserSummaryModel>> GetUsersAsync(int page, int pageSize) =>
        await GetAsync<PagedResponseModel<AdminUserSummaryModel>>($"/api/admin/users?page={page}&pageSize={pageSize}") ?? new();

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

    public async Task<(bool Success, string? ErrorMessage)> CreateUserAsync(AdminUserCreateModel request)
    {
        try
        {
            var response = await PostAsync("/api/admin/users", request);
            return response;
        }
        catch (HttpRequestException)
        {
            return (false, localizer["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."]);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> DisableUserAsync(Guid userId)
    {
        try
        {
            return await PostAsync($"/api/admin/users/{userId}/disable", new { });
        }
        catch (HttpRequestException)
        {
            return (false, localizer["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."]);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> EnableUserAsync(Guid userId)
    {
        try
        {
            return await PostAsync($"/api/admin/users/{userId}/enable", new { });
        }
        catch (HttpRequestException)
        {
            return (false, localizer["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."]);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> ResetPasswordAsync(Guid userId, string newPassword)
    {
        try
        {
            return await PostAsync($"/api/admin/users/{userId}/reset-password", new AdminResetPasswordModel
            {
                NewPassword = newPassword
            });
        }
        catch (HttpRequestException)
        {
            return (false, localizer["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."]);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> UpdateUserRolesAsync(Guid userId, IReadOnlyCollection<string> roles)
    {
        try
        {
            return await PostAsync($"/api/admin/users/{userId}/roles", new AdminUserRoleUpdateModel
            {
                Roles = roles
            });
        }
        catch (HttpRequestException)
        {
            return (false, localizer["FarmIQ API is unavailable. Confirm the API is running and the admin BaseUrl is correct."]);
        }
    }

    public async Task<bool> IsApiHealthyAsync()
    {
        using var client = CreateClient(string.Empty);
        var response = await client.GetAsync("/health/ready");
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
            throw new UnauthorizedAccessException(localizer["Your admin session expired. Please sign in again."]);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    private async Task<(bool Success, string? ErrorMessage)> PostAsync<TRequest>(string url, TRequest request)
    {
        var session = await authService.GetSessionAsync();
        if (session is null)
        {
            return (false, localizer["Your admin session expired. Please sign in again."]);
        }

        using var client = CreateClient(session.AccessToken);
        var response = await client.PostAsJsonAsync(url, request);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await authService.LogoutAsync();
            return (false, localizer["Your admin session expired. Please sign in again."]);
        }

        if (response.IsSuccessStatusCode)
        {
            return (true, null);
        }

        var error = await ReadErrorAsync(response);
        return (false, error);
    }

    private async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return localizer["The request could not be completed."];
        }

        try
        {
            var problem = JsonSerializer.Deserialize<ApiErrorModel>(raw, JsonOptions);
            return localizer.NormalizeApiMessage(
                problem?.Error,
                problem?.ErrorDescription ?? problem?.Detail ?? problem?.Title ?? problem?.Error ?? raw);
        }
        catch (JsonException)
        {
        }

        return localizer.NormalizeApiMessage(null, raw.Trim().Trim('"'));
    }

    private HttpClient CreateClient(string accessToken)
    {
        var client = new HttpClient { BaseAddress = new Uri(_apiBaseUrl) };
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(localizer.CurrentLanguage);
        return client;
    }
}

public sealed class RefreshCoordinator(IConfiguration configuration)
{
    public IDisposable StartPolling(RefreshArea area, Func<Task> refreshAction) =>
        StartPolling(refreshAction, ResolveInterval(area));

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

    private TimeSpan ResolveInterval(RefreshArea area)
    {
        var seconds = area switch
        {
            RefreshArea.Dashboard => configuration.GetValue("Polling:DashboardSeconds", 15),
            RefreshArea.Jobs => configuration.GetValue("Polling:JobsSeconds", 10),
            RefreshArea.Deliveries => configuration.GetValue("Polling:DeliveriesSeconds", 12),
            _ => 15
        };

        return TimeSpan.FromSeconds(Math.Max(seconds, 5));
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
