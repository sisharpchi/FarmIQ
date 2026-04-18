using FarmIQ.Application.Abstractions;
using FarmIQ.Application.Contracts;
using FarmIQ.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FarmIQ.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,Ops,Analyst")]
[Route("api/admin")]
public sealed class AdminController(IAdminQueryService adminQueryService) : ControllerBase
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
}
