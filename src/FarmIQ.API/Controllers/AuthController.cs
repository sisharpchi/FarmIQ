using System.Globalization;
using System.Security.Claims;
using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace FarmIQ.API.Controllers;

[ApiController]
public sealed class AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<AuthOptions> authOptions) : ControllerBase
{
    [HttpPost("~/api/auth/signup")]
    [Produces("application/json")]
    public async Task<IActionResult> SignUp([FromBody] SignUpRequest request)
    {
        if (!authOptions.Value.EnablePublicSignup)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "public_signup_disabled",
                error_description = Translate(
                    "Public signup is disabled. Ask an existing admin to create your account.",
                    "Публичная регистрация отключена. Попросите существующего администратора создать вам аккаунт.",
                    "Public signup o'chirilgan. Hisob ochish uchun mavjud adminga murojaat qiling.")
            });
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return Conflict(Translate(
                "An account with that email already exists.",
                "Аккаунт с таким email уже существует.",
                "Bu email bilan hisob allaqachon mavjud."));
        }

        const string defaultRole = "Analyst";
        if (!await roleManager.RoleExistsAsync(defaultRole))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(defaultRole));
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return BadRequest(string.Join(" ", createResult.Errors.Select(x => x.Description)));
        }

        var addToRoleResult = await userManager.AddToRoleAsync(user, defaultRole);
        if (!addToRoleResult.Succeeded)
        {
            return BadRequest(string.Join(" ", addToRoleResult.Errors.Select(x => x.Description)));
        }

        return Created("/api/auth/signup", new
        {
            userId = user.Id,
            user.Email,
            user.DisplayName,
            role = defaultRole
        });
    }

    [HttpPost("~/connect/token")]
    [Produces("application/json")]
    public async Task<IActionResult> Exchange()
    {
        if (!HasSupportedFormContentType(Request.ContentType))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                error_description = Translate(
                    "Token requests must use form content.",
                    "Token-запросы должны использовать form content.",
                    "Token so'rovlari form content bilan yuborilishi kerak.")
            });
        }

        IFormCollection form;

        try
        {
            form = await Request.ReadFormAsync();
        }
        catch (Exception)
        {
            return BadRequest(new
            {
                error = "invalid_request",
                error_description = Translate(
                    "Token request form payload could not be read.",
                    "Не удалось прочитать form payload token-запроса.",
                    "Token so'rovining form payload'ini o'qib bo'lmadi.")
            });
        }

        var grantType = form["grant_type"].ToString();
        var username = form["username"].ToString();
        var password = form["password"].ToString();

        if (!string.Equals(grantType, OpenIddictConstants.GrantTypes.Password, StringComparison.Ordinal))
        {
            return BadRequest(new { error = "unsupported_grant_type" });
        }

        var user = await userManager.FindByEmailAsync(username);
        if (user is null)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return BadRequest(new
            {
                error = "account_disabled",
                error_description = Translate(
                    "This FarmIQ admin account is disabled or locked.",
                    "Этот FarmIQ admin аккаунт отключён или заблокирован.",
                    "Bu FarmIQ admin hisobi o'chirilgan yoki bloklangan.")
            });
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return BadRequest(new
            {
                error = "account_disabled",
                error_description = Translate(
                    "This FarmIQ admin account is disabled or locked.",
                    "Этот FarmIQ admin аккаунт отключён или заблокирован.",
                    "Bu FarmIQ admin hisobi o'chirilgan yoki bloklangan.")
            });
        }

        if (!result.Succeeded)
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        var claims = new List<Claim>
        {
            new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString()),
            new Claim(OpenIddictConstants.Claims.Email, user.Email ?? string.Empty),
            new Claim(OpenIddictConstants.Claims.Name, user.DisplayName)
        };

        var roles = await userManager.GetRolesAsync(user);
        claims.AddRange(roles.Select(role => new Claim(OpenIddictConstants.Claims.Role, role)));

        var identity = new ClaimsIdentity(claims, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        identity.SetScopes(new[]
        {
            OpenIddictConstants.Scopes.Email,
            OpenIddictConstants.Scopes.Profile,
            OpenIddictConstants.Scopes.Roles
        });
        identity.SetDestinations(static claim => claim.Type switch
        {
            OpenIddictConstants.Claims.Name or OpenIddictConstants.Claims.Email or OpenIddictConstants.Claims.Role => [OpenIddictConstants.Destinations.AccessToken],
            _ => [OpenIddictConstants.Destinations.AccessToken]
        });

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static bool HasSupportedFormContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            || contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase);
    }

    private static string Translate(string english, string russian, string uzbek) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
        {
            "ru" => russian,
            "uz" => uzbek,
            _ => english
        };

    public sealed class SignUpRequest
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(120, MinimumLength = 2)]
        public string DisplayName { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.EmailAddress]
        public string Email { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 8)]
        public string Password { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
