using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Services;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Infrastructure.Identity;
using FarmIQ.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace FarmIQ.Infrastructure.Services;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddFarmIQInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var authOptions = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        services.AddOptions<OpenWeatherMapOptions>()
            .Bind(configuration.GetSection(OpenWeatherMapOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.BaseUrl), "OpenWeatherMap base URL is required.")
            .ValidateOnStart();
        services.AddOptions<LocalStorageOptions>()
            .Bind(configuration.GetSection(LocalStorageOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.RootPath), "Storage root path is required.")
            .ValidateOnStart();
        services.AddOptions<SeedAdminOptions>()
            .Bind(configuration.GetSection(SeedAdminOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.Email) && !string.IsNullOrWhiteSpace(x.Password), "Seed admin email and password are required.")
            .Validate(x => environment.IsDevelopment() || !x.UsesDefaultCredentials(), "Seed admin credentials must be overridden outside development.")
            .ValidateOnStart();
        services.AddOptions<WebhookOptions>()
            .Bind(configuration.GetSection(WebhookOptions.SectionName))
            .Validate(x => !string.IsNullOrWhiteSpace(x.WhatsAppVerifyToken) && !string.IsNullOrWhiteSpace(x.TelegramSecretToken) && !string.IsNullOrWhiteSpace(x.InstagramVerifyToken), "Webhook secrets are required.")
            .ValidateOnStart();
        services.AddOptions<ChannelApiOptions>()
            .Bind(configuration.GetSection(ChannelApiOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<ProcessingOptions>()
            .Bind(configuration.GetSection(ProcessingOptions.SectionName))
            .Validate(x => x.PollIntervalSeconds > 0 && x.LeaseDurationMinutes > 0, "Processing options must be positive.")
            .ValidateOnStart();
        services.AddOptions<OpenAIOptions>()
            .Bind(configuration.GetSection(OpenAIOptions.SectionName))
            .Validate(x => !x.Enabled || !string.IsNullOrWhiteSpace(x.ApiKey), "OpenAI ApiKey is required when OpenAI is enabled.")
            .Validate(x => !x.Enabled || (!string.IsNullOrWhiteSpace(x.VisionModel) && !string.IsNullOrWhiteSpace(x.TranscriptionModel)), "OpenAI model names are required when OpenAI is enabled.")
            .Validate(x => x.TimeoutSeconds > 0 && x.MaxImagesPerRequest > 0, "OpenAI options must be positive.")
            .ValidateOnStart();
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .Validate(x => x.AccessTokenLifetimeMinutes > 0, "Auth access token lifetime must be positive.")
            .Validate(x => environment.IsDevelopment() || HasStrongKey(x.SigningKey), "Auth:SigningKey must be configured with at least 32 characters outside development.")
            .Validate(x => environment.IsDevelopment() || HasStrongKey(x.EncryptionKey), "Auth:EncryptionKey must be configured with at least 32 characters outside development.")
            .ValidateOnStart();

        services.AddDbContext<FarmIQDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Database=farmiq;Username=postgres;Password=postgres";

            options.UseNpgsql(connectionString);
            options.UseOpenIddict<Guid>();
        });

        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<FarmIQDbContext>()
            .AddDefaultTokenProviders();

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                    .UseDbContext<FarmIQDbContext>();
                options.UseEntityFrameworkCore()
                    .ReplaceDefaultEntities<Guid>();
            })
            .AddServer(options =>
            {
                options.SetTokenEndpointUris("/connect/token");
                options.AllowPasswordFlow();
                options.AcceptAnonymousClients();
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(authOptions.AccessTokenLifetimeMinutes));

                if (environment.IsDevelopment())
                {
                    options.AddDevelopmentEncryptionCertificate()
                        .AddDevelopmentSigningCertificate();
                }
                else
                {
                    options.AddEncryptionKey(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.EncryptionKey!)))
                        .AddSigningKey(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey!)));
                }

                options.UseAspNetCore()
                    .EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        services.AddHttpClient(nameof(LocalMediaStorageService));
        services.AddHttpClient(nameof(OpenWeatherMapService));
        services.AddHttpClient(nameof(OpenAiCropAnalysisService));
        services.AddHttpClient(nameof(OpenAiSpeechToTextService));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IMessageIngestionService, MessageIngestionService>();
        services.AddScoped<IAdvisoryWorkflowService, AdvisoryWorkflowService>();
        services.AddScoped<IInboundIntentClassifier, InboundIntentClassifier>();
        services.AddScoped<IConversationResponseComposer, ConversationResponseComposer>();
        services.AddScoped<IProcessingJobLeaseService, ProcessingJobLeaseService>();
        services.AddScoped<IAdminQueryService, AdminQueryService>();
        services.AddScoped<IAdminUserManagementService, AdminUserManagementService>();
        services.AddSingleton<IProcessingRuntimeSettings, ProcessingRuntimeSettings>();
        services.AddSingleton<WorkerHeartbeat>();

        services.AddSingleton<IBackgroundJobQueue, InMemoryBackgroundJobQueue>();
        services.AddHostedService<AdvisoryProcessingWorker>();

        services.AddHttpContextAccessor();
        services.AddScoped<IMediaStorageService, LocalMediaStorageService>();
        services.AddScoped<ISpeechToTextService, OpenAiSpeechToTextService>();
        services.AddScoped<ILanguageService, MockLanguageService>();
        services.AddScoped<ICropAnalysisService, OpenAiCropAnalysisService>();
        services.AddScoped<IWeatherService, OpenWeatherMapService>();

        services.AddScoped<IMessageChannelService, WhatsAppService>();
        services.AddScoped<IMessageChannelService, TelegramService>();
        services.AddScoped<IMessageChannelService, InstagramService>();
        services.AddScoped<IMessageChannelResolver, MessageChannelResolver>();

        services.AddSingleton<IStartupFilter, ConfigurationValidationStartupFilter>();

        return services;
    }

    private static bool HasStrongKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 32;

    public static async Task SeedIdentityAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SeedAdminOptions>>();

        foreach (var role in new[] { "Admin", "Ops", "Analyst" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        var email = options.Value.Email;
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = options.Value.DisplayName,
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, options.Value.Password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException($"Unable to seed admin user: {string.Join(", ", createResult.Errors.Select(x => x.Description))}");
        }

        await userManager.AddToRolesAsync(user, new[] { "Admin", "Ops" });
    }
}

internal sealed class ProcessingRuntimeSettings(IOptions<ProcessingOptions> options) : IProcessingRuntimeSettings
{
    public int LeaseDurationMinutes => options.Value.LeaseDurationMinutes;
}

internal sealed class ConfigurationValidationStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            _ = app.ApplicationServices.GetRequiredService<IOptions<LocalStorageOptions>>().Value;
            _ = app.ApplicationServices.GetRequiredService<IOptions<SeedAdminOptions>>().Value;
            _ = app.ApplicationServices.GetRequiredService<IOptions<WebhookOptions>>().Value;
            _ = app.ApplicationServices.GetRequiredService<IOptions<ProcessingOptions>>().Value;
            _ = app.ApplicationServices.GetRequiredService<IOptions<OpenAIOptions>>().Value;
            _ = app.ApplicationServices.GetRequiredService<IOptions<AuthOptions>>().Value;
            var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
            if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            {
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");
            }
            next(app);
        };
    }
}
