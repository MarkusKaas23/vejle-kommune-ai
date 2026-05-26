using Microsoft.AspNetCore.Mvc;

namespace VejleKommune.Code.Features.DocumentIngestion;

/// <summary>
/// Phase 7 – Generative Pipeline (Document → Content).
/// POST  /umbraco/api/vejle/ingest        → 202 Accepted + jobId
/// GET   /umbraco/api/vejle/ingest/{jobId} → DocumentIngestionJobState
///
/// The caller uploads a document as base64. The worker creates a new unpublished
/// newsArticle draft — rollback is the standard CMS operation (delete/trash the node).
/// There is intentionally no DELETE endpoint here: the node lives in the content tree
/// and is managed through the backoffice, not dismissed in memory like findings.
/// </summary>
[ApiController]
[Route("umbraco/api/vejle/ingest")]
public sealed class DocumentIngestionController : ControllerBase
{
    private readonly DocumentIngestionQueue _queue;

    public DocumentIngestionController(DocumentIngestionQueue queue)
    {
        _queue = queue;
    }

    /// <summary>
    /// Queue a document ingestion job.
    /// Supply the document as a base64-encoded string with its MIME type.
    /// Returns 202 Accepted with a job ID to poll for the result.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DocumentIngestionQueued), 202)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public IActionResult Ingest([FromBody] DocumentIngestionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentBase64))
            return BadRequest(new { error = "documentBase64 is required." });

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(request.DocumentBase64);
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "documentBase64 is not valid base64." });
        }

        if (bytes.Length == 0)
            return BadRequest(new { error = "Document is empty." });

        string mimeType = string.IsNullOrWhiteSpace(request.MimeType)
            ? "application/pdf"
            : request.MimeType;

        string jobId = Guid.NewGuid().ToString("N");

        DocumentIngestionJob job = new(
            JobId: jobId,
            DocumentBytes: bytes,
            MimeType: mimeType,
            Hint: request.Hint,
            QueuedAt: DateTimeOffset.UtcNow);

        _queue.Enqueue(job);

        return Accepted(new DocumentIngestionQueued(
            jobId,
            $"Ingestion queued. Poll /umbraco/api/vejle/ingest/{jobId} for status."));
    }

    /// <summary>
    /// Poll the status of a document ingestion job.
    /// On Completed, the response includes the key of the newly created content node.
    /// </summary>
    [HttpGet("{jobId}")]
    [ProducesResponseType(typeof(DocumentIngestionJobState), 200)]
    [ProducesResponseType(404)]
    public IActionResult Status(string jobId)
    {
        DocumentIngestionJobState? state = _queue.GetState(jobId);
        if (state is null)
            return NotFound(new { error = $"Job '{jobId}' not found." });

        return Ok(state);
    }
}

public sealed record DocumentIngestionRequest(
    /// <summary>Base64-encoded document bytes.</summary>
    string DocumentBase64,
    /// <summary>MIME type. Defaults to "application/pdf" if omitted.</summary>
    string? MimeType,
    /// <summary>Optional editorial hint passed to the AI (e.g. "Pressemeddelelse fra Teknik og Miljø").</summary>
    string? Hint);

public sealed record DocumentIngestionQueued(string JobId, string Message);
