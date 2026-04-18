using FarmIQ.API.Middleware;
using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FarmIQ.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Ops,Analyst")]
[Route("api/admin")]
public sealed class AdminController(
    IAdminQueryService adminQueryService,
    IAdminUserManagementService adminUserManagementService) : ControllerBase
{
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await adminQueryService.GetConversationsAsync(page, pageSize, cancellationToken));

    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs([FromQuery] ProcessingJobStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await adminQueryService.GetJobsAsync(status, page, pageSize, cancellationToken));

    [HttpPost("jobs/retry")]
    [Authorize(Roles = "Admin,Ops")]
    public async Task<IActionResult> RetryJob([FromBody] AdminReplayRequest request, CancellationToken cancellationToken)
    {
        await adminQueryService.RetryJobAsync(request.ProcessingJobId, cancellationToken);
        return Accepted(new { request.ProcessingJobId, status = "queued" });
    }

    [HttpGet("advisories")]
    public async Task<IActionResult> GetAdvisories([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await adminQueryService.GetAdvisoriesAsync(page, pageSize, cancellationToken));

    [HttpGet("conversations/{conversationId:guid}")]
    public async Task<IActionResult> GetConversationDetail(Guid conversationId, CancellationToken cancellationToken = default)
    {
        var result = await adminQueryService.GetConversationDetailAsync(conversationId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("advisories/{advisoryId:guid}")]
    public async Task<IActionResult> GetAdvisoryDetail(Guid advisoryId, CancellationToken cancellationToken = default)
    {
        var result = await adminQueryService.GetAdvisoryDetailAsync(advisoryId, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("deliveries/issues")]
    public async Task<IActionResult> GetDeliveryIssues([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await adminQueryService.GetDeliveryIssuesAsync(page, pageSize, cancellationToken));

    [HttpGet("jobs/stuck")]
    public async Task<IActionResult> GetStuckJobs([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await adminQueryService.GetStuckJobsAsync(page, pageSize, cancellationToken));

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics(CancellationToken cancellationToken = default) =>
        Ok(await adminQueryService.GetAnalyticsAsync(cancellationToken));

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken = default) =>
        Ok(await adminQueryService.GetSystemStatusAsync(cancellationToken));

    [HttpGet("session")]
    public async Task<IActionResult> GetSession() =>
        Ok(await adminQueryService.GetSessionAsync(User));

    [HttpGet("insights/anonymized")]
    [Authorize(Roles = "Admin,Analyst")]
    public async Task<IActionResult> GetAnonymizedInsights(CancellationToken cancellationToken = default)
    {
        var analytics = await adminQueryService.GetAnalyticsAsync(cancellationToken);
        return Ok(new
        {
            analytics.TotalFarmers,
            analytics.TotalConversations,
            analytics.CompletedAdvisories,
            analytics.FailedJobs
        });
    }

    [HttpGet("users")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        Ok(await adminUserManagementService.GetUsersAsync(page, pageSize, cancellationToken));

    [HttpPost("users")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> CreateUser([FromBody] AdminCreateUserRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            async () =>
            {
                var created = await adminUserManagementService.CreateUserAsync(request, GetActor(), GetCorrelationId(), cancellationToken);
                return Created($"/api/admin/users/{created.UserId}", created);
            });

    [HttpPost("users/{userId:guid}/disable")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> DisableUser(Guid userId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await adminUserManagementService.DisableUserAsync(userId, GetActor(), GetCorrelationId(), cancellationToken)));

    [HttpPost("users/{userId:guid}/enable")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> EnableUser(Guid userId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await adminUserManagementService.EnableUserAsync(userId, GetActor(), GetCorrelationId(), cancellationToken)));

    [HttpPost("users/{userId:guid}/reset-password")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> ResetPassword(Guid userId, [FromBody] AdminResetPasswordRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await adminUserManagementService.ResetPasswordAsync(userId, request, GetActor(), GetCorrelationId(), cancellationToken)));

    [HttpPost("users/{userId:guid}/roles")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> UpdateRoles(Guid userId, [FromBody] AdminUpdateUserRolesRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () => Ok(await adminUserManagementService.UpdateRolesAsync(userId, request, GetActor(), GetCorrelationId(), cancellationToken)));

    private string GetActor() => User.Identity?.Name ?? User.Identity?.AuthenticationType ?? "system";

    private string? GetCorrelationId() => HttpContext.Items[CorrelationIdMiddleware.HeaderName]?.ToString();

    private async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                error = "admin_operation_failed",
                error_description = exception.Message
            });
        }
    }
}
