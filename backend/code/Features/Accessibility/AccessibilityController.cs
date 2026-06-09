using Microsoft.AspNetCore.Mvc;

namespace VejleKommune.Code.Features.Accessibility;

/// <summary>
/// Phase 6 – Async Analyze (accessibility audit).
/// POST  /umbraco/api/vejle/accessibility/audit     → 202 Accepted + jobId
/// GET   /umbraco/api/vejle/accessibility/findings/{jobId} → AccessibilityJobState
/// DELETE /umbraco/api/vejle/accessibility/findings/{jobId} → 204 (dismiss)
///
/// The DELETE endpoint is architecturally load-bearing: it models the trivial
/// rollback semantics that distinguish Async analyze from Async transform.
/// </summary>
[ApiController]
[Route("umbraco/api/vejle/accessibility")]
public sealed class AccessibilityController : ControllerBase
{
    private readonly AccessibilityQueue _queue;

    public AccessibilityController(AccessibilityQueue queue)
    {
        _queue = queue;
    }

    /// <summary>Queue an accessibility audit job. Returns 202 Accepted with a job ID.</summary>
    [HttpPost("audit")]
    [ProducesResponseType(typeof(AccessibilityJobQueued), 202)]
    public IActionResult Audit([FromBody] AccessibilityAuditRequest request)
    {
        string jobId = Guid.NewGuid().ToString("N");

        AccessibilityJob job = new(
            JobId: jobId,
            ContentKey: request.ContentKey,
            Culture: request.Culture,
            QueuedAt: DateTimeOffset.UtcNow);

        _queue.Enqueue(job);

        return Accepted(new AccessibilityJobQueued(jobId,
            $"Audit queued. Poll /umbraco/api/vejle/accessibility/findings/{jobId} for results."));
    }

    /// <summary>Poll findings for a completed audit job.</summary>
    [HttpGet("findings/{jobId}")]
    [ProducesResponseType(typeof(AccessibilityJobState), 200)]
    [ProducesResponseType(404)]
    public IActionResult Findings(string jobId)
    {
        AccessibilityJobState? state = _queue.GetState(jobId);
        if (state is null)
            return NotFound(new { error = $"Job '{jobId}' not found." });

        return Ok(state);
    }

    /// <summary>
    /// Dismiss all findings for a job.
    /// This is the rollback operation for the Async analyze Pattern:
    /// trivially reversible — the CMS was never mutated.
    /// </summary>
    [HttpDelete("findings/{jobId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public IActionResult Dismiss(string jobId)
    {
        if (!_queue.Dismiss(jobId))
            return NotFound(new { error = $"Job '{jobId}' not found." });

        return NoContent();
    }
}

public sealed record AccessibilityAuditRequest(
    /// <summary>Content node key to audit.</summary>
    Guid ContentKey,
    /// <summary>Culture to read content from, e.g. "da-DK".</summary>
    string Culture);

public sealed record AccessibilityJobQueued(string JobId, string Message);
