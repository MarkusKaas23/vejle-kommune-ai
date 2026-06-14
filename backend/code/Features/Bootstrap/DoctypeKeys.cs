namespace VejleKommune.Code.Features.Bootstrap;

internal static class DoctypeKeys
{
    public static readonly Guid SeoMetadata = new("aaaa0001-0000-0000-0000-000000000001");
    public static readonly Guid AccessibilityMetadata = new("aaaa0001-0000-0000-0000-000000000002");
    public static readonly Guid TranslationMetadata = new("aaaa0001-0000-0000-0000-000000000003");
    public static readonly Guid SchemaOrgOverride = new("aaaa0001-0000-0000-0000-000000000004");

    public static readonly Guid HomePage = new("bbbb0001-0000-0000-0000-000000000001");
    public static readonly Guid NewsListPage = new("bbbb0001-0000-0000-0000-000000000002");
    public static readonly Guid NewsArticle = new("bbbb0001-0000-0000-0000-000000000003");
    public static readonly Guid EventListPage = new("bbbb0001-0000-0000-0000-000000000004");
    public static readonly Guid Event = new("bbbb0001-0000-0000-0000-000000000005");
    public static readonly Guid ServicePage = new("bbbb0001-0000-0000-0000-000000000006");
    public static readonly Guid ContentPage = new("bbbb0001-0000-0000-0000-000000000007");
    public static readonly Guid SiteSettings = new("bbbb0001-0000-0000-0000-000000000008");

    // Subsite types — used to demo the Content Converter feature
    public static readonly Guid SubSiteNewsArticle = new("bbbb0001-0000-0000-0000-000000000009");
    public static readonly Guid SubSiteNewsListPage = new("bbbb0001-0000-0000-0000-000000000010");

    public static readonly Guid SelfServiceLinkElement = new("cccc0001-0000-0000-0000-000000000001");
    public static readonly Guid AccessibilityFindingElement = new("cccc0001-0000-0000-0000-000000000002");

    public static readonly Guid ImageMedia = new("dddd0001-0000-0000-0000-000000000001");
    public static readonly Guid DocumentMedia = new("dddd0001-0000-0000-0000-000000000002");

    public static readonly Guid HomePageTemplate = new("eeee0001-0000-0000-0000-000000000001");
    public static readonly Guid NewsListPageTemplate = new("eeee0001-0000-0000-0000-000000000002");
    public static readonly Guid NewsArticleTemplate = new("eeee0001-0000-0000-0000-000000000003");
    public static readonly Guid EventListPageTemplate = new("eeee0001-0000-0000-0000-000000000004");
    public static readonly Guid EventTemplate = new("eeee0001-0000-0000-0000-000000000005");
    public static readonly Guid ServicePageTemplate = new("eeee0001-0000-0000-0000-000000000006");
    public static readonly Guid ContentPageTemplate = new("eeee0001-0000-0000-0000-000000000007");

    public static readonly Guid SelfServiceLinksDataType = new("ffff0001-0000-0000-0000-000000000001");
    public static readonly Guid AccessibilityFindingsDataType = new("ffff0001-0000-0000-0000-000000000002");

    public const string SeoMetadataAlias = "seoMetadata";
    public const string AccessibilityMetadataAlias = "accessibilityMetadata";
    public const string TranslationMetadataAlias = "translationMetadata";
    public const string SchemaOrgOverrideAlias = "schemaOrgOverride";

    public const string HomePageAlias = "homePage";
    public const string NewsListPageAlias = "newsListPage";
    public const string NewsArticleAlias = "newsArticle";
    public const string EventListPageAlias = "eventListPage";
    public const string EventAlias = "event";
    public const string ServicePageAlias = "servicePage";
    public const string ContentPageAlias = "contentPage";
    public const string SiteSettingsAlias = "siteSettings";
    public const string SubSiteNewsArticleAlias = "veSubSiteNewsArticle";
    public const string SubSiteNewsListPageAlias = "veSubSiteNewsListPage";

    public const string SelfServiceLinkElementAlias = "selfServiceLink";
    public const string AccessibilityFindingElementAlias = "accessibilityFinding";

    public const string ImageMediaAlias = "vejleImage";
    public const string DocumentMediaAlias = "vejleDocument";
}
