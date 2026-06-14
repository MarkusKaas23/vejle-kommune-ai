using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace VejleKommune.Code.Features.Bootstrap;

internal static class DoctypeFactory
{
    public static async Task CreateAllAsync(
        IContentTypeService contentTypeService,
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        ResolvedCompositions compositions,
        ResolvedTemplates templates)
    {
        var newsArticle = BuildNewsArticle(helper, dt, compositions, templates);
        await contentTypeService.CreateAsync(newsArticle, Constants.Security.SuperUserKey);

        var evt = BuildEvent(helper, dt, compositions, templates);
        await contentTypeService.CreateAsync(evt, Constants.Security.SuperUserKey);

        var servicePage = BuildServicePage(helper, dt, compositions, templates);
        await contentTypeService.CreateAsync(servicePage, Constants.Security.SuperUserKey);

        var contentPage = BuildContentPage(helper, dt, compositions, templates);
        await contentTypeService.CreateAsync(contentPage, Constants.Security.SuperUserKey);

        var newsList = BuildNewsListPage(helper, dt, compositions, templates, newsArticle);
        await contentTypeService.CreateAsync(newsList, Constants.Security.SuperUserKey);

        var eventList = BuildEventListPage(helper, dt, compositions, templates, evt);
        await contentTypeService.CreateAsync(eventList, Constants.Security.SuperUserKey);

        var subsiteNewsArticle = BuildSubSiteNewsArticle(helper, dt);
        await contentTypeService.CreateAsync(subsiteNewsArticle, Constants.Security.SuperUserKey);

        var subsiteNewsList = BuildSubSiteNewsListPage(helper, dt, subsiteNewsArticle);
        await contentTypeService.CreateAsync(subsiteNewsList, Constants.Security.SuperUserKey);

        var home = BuildHomePage(helper, dt, compositions, templates, newsList, eventList, servicePage, contentPage, subsiteNewsList);
        await contentTypeService.CreateAsync(home, Constants.Security.SuperUserKey);

        var siteSettings = BuildSiteSettings(helper, dt);
        await contentTypeService.CreateAsync(siteSettings, Constants.Security.SuperUserKey);
    }

    private static ContentType BuildHomePage(
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        ResolvedCompositions comps,
        ResolvedTemplates tpl,
        IContentType newsList,
        IContentType eventList,
        IContentType servicePage,
        IContentType contentPage,
        IContentType subsiteNewsList)
    {
        var ct = NewPublic(helper, DoctypeKeys.HomePage, DoctypeKeys.HomePageAlias, "Home page", "Site root. Mapped to Schema.org WebSite + GovernmentOrganization (multi-block exception per ADR-0006).", "icon-home");
        ct.AllowedAsRoot = true;
        ApplyAllCompositions(ct, comps);

        ct.AddPropertyGroup("hero", "Hero");
        Add(ct, helper, dt.Textstring, "headline", "Headline", "hero", ContentVariation.Culture);
        Add(ct, helper, dt.Textarea, "introText", "Intro text", "hero", ContentVariation.Culture);

        AttachTemplate(ct, tpl.HomePage);
        ct.AllowedContentTypes = new[]
        {
            new ContentTypeSort(newsList.Key, 0, newsList.Alias),
            new ContentTypeSort(eventList.Key, 1, eventList.Alias),
            new ContentTypeSort(servicePage.Key, 2, servicePage.Alias),
            new ContentTypeSort(contentPage.Key, 3, contentPage.Alias),
            new ContentTypeSort(subsiteNewsList.Key, 4, subsiteNewsList.Alias),
        };
        return ct;
    }

    /// <summary>
    /// Subsite news article — mirrors newsArticle but with deliberate differences to demo
    /// the three conversion scenarios in ContentConverter:
    ///   MATCHED      : headline, publishedDate, summary, image (same alias + same editor)
    ///   TYPE MISMATCH: body (TextBox here vs TextArea on newsArticle — same alias, wrong type)
    ///   UNMATCHED    : subsiteCategory (no equivalent on newsArticle)
    /// </summary>
    private static ContentType BuildSubSiteNewsArticle(IShortStringHelper helper, ResolvedDataTypes dt)
    {
        var ct = new ContentType(helper, -1)
        {
            Key = DoctypeKeys.SubSiteNewsArticle,
            Alias = DoctypeKeys.SubSiteNewsArticleAlias,
            Name = "Subsite news article",
            Description = "Subsite variant of the main-site news article. Used to demo ContentConverter (ADR-0016).",
            Icon = "icon-newspaper",
            Variations = ContentVariation.Culture,
        };

        ct.AddPropertyGroup("article", "Article");
        Add(ct, helper, dt.Textstring,    "headline",        "Headline",         "article", ContentVariation.Culture);
        Add(ct, helper, dt.DateTimePicker,"publishedDate",   "Published date",   "article", ContentVariation.Nothing);
        Add(ct, helper, dt.Textarea,      "summary",         "Summary",          "article", ContentVariation.Culture);
        // Intentional type mismatch: newsArticle uses TextArea, subsite uses TextBox
        Add(ct, helper, dt.Textstring,    "body",            "Body",             "article", ContentVariation.Culture);
        Add(ct, helper, dt.MediaPicker,   "image",           "Image",            "article", ContentVariation.Nothing);
        // Intentional unmatched property: no equivalent on newsArticle
        Add(ct, helper, dt.Textstring,    "subsiteCategory", "Subsite category", "article", ContentVariation.Culture);

        return ct;
    }

    private static ContentType BuildSubSiteNewsListPage(
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        IContentType subsiteNewsArticle)
    {
        var ct = new ContentType(helper, -1)
        {
            Key = DoctypeKeys.SubSiteNewsListPage,
            Alias = DoctypeKeys.SubSiteNewsListPageAlias,
            Name = "Subsite news list",
            Description = "Subsite news listing. Children are veSubSiteNewsArticle nodes.",
            Icon = "icon-list",
            Variations = ContentVariation.Culture,
        };

        ct.AddPropertyGroup("listing", "Listing");
        Add(ct, helper, dt.Textstring, "headline", "Headline", "listing", ContentVariation.Culture);

        ct.AllowedContentTypes = new[]
        {
            new ContentTypeSort(subsiteNewsArticle.Key, 0, subsiteNewsArticle.Alias),
        };
        return ct;
    }

    private static ContentType BuildNewsListPage(
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        ResolvedCompositions comps,
        ResolvedTemplates tpl,
        IContentType newsArticle)
    {
        var ct = NewPublic(helper, DoctypeKeys.NewsListPage, DoctypeKeys.NewsListPageAlias, "News list page", "Listing page for news articles. Maps to Schema.org CollectionPage.", "icon-list");
        ApplyAllCompositions(ct, comps);

        ct.AddPropertyGroup("listing", "Listing");
        Add(ct, helper, dt.Textstring, "headline", "Headline", "listing", ContentVariation.Culture);
        Add(ct, helper, dt.Textarea, "introText", "Intro text", "listing", ContentVariation.Culture);

        AttachTemplate(ct, tpl.NewsListPage);
        ct.AllowedContentTypes = new[]
        {
            new ContentTypeSort(newsArticle.Key, 0, newsArticle.Alias),
        };
        return ct;
    }

    private static ContentType BuildNewsArticle(
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        ResolvedCompositions comps,
        ResolvedTemplates tpl)
    {
        var ct = NewPublic(helper, DoctypeKeys.NewsArticle, DoctypeKeys.NewsArticleAlias, "News article", "Single news article. Maps to Schema.org NewsArticle.", "icon-newspaper");
        ApplyAllCompositions(ct, comps);

        ct.AddPropertyGroup("article", "Article");
        Add(ct, helper, dt.Textstring, "headline", "Headline", "article", ContentVariation.Culture);
        Add(ct, helper, dt.DateTimePicker, "publishedDate", "Published date", "article", ContentVariation.Nothing);
        Add(ct, helper, dt.Textarea, "summary", "Summary", "article", ContentVariation.Culture);
        Add(ct, helper, dt.RichText, "body", "Body", "article", ContentVariation.Culture);
        Add(ct, helper, dt.MediaPicker, "image", "Image", "article", ContentVariation.Nothing);

        AttachTemplate(ct, tpl.NewsArticle);
        return ct;
    }

    private static ContentType BuildEventListPage(
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        ResolvedCompositions comps,
        ResolvedTemplates tpl,
        IContentType evt)
    {
        var ct = NewPublic(helper, DoctypeKeys.EventListPage, DoctypeKeys.EventListPageAlias, "Event list page", "Listing page for events. Maps to Schema.org CollectionPage.", "icon-list");
        ApplyAllCompositions(ct, comps);

        ct.AddPropertyGroup("listing", "Listing");
        Add(ct, helper, dt.Textstring, "headline", "Headline", "listing", ContentVariation.Culture);
        Add(ct, helper, dt.Textarea, "introText", "Intro text", "listing", ContentVariation.Culture);

        AttachTemplate(ct, tpl.EventListPage);
        ct.AllowedContentTypes = new[]
        {
            new ContentTypeSort(evt.Key, 0, evt.Alias),
        };
        return ct;
    }

    private static ContentType BuildEvent(
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        ResolvedCompositions comps,
        ResolvedTemplates tpl)
    {
        var ct = NewPublic(helper, DoctypeKeys.Event, DoctypeKeys.EventAlias, "Event", "Single event. Maps to Schema.org Event.", "icon-calendar");
        ApplyAllCompositions(ct, comps);

        ct.AddPropertyGroup("event", "Event");
        Add(ct, helper, dt.Textstring, "headline", "Headline", "event", ContentVariation.Culture);
        Add(ct, helper, dt.DateTimePicker, "startDate", "Start date", "event", ContentVariation.Nothing);
        Add(ct, helper, dt.DateTimePicker, "endDate", "End date", "event", ContentVariation.Nothing);
        Add(ct, helper, dt.Textstring, "location", "Location", "event", ContentVariation.Culture);
        Add(ct, helper, dt.RichText, "body", "Body", "event", ContentVariation.Culture);

        AttachTemplate(ct, tpl.Event);
        return ct;
    }

    private static ContentType BuildServicePage(
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        ResolvedCompositions comps,
        ResolvedTemplates tpl)
    {
        var ct = NewPublic(helper, DoctypeKeys.ServicePage, DoctypeKeys.ServicePageAlias, "Service page", "Municipal service page. Maps to Schema.org GovernmentService. selfServiceLinks stubbed as textarea per ADR-0011 scope deviation (Block List Editor deferred to Phase 5/6).", "icon-document-dashed");
        ApplyAllCompositions(ct, comps);

        ct.AddPropertyGroup("service", "Service");
        Add(ct, helper, dt.Textstring, "headline", "Headline", "service", ContentVariation.Culture);
        Add(ct, helper, dt.Textarea, "summary", "Summary", "service", ContentVariation.Culture);
        Add(ct, helper, dt.RichText, "body", "Body", "service", ContentVariation.Culture);
        Add(ct, helper, dt.Textarea, "selfServiceLinks", "Self-service links (stub — will become Block List in Phase 5/6)", "service", ContentVariation.Culture);

        AttachTemplate(ct, tpl.ServicePage);
        return ct;
    }

    private static ContentType BuildContentPage(
        IShortStringHelper helper,
        ResolvedDataTypes dt,
        ResolvedCompositions comps,
        ResolvedTemplates tpl)
    {
        var ct = NewPublic(helper, DoctypeKeys.ContentPage, DoctypeKeys.ContentPageAlias, "Content page", "Generic content page. Maps to Schema.org WebPage.", "icon-document");
        ApplyAllCompositions(ct, comps);

        ct.AddPropertyGroup("content", "Content");
        Add(ct, helper, dt.Textstring, "headline", "Headline", "content", ContentVariation.Culture);
        Add(ct, helper, dt.RichText, "body", "Body", "content", ContentVariation.Culture);

        AttachTemplate(ct, tpl.ContentPage);
        return ct;
    }

    private static ContentType BuildSiteSettings(IShortStringHelper helper, ResolvedDataTypes dt)
    {
        var ct = new ContentType(helper, -1)
        {
            Key = DoctypeKeys.SiteSettings,
            Alias = DoctypeKeys.SiteSettingsAlias,
            Name = "Site settings",
            Description = "Internal, DA-only site-wide settings.",
            Icon = "icon-settings",
            AllowedAsRoot = true,
            Variations = ContentVariation.Nothing,
        };
        ct.AddPropertyGroup("settings", "Settings");
        AddInvariant(ct, helper, dt.Textstring, "siteName", "Site name", "settings");
        AddInvariant(ct, helper, dt.Textstring, "organizationName", "Organization name (Schema.org)", "settings");
        AddInvariant(ct, helper, dt.Textstring, "organizationUrl", "Organization URL (Schema.org)", "settings");
        AddInvariant(ct, helper, dt.Textstring, "contactEmail", "Contact email", "settings");
        return ct;
    }

    private static ContentType NewPublic(IShortStringHelper helper, Guid key, string alias, string name, string description, string icon)
    {
        return new ContentType(helper, -1)
        {
            Key = key,
            Alias = alias,
            Name = name,
            Description = description,
            Icon = icon,
            Variations = ContentVariation.Culture,
        };
    }

    private static void ApplyAllCompositions(ContentType ct, ResolvedCompositions comps)
    {
        ct.AddContentType(comps.SeoMetadata);
        ct.AddContentType(comps.AccessibilityMetadata);
        ct.AddContentType(comps.TranslationMetadata);
        ct.AddContentType(comps.SchemaOrgOverride);
    }

    private static void AttachTemplate(ContentType ct, ITemplate template)
    {
        ct.AllowedTemplates = new[] { template };
        ct.SetDefaultTemplate(template);
    }

    private static void Add(
        ContentType ct,
        IShortStringHelper helper,
        IDataType dataType,
        string alias,
        string name,
        string groupAlias,
        ContentVariation variations)
    {
        var pt = new PropertyType(helper, dataType, alias)
        {
            Name = name,
            Mandatory = false,
            Variations = variations,
        };
        ct.AddPropertyType(pt, groupAlias);
    }

    private static void AddInvariant(
        ContentType ct,
        IShortStringHelper helper,
        IDataType dataType,
        string alias,
        string name,
        string groupAlias)
    {
        var pt = new PropertyType(helper, dataType, alias)
        {
            Name = name,
            Mandatory = false,
            Variations = ContentVariation.Nothing,
        };
        ct.AddPropertyType(pt, groupAlias);
    }
}
