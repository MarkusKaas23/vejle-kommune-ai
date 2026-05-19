using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace VejleKommune.Code.Features.Bootstrap;

internal sealed record ResolvedTemplates(
    ITemplate HomePage,
    ITemplate NewsListPage,
    ITemplate NewsArticle,
    ITemplate EventListPage,
    ITemplate Event,
    ITemplate ServicePage,
    ITemplate ContentPage);

internal static class TemplateFactory
{
    public static ResolvedTemplates CreateAll(ITemplateService templateService, IShortStringHelper shortStringHelper)
    {
        var home = CreateOrGet(templateService, "Home page", DoctypeKeys.HomePageAlias, DoctypeKeys.HomePageTemplate, HomePageRazor);
        var newsList = CreateOrGet(templateService, "News list page", DoctypeKeys.NewsListPageAlias, DoctypeKeys.NewsListPageTemplate, ListPageRazor);
        var newsArticle = CreateOrGet(templateService, "News article", DoctypeKeys.NewsArticleAlias, DoctypeKeys.NewsArticleTemplate, ArticleRazor);
        var eventList = CreateOrGet(templateService, "Event list page", DoctypeKeys.EventListPageAlias, DoctypeKeys.EventListPageTemplate, ListPageRazor);
        var evt = CreateOrGet(templateService, "Event", DoctypeKeys.EventAlias, DoctypeKeys.EventTemplate, EventRazor);
        var service = CreateOrGet(templateService, "Service page", DoctypeKeys.ServicePageAlias, DoctypeKeys.ServicePageTemplate, ServicePageRazor);
        var content = CreateOrGet(templateService, "Content page", DoctypeKeys.ContentPageAlias, DoctypeKeys.ContentPageTemplate, ContentPageRazor);

        return new ResolvedTemplates(home, newsList, newsArticle, eventList, evt, service, content);
    }

    private static ITemplate CreateOrGet(ITemplateService templateService, string name, string alias, Guid templateKey, string content)
    {
        var existing = templateService.GetAsync(alias).GetAwaiter().GetResult();
        if (existing is not null) return existing;

        var attempt = templateService
            .CreateAsync(name, alias, content, Constants.Security.SuperUserKey, templateKey)
            .GetAwaiter()
            .GetResult();

        if (!attempt.Success || attempt.Result is null)
            throw new InvalidOperationException($"Failed to create template '{alias}': {attempt.Status}");

        return attempt.Result;
    }

    private const string LayoutDirective =
        """
        @inherits Umbraco.Cms.Web.Common.Views.UmbracoViewPage
        """;

    private static readonly string HomePageRazor =
        $$"""
        {{LayoutDirective}}
        <section>
            <h1>@(Model.Value<string>("headline") ?? Model.Name)</h1>
            <p>@Model.Value("introText")</p>
        </section>
        <section>
            <h2>Nyheder, begivenheder og service</h2>
            <ul>
                @foreach (var child in Model.Children?.Where(c => c.IsPublished()) ?? Enumerable.Empty<IPublishedContent>())
                {
                    <li><a href="@child.Url()">@child.Name</a></li>
                }
            </ul>
        </section>
        """;

    private static readonly string ListPageRazor =
        $$"""
        {{LayoutDirective}}
        <section>
            <h1>@(Model.Value<string>("headline") ?? Model.Name)</h1>
            <p>@Model.Value("introText")</p>
        </section>
        <ul>
            @foreach (var child in Model.Children?.Where(c => c.IsPublished()) ?? Enumerable.Empty<IPublishedContent>())
            {
                <li><a href="@child.Url()">@child.Name</a></li>
            }
        </ul>
        """;

    private static readonly string ArticleRazor =
        $$"""
        {{LayoutDirective}}
        <article>
            <h1>@(Model.Value<string>("headline") ?? Model.Name)</h1>
            @{ var published = Model.Value<DateTime?>("publishedDate"); }
            @if (published.HasValue)
            {
                <time datetime="@published.Value.ToString("o")">@published.Value.ToString("d. MMMM yyyy")</time>
            }
            <p><strong>@Model.Value("summary")</strong></p>
            @Html.Raw(Model.Value("body"))
        </article>
        """;

    private static readonly string EventRazor =
        $$"""
        {{LayoutDirective}}
        <article>
            <h1>@(Model.Value<string>("headline") ?? Model.Name)</h1>
            @{ var start = Model.Value<DateTime?>("startDate"); var end = Model.Value<DateTime?>("endDate"); }
            @if (start.HasValue)
            {
                <p><time datetime="@start.Value.ToString("o")">@start.Value.ToString("d. MMMM yyyy HH:mm")</time>@(end.HasValue ? " – " + end.Value.ToString("HH:mm") : "")</p>
            }
            <p>Sted: @Model.Value("location")</p>
            @Html.Raw(Model.Value("body"))
        </article>
        """;

    private static readonly string ServicePageRazor =
        $$"""
        {{LayoutDirective}}
        <article>
            <h1>@(Model.Value<string>("headline") ?? Model.Name)</h1>
            <p><strong>@Model.Value("summary")</strong></p>
            @Html.Raw(Model.Value("body"))
            @{ var links = Model.Value<string>("selfServiceLinks"); }
            @if (!string.IsNullOrWhiteSpace(links))
            {
                <aside><h2>Selvbetjening</h2><p>@links</p></aside>
            }
        </article>
        """;

    private static readonly string ContentPageRazor =
        $$"""
        {{LayoutDirective}}
        <article>
            <h1>@(Model.Value<string>("headline") ?? Model.Name)</h1>
            @Html.Raw(Model.Value("body"))
        </article>
        """;
}
