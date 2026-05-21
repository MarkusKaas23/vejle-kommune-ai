using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Extensions;
using VejleKommune.Code.Features.Bootstrap;

namespace VejleKommune.Code.Features.Seo;

/// <summary>
/// Deterministic Schema.org JSON-LD renderer for the Schema enrichment Pattern.
/// Maps doctype alias → default Schema.org type per ADR-0006.
/// The renderer is design-time, not request-path-AI; the runtime is pure.
/// </summary>
public sealed class SchemaOrgRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly IPublishedUrlProvider _urlProvider;
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<SchemaOrgRenderer> _logger;

    public SchemaOrgRenderer(
        IVariationContextAccessor variationContextAccessor,
        IPublishedUrlProvider urlProvider,
        IPublishedValueFallback publishedValueFallback,
        ILogger<SchemaOrgRenderer> logger)
    {
        _variationContextAccessor = variationContextAccessor;
        _urlProvider = urlProvider;
        _fallback = publishedValueFallback;
        _logger = logger;
    }

    public string RenderJsonLd(IPublishedContent page)
    {
        var blocks = BuildBlocks(page).ToList();
        if (blocks.Count == 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        foreach (var block in blocks)
        {
            sb.Append("<script type=\"application/ld+json\">");
            sb.Append(block.ToJsonString(JsonOptions));
            sb.Append("</script>");
        }
        return sb.ToString();
    }

    private IEnumerable<JsonObject> BuildBlocks(IPublishedContent page)
    {
        var culture = _variationContextAccessor.VariationContext?.Culture ?? LanguageBootstrap.Danish;
        var url = page.Url(_urlProvider, culture, UrlMode.Absolute);
        var inLanguage = string.IsNullOrWhiteSpace(culture) ? LanguageBootstrap.Danish : culture;

        var typeOverride = Str(page, "schemaTypeOverride");
        if (!string.IsNullOrWhiteSpace(typeOverride))
        {
            yield return BuildWebPage(page, url, inLanguage, schemaType: typeOverride!);
        }
        else
        {
            foreach (var block in DefaultBlocks(page, url, inLanguage))
                yield return block;
        }

        var additional = Str(page, "additionalJsonLd");
        if (!string.IsNullOrWhiteSpace(additional))
        {
            if (TryParseObject(additional!, out var parsed))
                yield return parsed!;
            else
                _logger.LogWarning("Page {Id} has invalid additionalJsonLd; skipping", page.Id);
        }
    }

    private IEnumerable<JsonObject> DefaultBlocks(IPublishedContent page, string url, string inLanguage)
    {
        switch (page.ContentType.Alias)
        {
            case DoctypeKeys.HomePageAlias:
                yield return BuildWebSite(page, url, inLanguage);
                yield return BuildOrganization(url, inLanguage);
                break;

            case DoctypeKeys.NewsArticleAlias:
                yield return BuildNewsArticle(page, url, inLanguage);
                break;

            case DoctypeKeys.NewsListPageAlias:
            case DoctypeKeys.EventListPageAlias:
                yield return BuildCollectionPage(page, url, inLanguage);
                break;

            case DoctypeKeys.EventAlias:
                yield return BuildEvent(page, url, inLanguage);
                break;

            case DoctypeKeys.ServicePageAlias:
                yield return BuildGovernmentService(page, url, inLanguage);
                break;

            case DoctypeKeys.ContentPageAlias:
            default:
                yield return BuildWebPage(page, url, inLanguage, schemaType: "WebPage");
                break;
        }
    }

    private JsonObject BuildWebSite(IPublishedContent page, string url, string inLanguage)
    {
        var obj = NewSchemaObject("WebSite", url);
        obj["name"] = page.Name;
        obj["inLanguage"] = inLanguage;
        var description = Str(page, "seoMetaDescription") ?? Str(page, "introText");
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;
        return obj;
    }

    private static JsonObject BuildOrganization(string url, string inLanguage)
    {
        var obj = NewSchemaObject("GovernmentOrganization", url + "#organization");
        obj["name"] = "Vejle Kommune";
        obj["url"] = url;
        obj["inLanguage"] = inLanguage;
        return obj;
    }

    private JsonObject BuildNewsArticle(IPublishedContent page, string url, string inLanguage)
    {
        var obj = NewSchemaObject("NewsArticle", url);
        obj["headline"] = Str(page, "headline") ?? page.Name ?? string.Empty;
        obj["inLanguage"] = inLanguage;

        var summary = Str(page, "summary") ?? Str(page, "seoMetaDescription");
        if (!string.IsNullOrWhiteSpace(summary))
            obj["description"] = summary;

        var published = Date(page, "publishedDate");
        if (published.HasValue)
            obj["datePublished"] = published.Value.ToString("o");

        return obj;
    }

    private JsonObject BuildCollectionPage(IPublishedContent page, string url, string inLanguage)
    {
        var obj = NewSchemaObject("CollectionPage", url);
        obj["name"] = Str(page, "headline") ?? page.Name ?? string.Empty;
        obj["inLanguage"] = inLanguage;
        var description = Str(page, "introText") ?? Str(page, "seoMetaDescription");
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;
        return obj;
    }

    private JsonObject BuildEvent(IPublishedContent page, string url, string inLanguage)
    {
        var obj = NewSchemaObject("Event", url);
        obj["name"] = Str(page, "headline") ?? page.Name ?? string.Empty;
        obj["inLanguage"] = inLanguage;

        var start = Date(page, "startDate");
        if (start.HasValue)
            obj["startDate"] = start.Value.ToString("o");

        var end = Date(page, "endDate");
        if (end.HasValue)
            obj["endDate"] = end.Value.ToString("o");

        var location = Str(page, "location");
        if (!string.IsNullOrWhiteSpace(location))
        {
            obj["location"] = new JsonObject
            {
                ["@type"] = "Place",
                ["name"] = location,
            };
        }
        return obj;
    }

    private JsonObject BuildGovernmentService(IPublishedContent page, string url, string inLanguage)
    {
        var obj = NewSchemaObject("GovernmentService", url);
        obj["name"] = Str(page, "headline") ?? page.Name ?? string.Empty;
        obj["inLanguage"] = inLanguage;

        var description = Str(page, "summary") ?? Str(page, "seoMetaDescription");
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;

        obj["provider"] = new JsonObject
        {
            ["@type"] = "GovernmentOrganization",
            ["name"] = "Vejle Kommune",
        };
        return obj;
    }

    private JsonObject BuildWebPage(IPublishedContent page, string url, string inLanguage, string schemaType)
    {
        var obj = NewSchemaObject(schemaType, url);
        obj["name"] = Str(page, "headline") ?? page.Name ?? string.Empty;
        obj["inLanguage"] = inLanguage;

        var description = Str(page, "seoMetaDescription");
        if (!string.IsNullOrWhiteSpace(description))
            obj["description"] = description;
        return obj;
    }

    private static JsonObject NewSchemaObject(string type, string url) => new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = type,
        ["url"] = url,
    };

    private string? Str(IPublishedContent page, string alias)
    {
        if (!page.HasProperty(alias)) return null;
        var value = page.Value<string>(_fallback, alias);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private DateTime? Date(IPublishedContent page, string alias)
    {
        if (!page.HasProperty(alias)) return null;
        var value = page.Value<DateTime?>(_fallback, alias);
        return value.HasValue && value.Value != DateTime.MinValue ? value : null;
    }

    private static bool TryParseObject(string raw, out JsonObject? result)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            if (node is JsonObject obj)
            {
                result = obj;
                return true;
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        result = null;
        return false;
    }
}
