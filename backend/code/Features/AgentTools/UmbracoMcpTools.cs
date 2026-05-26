using System.Text.Json;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using VejleKommune.Code.Features.Bootstrap;

namespace VejleKommune.Code.Features.AgentTools;

/// <summary>
/// Phase 8 – Agent Tool (MCP).
/// Implements the four Umbraco tools exposed to the LLM via MCP:
///
///   umbraco_list_content   – list child nodes under a path
///   umbraco_get_content    – read a node's properties
///   umbraco_search_content – full-text search across content
///   umbraco_create_draft   – create an unpublished newsArticle draft
///
/// Write tools (umbraco_create_draft) log an approval notice because at
/// L1 there is no backoffice approval gate. L2 would require an interactive
/// approval dialog before any write is executed.
/// </summary>
public sealed class UmbracoMcpTools
{
    private readonly IContentService _contentService;

    public UmbracoMcpTools(IContentService contentService)
    {
        _contentService = contentService;
    }

    // ── Tool definitions (returned by tools/list) ─────────────────────────

    public static IReadOnlyList<McpTool> Definitions { get; } =
    [
        new McpTool(
            Name: "umbraco_list_content",
            Description:
                "List immediate child content nodes under a given path or the site root. " +
                "Returns each node's key, name, content type, URL segment, and publish status.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string",
                        description = "URL path to list children of, e.g. '/nyheder'. Omit for root." },
                    culture = new { type = "string",
                        description = "Culture code, e.g. 'da-DK'. Defaults to da-DK." },
                },
                required = Array.Empty<string>(),
            }),

        new McpTool(
            Name: "umbraco_get_content",
            Description:
                "Get all property values for a content node identified by its GUID key. " +
                "Returns properties for the requested culture.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    key = new { type = "string",
                        description = "GUID key of the content node (from umbraco_list_content)." },
                    culture = new { type = "string",
                        description = "Culture code. Defaults to da-DK." },
                },
                required = new[] { "key" },
            }),

        new McpTool(
            Name: "umbraco_search_content",
            Description:
                "Search published content by keyword. Returns matching nodes with key, name, and content type.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string",
                        description = "Keyword or phrase to search for." },
                    contentTypeAlias = new { type = "string",
                        description = "Optional: limit results to this content type alias." },
                },
                required = new[] { "query" },
            }),

        new McpTool(
            Name: "umbraco_create_draft",
            Description:
                "Create a new unpublished newsArticle draft under the Nyheder list page. " +
                "⚠ WRITE OPERATION — this mutates the CMS. At L1 the caller is responsible " +
                "for confirming intent before calling this tool. " +
                "Returns the key and backoffice URL of the created node.",
            InputSchema: new
            {
                type = "object",
                properties = new
                {
                    headline    = new { type = "string", description = "Article headline (da-DK)." },
                    summary     = new { type = "string", description = "Short summary (da-DK)." },
                    body        = new { type = "string",
                        description = "Body text as HTML (da-DK). Use <p> and <ul>/<li>." },
                    publishedDate = new { type = "string",
                        description = "ISO-8601 date string, e.g. '2026-05-26'." },
                },
                required = new[] { "headline" },
            }),
    ];

    // ── Tool dispatch ─────────────────────────────────────────────────────

    public McpToolCallResult Dispatch(string toolName, JsonElement arguments)
    {
        return toolName switch
        {
            "umbraco_list_content"   => ListContent(arguments),
            "umbraco_get_content"    => GetContent(arguments),
            "umbraco_search_content" => SearchContent(arguments),
            "umbraco_create_draft"   => CreateDraft(arguments),
            _ => Error($"Unknown tool: {toolName}"),
        };
    }

    // ── umbraco_list_content ──────────────────────────────────────────────

    private McpToolCallResult ListContent(JsonElement args)
    {
        string culture = args.TryGetString("culture") ?? "da-DK";
        string? path   = args.TryGetString("path");

        IEnumerable<IContent> nodes;

        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            nodes = _contentService.GetRootContent();
        }
        else
        {
            // Find parent by matching node names along the path segments.
            IContent? parent = FindByPath(path);
            if (parent is null)
                return Error($"No content node found at path '{path}'.");
            nodes = _contentService.GetPagedChildren(parent.Id, 0, 50, out _);
        }

        var items = nodes.Select(n => new
        {
            key         = n.Key,
            name        = n.GetCultureName(culture) ?? n.Name,
            contentType = n.ContentType.Alias,
            published   = n.Published,
        }).ToList();

        return Ok(JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── umbraco_get_content ───────────────────────────────────────────────

    private McpToolCallResult GetContent(JsonElement args)
    {
        if (!args.TryGetProperty("key", out JsonElement keyEl) ||
            !Guid.TryParse(keyEl.GetString(), out Guid key))
            return Error("'key' must be a valid GUID string.");

        string culture = args.TryGetString("culture") ?? "da-DK";

        IContent? content = _contentService.GetById(key);
        if (content is null)
            return Error($"Content node '{key}' not found.");

        var props = new Dictionary<string, object?>();
        props["_name"]        = content.GetCultureName(culture) ?? content.Name;
        props["_contentType"] = content.ContentType.Alias;
        props["_key"]         = content.Key;
        props["_published"]   = content.Published;

        foreach (IProperty prop in content.Properties)
        {
            object? val = prop.GetValue(culture) ?? prop.GetValue();

            // Unwrap rich-text envelope for readability.
            if (val is string s && s.TrimStart().StartsWith('{'))
            {
                try
                {
                    using JsonDocument doc = JsonDocument.Parse(s);
                    if (doc.RootElement.TryGetProperty("markup", out JsonElement markup))
                        val = markup.GetString();
                }
                catch { }
            }

            props[prop.Alias] = val;
        }

        return Ok(JsonSerializer.Serialize(props, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── umbraco_search_content ────────────────────────────────────────────

    private McpToolCallResult SearchContent(JsonElement args)
    {
        if (!args.TryGetProperty("query", out JsonElement queryEl))
            return Error("'query' is required.");

        string query = queryEl.GetString() ?? string.Empty;
        string? typeFilter = args.TryGetString("contentTypeAlias");

        // Simple in-memory search: get all descendants of root and match on name/values.
        // Production package would use Examine (Lucene) index for performance.
        List<object> results = [];

        foreach (IContent root in _contentService.GetRootContent())
        {
            foreach (IContent node in _contentService.GetPagedDescendants(root.Id, 0, 200, out _))
            {
                if (typeFilter is not null &&
                    !node.ContentType.Alias.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool matches = node.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                    node.Properties.Any(p =>
                        p.GetValue()?.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
                        p.GetValue("da-DK")?.ToString()?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

                if (matches)
                    results.Add(new { key = node.Key, name = node.Name, contentType = node.ContentType.Alias });

                if (results.Count >= 20) break;
            }
            if (results.Count >= 20) break;
        }

        return Ok(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── umbraco_create_draft ──────────────────────────────────────────────

    private McpToolCallResult CreateDraft(JsonElement args)
    {
        string? headline      = args.TryGetString("headline");
        string? summary       = args.TryGetString("summary");
        string? body          = args.TryGetString("body");
        string? publishedDate = args.TryGetString("publishedDate");

        if (string.IsNullOrWhiteSpace(headline))
            return Error("'headline' is required.");

        // Find the newsListPage node by traversing root children — avoids GetPagedOfType
        // whose IQuery<IContent> filter parameter is non-nullable.
        IContent? parent = _contentService.GetRootContent()
            .SelectMany(root => _contentService.GetPagedChildren(root.Id, 0, 100, out _))
            .FirstOrDefault(n => n.ContentType.Alias.Equals(
                DoctypeKeys.NewsListPageAlias, StringComparison.OrdinalIgnoreCase));

        if (parent is null)
            return Error("No newsListPage node found — ensure content has been seeded.");

        IContent draft = _contentService.Create(headline, parent.Id, DoctypeKeys.NewsArticleAlias);
        draft.SetCultureName(headline, "da-DK");
        draft.SetValue("headline", headline, "da-DK");

        if (!string.IsNullOrWhiteSpace(summary))
            draft.SetValue("summary", summary, "da-DK");

        if (!string.IsNullOrWhiteSpace(body))
            draft.SetValue("body", JsonSerializer.Serialize(new { markup = body }), "da-DK");

        if (DateTime.TryParse(publishedDate, out DateTime parsedDate))
            draft.SetValue("publishedDate", parsedDate);

        _contentService.Save(draft);

        string backofficePath = $"/umbraco#/content/content/edit/{draft.Id}";

        return Ok(JsonSerializer.Serialize(new
        {
            contentKey      = draft.Key,
            name            = draft.Name,
            backofficePath,
            note = "⚠ Draft created (unpublished). Review in backoffice before publishing.",
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private IContent? FindByPath(string path)
    {
        string[] segments = path.Trim('/').Split('/');
        IEnumerable<IContent> roots = _contentService.GetRootContent().ToList();

        // Try matching first segment against a root node.
        IContent? current = roots.FirstOrDefault(n =>
            n.Name?.Equals(segments[0], StringComparison.OrdinalIgnoreCase) == true);

        // If not found at root level, search one level deeper (typical Umbraco tree has a
        // single home root — e.g. "Velkommen til Vejle Kommune" — with sections as children).
        if (current is null)
        {
            foreach (IContent root in roots)
            {
                current = _contentService.GetPagedChildren(root.Id, 0, 100, out _)
                    .FirstOrDefault(n => n.Name?.Equals(segments[0],
                        StringComparison.OrdinalIgnoreCase) == true);
                if (current is not null) break;
            }
        }

        // Descend remaining segments.
        for (int i = 1; i < segments.Length && current is not null; i++)
        {
            string seg = segments[i];
            current = _contentService.GetPagedChildren(current.Id, 0, 100, out _)
                .FirstOrDefault(n => n.Name?.Equals(seg, StringComparison.OrdinalIgnoreCase) == true);
        }

        return current;
    }

    private static McpToolCallResult Ok(string text) =>
        new([new McpContent("text", text)]);

    private static McpToolCallResult Error(string message) =>
        new([new McpContent("text", message)], IsError: true);
}

// ── Extension helpers for JsonElement ────────────────────────────────────

internal static class JsonElementExtensions
{
    public static string? TryGetString(this JsonElement el, string property) =>
        el.TryGetProperty(property, out JsonElement val) ? val.GetString() : null;
}
