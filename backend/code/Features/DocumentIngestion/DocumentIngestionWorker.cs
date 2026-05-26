using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using VejleKommune.Code.Features.Ai;

namespace VejleKommune.Code.Features.DocumentIngestion;

/// <summary>
/// Phase 7 – Generative Pipeline (Document → Content).
/// BackgroundService that drains DocumentIngestionQueue, sends the raw document
/// to Gemini (inline_data, PDF or DOCX) with a structured-output schema, and
/// creates a new unpublished newsArticle draft under the Nyheder list page.
///
/// Pipeline steps:
///   1. Resolve Nyheder parent node (newsListPage, key b517cfde-...) → int ID
///   2. Build AiRequest with the document bytes as AiImage inline_data
///   3. Gemini extracts: headline, summary, body (HTML), publishedDate
///   4. Create IContent of type "newsArticle" under Nyheder
///   5. SetCultureName + SetValue for culture-variant fields (da-DK)
///   6. SetValue for publishedDate (invariant DateTime)
///   7. contentService.Save → unpublished draft, editor reviews before publish
///
/// Rollback semantics: same as Async Transform — the CMS is mutated, so rollback
/// means deleting or trashing the draft node (standard CMS operation, not an API endpoint).
/// </summary>
public sealed class DocumentIngestionWorker : BackgroundService
{
    /// <summary>
    /// Umbraco key for the Nyheder newsListPage node (set during content seeding).
    /// New articles are created as children of this node.
    /// </summary>
    private static readonly Guid NyhederKey = new("b517cfde-dee4-4ae1-a8ce-bd734e0c6f94");

    private const string ContentTypeAlias = "newsArticle";
    private const string DefaultCulture = "da-DK";

    private readonly DocumentIngestionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAiProvider _aiProvider;
    private readonly ILogger<DocumentIngestionWorker> _logger;

    public DocumentIngestionWorker(
        DocumentIngestionQueue queue,
        IServiceScopeFactory scopeFactory,
        IAiProvider aiProvider,
        ILogger<DocumentIngestionWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _aiProvider = aiProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (DocumentIngestionJob job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _queue.SetState(job.JobId, new DocumentIngestionJobState(job.JobId, DocumentIngestionStatus.Running));
            _logger.LogInformation(
                "DocumentIngestion job {JobId} started: {MimeType} {Bytes} bytes",
                job.JobId, job.MimeType, job.DocumentBytes.Length);

            try
            {
                Guid contentKey = await IngestAsync(job, stoppingToken);

                _queue.SetState(job.JobId, new DocumentIngestionJobState(
                    job.JobId, DocumentIngestionStatus.Completed,
                    ContentKey: contentKey,
                    CompletedAt: DateTimeOffset.UtcNow));

                _logger.LogInformation(
                    "DocumentIngestion job {JobId} completed: new node {ContentKey}",
                    job.JobId, contentKey);
            }
            catch (Exception ex)
            {
                _queue.SetState(job.JobId, new DocumentIngestionJobState(
                    job.JobId, DocumentIngestionStatus.Failed, Error: ex.Message));

                _logger.LogError(ex, "DocumentIngestion job {JobId} failed.", job.JobId);
            }
        }
    }

    private async Task<Guid> IngestAsync(DocumentIngestionJob job, CancellationToken ct)
    {
        // ── Step 1: Resolve Nyheder parent node ──────────────────────────────
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IContentService contentService = scope.ServiceProvider.GetRequiredService<IContentService>();

        IContent? nyheder = contentService.GetById(NyhederKey);
        if (nyheder is null)
            throw new InvalidOperationException(
                $"Nyheder list node ({NyhederKey}) not found. Ensure content has been seeded.");

        int parentId = nyheder.Id;

        // ── Step 2: Send document to Gemini ──────────────────────────────────
        string hintClause = string.IsNullOrWhiteSpace(job.Hint)
            ? string.Empty
            : $" Kontekst fra redaktøren: \"{job.Hint}\".";

        const string SystemPrompt =
            "Du er en redaktionel assistent for Vejle Kommune. " +
            "Du modtager et dokument (pressemeddelelse, dagsorden eller lignende). " +
            "Udtræk de vigtigste informationer og returner dem som et JSON-objekt " +
            "med fire felter: headline, summary, body og publishedDate. " +
            "headline: en kort, klar overskrift (max 100 tegn). " +
            "summary: en neutral manchet på 1-2 sætninger. " +
            "body: brødtekst som HTML (brug <p> og <ul>/<li> — ingen <h1>/<h2>). " +
            "publishedDate: dato i ISO-8601-format (YYYY-MM-DD) baseret på dokumentets dato; " +
            "brug dags dato hvis dokumentet ikke angiver en dato. " +
            "Skriv i en klar, borgervenlig tone.";

        const string JsonSchema = """
            {
              "type": "object",
              "properties": {
                "headline":      { "type": "string" },
                "summary":       { "type": "string" },
                "body":          { "type": "string" },
                "publishedDate": { "type": "string" }
              },
              "required": ["headline", "summary", "body", "publishedDate"]
            }
            """;

        AiRequest request = new(
            Prompt: $"Analyser følgende dokument og ekstrahér indholdet som angivet.{hintClause}",
            SystemPrompt: SystemPrompt,
            Images: [new AiImage(job.DocumentBytes, job.MimeType)],
            JsonResponseSchema: JsonSchema,
            CallContext: new AiCallContext("GenerativePipeline-DocumentIngestion", job.JobId));

        AiResponse response = await _aiProvider.CompleteAsync(request, ct);

        // ── Step 3: Parse Gemini response ─────────────────────────────────────
        ExtractedArticle? extracted =
            JsonSerializer.Deserialize<ExtractedArticle>(response.Content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (extracted is null || string.IsNullOrWhiteSpace(extracted.Headline))
            throw new InvalidOperationException("Gemini returned an empty or unparseable extraction.");

        _logger.LogInformation(
            "DocumentIngestion job {JobId}: extracted headline='{Headline}', publishedDate={Date}",
            job.JobId, extracted.Headline, extracted.PublishedDate);

        // ── Step 4: Create draft content node ────────────────────────────────
        IContent draft = contentService.Create(extracted.Headline, parentId, ContentTypeAlias);

        // Register da-DK culture variant (required to create ContentCultureInfo entry —
        // same pattern as TranslationWorker; without this the variant shows as "Not created").
        draft.SetCultureName(extracted.Headline, DefaultCulture);

        // Culture-variant text fields.
        draft.SetValue("headline", extracted.Headline, DefaultCulture);
        draft.SetValue("summary", extracted.Summary ?? string.Empty, DefaultCulture);

        // body is stored as Umbraco rich-text JSON envelope: {"markup":"<html>"}
        if (!string.IsNullOrWhiteSpace(extracted.Body))
            draft.SetValue("body", WrapMarkup(extracted.Body), DefaultCulture);

        // publishedDate is invariant (not culture-variant per newsarticle.config).
        if (DateTime.TryParse(extracted.PublishedDate, out DateTime parsedDate))
            draft.SetValue("publishedDate", parsedDate);

        // ── Step 5: Save as unpublished draft ────────────────────────────────
        // The editor reviews and publishes from the backoffice — this is the third
        // use of the "unpublished-by-default review gate" across Phases 5, 6, and 7.
        contentService.Save(draft);

        return draft.Key;
    }

    /// <summary>Wraps an HTML string into Umbraco's rich-text JSON envelope.</summary>
    private static string WrapMarkup(string html) =>
        JsonSerializer.Serialize(new { markup = html });

    // ── Deserialization target ───────────────────────────────────────────────

    private sealed record ExtractedArticle(
        string Headline,
        string? Summary,
        string? Body,
        string? PublishedDate);
}
