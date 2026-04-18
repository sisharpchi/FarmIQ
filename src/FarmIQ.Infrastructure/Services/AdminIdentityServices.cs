using System.Text.Json;
using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Core.Entities;
using FarmIQ.Infrastructure.Identity;
using FarmIQ.Infrastructure.Persistence;
using FarmIQ.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FarmIQ.Infrastructure.Services;

public sealed class AdminUserManagementService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    FarmIQDbContext dbContext) : IAdminUserManagementService
{
    private static readonly string[] KnownRoles = ["Admin", "Ops", "Analyst"];

    public async Task<PagedResponse<AdminUserSummaryDto>> GetUsersAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = userManager.Users
            .OrderBy(x => x.Email);

        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<AdminUserSummaryDto>(users.Count);
        foreach (var user in users)
        {
            items.Add(await MapAsync(user, cancellationToken));
        }

        return new PagedResponse<AdminUserSummaryDto>(items, totalCount, page, pageSize);
    }

    public async Task<AdminUserSummaryDto> CreateUserAsync(AdminCreateUserRequest request, string actor, string? correlationId, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            throw new InvalidOperationException("A user with that email already exists.");
        }

        var roles = await NormalizeRolesAsync(request.Roles, cancellationToken);
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            EmailConfirmed = true,
            LockoutEnabled = true
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        EnsureSucceeded(createResult, "Unable to create the admin account.");

        var roleResult = await userManager.AddToRolesAsync(user, roles);
        EnsureSucceeded(roleResult, "Unable to assign roles to the new account.");

        await WriteAuditAsync("AdminUser", "Created", actor, correlationId, new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            Roles = roles
        }, cancellationToken);

        return await MapAsync(user, cancellationToken);
    }

    public async Task<AdminUserSummaryDto> DisableUserAsync(Guid userId, string actor, string? correlationId, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredUserAsync(userId);
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);

        var result = await userManager.UpdateAsync(user);
        EnsureSucceeded(result, "Unable to disable the selected user.");

        await WriteAuditAsync("AdminUser", "Disabled", actor, correlationId, new
        {
            user.Id,
            user.Email
        }, cancellationToken);

        return await MapAsync(user, cancellationToken);
    }

    public async Task<AdminUserSummaryDto> EnableUserAsync(Guid userId, string actor, string? correlationId, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredUserAsync(userId);
        user.LockoutEnabled = true;
        user.LockoutEnd = null;

        var updateResult = await userManager.UpdateAsync(user);
        EnsureSucceeded(updateResult, "Unable to enable the selected user.");

        await userManager.ResetAccessFailedCountAsync(user);

        await WriteAuditAsync("AdminUser", "Enabled", actor, correlationId, new
        {
            user.Id,
            user.Email
        }, cancellationToken);

        return await MapAsync(user, cancellationToken);
    }

    public async Task<AdminUserSummaryDto> ResetPasswordAsync(Guid userId, AdminResetPasswordRequest request, string actor, string? correlationId, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredUserAsync(userId);
        try
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
            EnsureSucceeded(result, "Unable to reset the selected user's password.");
        }
        catch (NotSupportedException)
        {
            var removeResult = await userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded && !removeResult.Errors.All(x => x.Code == "PasswordMismatch"))
            {
                EnsureSucceeded(removeResult, "Unable to remove the selected user's password.");
            }

            var addResult = await userManager.AddPasswordAsync(user, request.NewPassword);
            EnsureSucceeded(addResult, "Unable to reset the selected user's password.");
        }

        await WriteAuditAsync("AdminUser", "PasswordReset", actor, correlationId, new
        {
            user.Id,
            user.Email
        }, cancellationToken);

        return await MapAsync(user, cancellationToken);
    }

    public async Task<AdminUserSummaryDto> UpdateRolesAsync(Guid userId, AdminUpdateUserRolesRequest request, string actor, string? correlationId, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredUserAsync(userId);
        var requestedRoles = await NormalizeRolesAsync(request.Roles, cancellationToken);
        var existingRoles = await userManager.GetRolesAsync(user);

        var rolesToRemove = existingRoles.Except(requestedRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, rolesToRemove);
            EnsureSucceeded(removeResult, "Unable to remove existing user roles.");
        }

        var rolesToAdd = requestedRoles.Except(existingRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToAdd.Length > 0)
        {
            var addResult = await userManager.AddToRolesAsync(user, rolesToAdd);
            EnsureSucceeded(addResult, "Unable to assign the requested roles.");
        }

        await WriteAuditAsync("AdminUser", "RolesUpdated", actor, correlationId, new
        {
            user.Id,
            user.Email,
            Roles = requestedRoles
        }, cancellationToken);

        return await MapAsync(user, cancellationToken);
    }

    private async Task<ApplicationUser> GetRequiredUserAsync(Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user ?? throw new InvalidOperationException("The selected user was not found.");
    }

    private async Task<IReadOnlyCollection<string>> NormalizeRolesAsync(IEnumerable<string> roles, CancellationToken cancellationToken)
    {
        var normalized = roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            throw new InvalidOperationException("At least one role must be assigned.");
        }

        foreach (var role in normalized)
        {
            if (!KnownRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"'{role}' is not a supported FarmIQ admin role.");
            }

            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            }
        }

        return normalized;
    }

    private async Task<AdminUserSummaryDto> MapAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roles = await userManager.GetRolesAsync(user);
        return new AdminUserSummaryDto(
            user.Id,
            user.DisplayName,
            user.Email ?? string.Empty,
            IsEnabled(user),
            roles.ToArray(),
            user.LockoutEnd,
            user.AccessFailedCount);
    }

    private async Task WriteAuditAsync(string entityName, string action, string actor, string? correlationId, object payload, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            EntityName = entityName,
            Action = action,
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
            CorrelationId = correlationId,
            PayloadJson = JsonSerializer.Serialize(payload)
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsEnabled(ApplicationUser user) =>
        user.LockoutEnd is null || user.LockoutEnd <= DateTimeOffset.UtcNow;

    private static void EnsureSucceeded(IdentityResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        var details = string.Join(" ", result.Errors.Select(error => error.Description));
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(details) ? message : $"{message} {details}");
    }
}
