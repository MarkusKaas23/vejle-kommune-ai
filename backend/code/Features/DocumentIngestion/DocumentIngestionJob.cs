namespace VejleKommune.Code.Features.DocumentIngestion;

/// <summary>
/// Phase 7 – Generative Pipeline (Document → Content).
/// Represents a queued document-to-content job: a raw document (PDF or Word)
/// is sent to Gemini, which extracts structured newsArticle fields, and a new
/// unpublished draft node is created under the Nyheder list page for editorial review.
/// </summary>
public sealed record DocumentIngestionJob(
    /// <summary>Unique job identifier.</summary>
    string JobId,
    /// <summary>Raw document bytes (PDF or DOCX).</summary>
    byte[] DocumentBytes,
    /// <summary>MIME type, e.g. "application/pdf" or
    /// "application/vnd.openxmlformats-officedocument.wordprocessingml.document".</summary>
    string MimeType,
    /// <summary>Optional editorial hint passed to Gemini (e.g. source, context).</summary>
    string? Hint,
    /// <summary>When the job was queued.</summary>
    DateTimeOffset QueuedAt);

public sealed record DocumentIngestionJobState(
    string JobId,
    DocumentIngestionStatus Status,
    /// <summary>Key of the newly created content node (populated on Completed).</summary>
    Guid? ContentKey = null,
    string? Error = null,
    DateTimeOffset? CompletedAt = null);

public enum DocumentIngestionStatus
{
    Queued,
    Running,
    Completed,
    Failed,
}
