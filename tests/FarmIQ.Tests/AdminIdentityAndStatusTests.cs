using FarmIQ.API.Controllers;
using FarmIQ.Application.Contracts;
using FarmIQ.Application.Services;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Infrastructure.Identity;
using FarmIQ.Infrastructure.Persistence;
using FarmIQ.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace FarmIQ.Tests;

public sealed class AdminIdentityAndStatusTests
{
    [Fact]
    public async Task AdminUserManagementService_ShouldCreateAndManageInviteOnlyUsers()
    {
        await using var dbContext = CreateDbContext();
        var (userManager, roleManager, _) = CreateIdentityManagers(dbContext);
        var service = new AdminUserManagementService(userManager, roleManager, dbContext);

        var created = await service.CreateUserAsync(new AdminCreateUserRequest
        {
            DisplayName = "Analyst One",
            Email = "analyst1@farmiq.test",
            Password = "Strong!123",
            Roles = ["Analyst", "Ops"]
        }, "seed-admin", "corr-1");

        created.Email.Should().Be("analyst1@farmiq.test");
        created.IsEnabled.Should().BeTrue();
        created.Roles.Should().BeEquivalentTo(["Analyst", "Ops"]);

        var listed = await service.GetUsersAsync(1, 10);
        listed.Items.Should().ContainSingle(user => user.UserId == created.UserId);

        var disabled = await service.DisableUserAsync(created.UserId, "seed-admin", "corr-2");
        disabled.IsEnabled.Should().BeFalse();

        var enabled = await service.EnableUserAsync(created.UserId, "seed-admin", "corr-3");
        enabled.IsEnabled.Should().BeTrue();

        var rolesUpdated = await service.UpdateRolesAsync(created.UserId, new AdminUpdateUserRolesRequest
        {
            Roles = ["Admin"]
        }, "seed-admin", "corr-4");
        rolesUpdated.Roles.Should().BeEquivalentTo(["Admin"]);

        await service.ResetPasswordAsync(created.UserId, new AdminResetPasswordRequest
        {
            NewPassword = "Better!456"
        }, "seed-admin", "corr-5");

        var user = await userManager.FindByIdAsync(created.UserId.ToString());
        user.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(user!, "Better!456")).Should().BeTrue();
        dbContext.AuditLogs.Should().HaveCount(5);
    }

    [Fact]
    public async Task SignUp_ShouldReturnForbidden_WhenPublicSignupIsDisabled()
    {
        await using var dbContext = CreateDbContext();
        var (userManager, roleManager, signInManager) = CreateIdentityManagers(dbContext);
        var controller = new AuthController(
            userManager,
            signInManager,
            roleManager,
            Options.Create(new AuthOptions
            {
                EnablePublicSignup = false,
                AccessTokenLifetimeMinutes = 60
            }));

        var result = await controller.SignUp(new AuthController.SignUpRequest
        {
            DisplayName = "New User",
            Email = "newuser@farmiq.test",
            Password = "Strong!123",
            ConfirmPassword = "Strong!123"
        });

        result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task Exchange_ShouldRejectDisabledUsersBeforePasswordValidation()
    {
        await using var dbContext = CreateDbContext();
        var (userManager, roleManager, signInManager) = CreateIdentityManagers(dbContext);
        await roleManager.CreateAsync(new IdentityRole<Guid>("Analyst"));

        var user = new ApplicationUser
        {
            UserName = "disabled@farmiq.test",
            Email = "disabled@farmiq.test",
            DisplayName = "Disabled User",
            EmailConfirmed = true,
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.UtcNow.AddYears(100)
        };

        var createResult = await userManager.CreateAsync(user, "Strong!123");
        createResult.Succeeded.Should().BeTrue();

        var controller = new AuthController(
            userManager,
            signInManager,
            roleManager,
            Options.Create(new AuthOptions
            {
                EnablePublicSignup = false,
                AccessTokenLifetimeMinutes = 60
            }))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildTokenHttpContext(user.Email!, "Strong!123")
            }
        };

        var result = await controller.Exchange();
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetSystemStatusAsync_ShouldExposeProductionAuthAndPollingConfig()
    {
        await using var dbContext = CreateDbContext();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:EnablePublicSignup"] = "false",
            ["Processing:PollIntervalSeconds"] = "45",
            ["ChannelApis:WhatsAppBaseUrl"] = "https://graph.facebook.com",
            ["ChannelApis:TelegramBaseUrl"] = "https://api.telegram.org",
            ["ChannelApis:InstagramBaseUrl"] = "https://graph.facebook.com",
            ["Storage:RootPath"] = "/data/media",
            ["OpenWeatherMap:BaseUrl"] = "https://api.openweathermap.org/data/2.5",
            ["OpenAI:Enabled"] = "true",
            ["OpenAI:ApiKey"] = "test-key",
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=farmiq;Username=postgres;Password=postgres"
        }).Build();

        var service = new AdminQueryService(new UnitOfWork(dbContext), new FakeBackgroundJobQueue(), configuration);
        var status = await service.GetSystemStatusAsync();

        status.PublicSignupEnabled.Should().BeFalse();
        status.WorkerPollIntervalSeconds.Should().Be(45);
        status.DatabaseConfigured.Should().BeTrue();
        status.OpenAiConfigured.Should().BeTrue();
    }

    private static FarmIQDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FarmIQDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new FarmIQDbContext(options);
    }

    private static (UserManager<ApplicationUser> UserManager, RoleManager<IdentityRole<Guid>> RoleManager, SignInManager<ApplicationUser> SignInManager) CreateIdentityManagers(FarmIQDbContext dbContext)
    {
        var identityOptions = Options.Create(new IdentityOptions
        {
            Lockout =
            {
                AllowedForNewUsers = true
            },
            Password =
            {
                RequireDigit = true,
                RequiredLength = 8,
                RequireUppercase = true,
                RequireNonAlphanumeric = true
            },
            User =
            {
                RequireUniqueEmail = true
            }
        });

        var userStore = new UserStore<ApplicationUser, IdentityRole<Guid>, FarmIQDbContext, Guid>(dbContext);
        var roleStore = new RoleStore<IdentityRole<Guid>, FarmIQDbContext, Guid>(dbContext);

        var roleManager = new RoleManager<IdentityRole<Guid>>(
            roleStore,
            [new RoleValidator<IdentityRole<Guid>>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole<Guid>>>.Instance);

        var userManager = new UserManager<ApplicationUser>(
            userStore,
            identityOptions,
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var contextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var claimsFactory = new UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, identityOptions);
        var signInManager = new SignInManager<ApplicationUser>(
            userManager,
            contextAccessor,
            claimsFactory,
            identityOptions,
            NullLogger<SignInManager<ApplicationUser>>.Instance,
            new AuthenticationSchemeProvider(Options.Create(new AuthenticationOptions())),
            new DefaultUserConfirmation<ApplicationUser>());

        return (userManager, roleManager, signInManager);
    }

    private static DefaultHttpContext BuildTokenHttpContext(string email, string password)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/x-www-form-urlencoded";
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["grant_type"] = "password",
            ["username"] = email,
            ["password"] = password
        });

        context.Features.Set<IFormFeature>(new FormFeature(form));
        return context;
    }

    private sealed class FakeBackgroundJobQueue : FarmIQ.Application.Abstractions.IBackgroundJobQueue
    {
        public ValueTask QueueAsync(Guid jobId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask WaitForSignalAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
