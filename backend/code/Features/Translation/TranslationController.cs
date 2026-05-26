using Microsoft.AspNetCore.Mvc;

namespace VejleKommune.Code.Features.Translation;

/// <summary>
/// Phase 5 – Async Transform (translation).
/// Accepts a translation request and returns 202 Accepted immediately.
/// The actual translation runs in TranslationWorker (background service).
/// Poll /status/{jobId} to check progress.
/// </summary>
[ApiController]
[Route("umbraco/api/vejle/translation")]
public sealed class TranslationController : ControllerBase
{
    private readonly TranslationQueue _queue;

    public TranslationController(TranslationQueue queue)
    {
        _queue = queue;
    }

    /// <summary>
    /// Queue a translation job. Returns 202 Accepted with a job ID.
    /// </summary>
    [HttpPost("translate")]
    [ProducesResponseType(typeof(TranslationJobQueued), 202)]
    public IActionResult Translate([FromBody] TranslateRequest request)
    {
        string jobId = Guid.NewGuid().ToString("N");

        TranslationJob job = new(
            JobId: jobId,
            ContentKey: request.ContentKey,
            SourceCulture: request.SourceCulture,
            TargetCulture: request.TargetCulture,
            QueuedAt: DateTimeOffset.UtcNow);

        _queue.Enqueue(job);

        return Accepted(new TranslationJobQueued(jobId,
            $"Translation queued. Poll /umbraco/api/vejle/translation/status/{jobId} for progress."));
    }

    /// <summary>
    /// Poll the status of a translation job.
    /// </summary>
    [HttpGet("status/{jobId}")]
    [ProducesResponseType(typeof(TranslationJobState), 200)]
    [ProducesResponseType(404)]
    public IActionResult Status(string jobId)
    {
        TranslationJobState? state = _queue.GetState(jobId);
        if (state is null)
            return NotFound(new { error = $"Job '{jobId}' not found." });

        return Ok(state);
    }
}

public sealed record TranslateRequest(
    /// <summary>Content node key to translate.</summary>
    Guid ContentKey,
    /// <summary>Source culture, e.g. "da-DK".</summary>
    string SourceCulture,
    /// <summary>Target culture, e.g. "en-US".</summary>
    string TargetCulture);

public sealed record TranslationJobQueued(string JobId, string Message);
