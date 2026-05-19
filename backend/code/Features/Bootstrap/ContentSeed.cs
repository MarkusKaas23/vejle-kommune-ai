using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace VejleKommune.Code.Features.Bootstrap;

internal static class ContentSeed
{
    private const string Da = LanguageBootstrap.Danish;

    public static bool SeedIfEmpty(
        IContentService contentService,
        IContentTypeService contentTypeService,
        ILogger logger)
    {
        if (contentService.GetRootContent().Any())
        {
            logger.LogDebug("Content seed skipped: root content already exists");
            return false;
        }

        if (contentTypeService.Get(DoctypeKeys.HomePage) is null)
        {
            logger.LogWarning("Content seed skipped: HomePage doctype missing");
            return false;
        }

        logger.LogInformation("Content seed starting");

        var home = CreatePublic(contentService, "Velkommen til Vejle Kommune", -1, DoctypeKeys.HomePageAlias, c =>
        {
            c.SetValue("headline", "Velkommen til Vejle Kommune", Da);
            c.SetValue("introText", "Vejle Kommune leverer service, information og digitale løsninger til borgere og virksomheder i Vejle, Egtved, Give, Jelling og Børkop.", Da);
            c.SetValue("metaTitle", "Vejle Kommune", Da);
            c.SetValue("metaDescription", "Officielt website for Vejle Kommune – nyheder, selvbetjening og kontakt.", Da);
        });

        var newsList = CreatePublic(contentService, "Nyheder", home.Id, DoctypeKeys.NewsListPageAlias, c =>
        {
            c.SetValue("headline", "Nyheder fra Vejle Kommune", Da);
            c.SetValue("introText", "Seneste nyt fra kommunen.", Da);
            c.SetValue("metaTitle", "Nyheder – Vejle Kommune", Da);
            c.SetValue("metaDescription", "Læs de seneste nyheder fra Vejle Kommune.", Da);
        });

        var newsTitles = new[]
        {
            ("Ny cykelsti åbner mellem Vejle og Bredsten", "Borgmester klipper båndet til en 4,5 km ny cykelsti, der forbinder Vejle med Bredsten."),
            ("Renovering af Vejle Bibliotek afsluttet", "Biblioteket genåbner efter seks måneders renovering med ny børneafdeling og café."),
            ("Klimaplan 2030 vedtaget af byrådet", "Byrådet har enstemmigt vedtaget kommunens klimaplan med fokus på CO2-reduktion og grøn omstilling."),
            ("Nye åbningstider på Borgerservice", "Borgerservice udvider åbningstiderne om torsdagen for at imødekomme borgernes behov."),
            ("Skolernes sommerferieaktiviteter for børn", "Kommunen tilbyder gratis sommerferieaktiviteter for børn i alderen 6-14 år."),
        };
        for (var i = 0; i < newsTitles.Length; i++)
        {
            var (title, summary) = newsTitles[i];
            CreatePublic(contentService, title, newsList.Id, DoctypeKeys.NewsArticleAlias, c =>
            {
                c.SetValue("headline", title, Da);
                c.SetValue("publishedDate", DateTime.UtcNow.AddDays(-i));
                c.SetValue("summary", summary, Da);
                c.SetValue("body", $"<p>{summary}</p><p>Læs mere på Vejle Kommunes website.</p>", Da);
                c.SetValue("metaTitle", $"{title} – Vejle Kommune", Da);
                c.SetValue("metaDescription", summary, Da);
            });
        }

        var eventList = CreatePublic(contentService, "Begivenheder", home.Id, DoctypeKeys.EventListPageAlias, c =>
        {
            c.SetValue("headline", "Begivenheder i Vejle Kommune", Da);
            c.SetValue("introText", "Kommende arrangementer.", Da);
            c.SetValue("metaTitle", "Begivenheder – Vejle Kommune", Da);
            c.SetValue("metaDescription", "Se kommende arrangementer og begivenheder i Vejle Kommune.", Da);
        });

        var events = new[]
        {
            ("Borgermøde om byudvikling i Vejle Midtby", "Vejle Rådhus", DateTime.UtcNow.AddDays(14)),
            ("Sommerkoncert i Slotsbanken", "Slotsbanken, Vejle", DateTime.UtcNow.AddDays(30)),
            ("Sundhedsdag på Vejle Bibliotek", "Vejle Bibliotek", DateTime.UtcNow.AddDays(45)),
        };
        foreach (var (title, location, start) in events)
        {
            CreatePublic(contentService, title, eventList.Id, DoctypeKeys.EventAlias, c =>
            {
                c.SetValue("headline", title, Da);
                c.SetValue("startDate", start);
                c.SetValue("endDate", start.AddHours(2));
                c.SetValue("location", location, Da);
                c.SetValue("body", $"<p>Deltag i {title.ToLowerInvariant()}. Sted: {location}.</p>", Da);
                c.SetValue("metaTitle", $"{title} – Vejle Kommune", Da);
                c.SetValue("metaDescription", $"{title}. Sted: {location}.", Da);
            });
        }

        var services = new[]
        {
            ("Pas og kørekort", "Bestil tid til pas og kørekort hos Borgerservice."),
            ("Affald og genbrug", "Find afhentningsdage, åbningstider for genbrugspladser og bestil storskrald."),
            ("Skole og dagtilbud", "Skriv barn op til dagtilbud, find skoledistrikter og kontakt skoler."),
            ("Bolig og byggeri", "Byggetilladelser, BBR-oplysninger og adresseændringer."),
            ("Hjælp til ældre", "Hjemmehjælp, plejehjem, hjælpemidler og forebyggende hjemmebesøg."),
        };
        foreach (var (title, summary) in services)
        {
            CreatePublic(contentService, title, home.Id, DoctypeKeys.ServicePageAlias, c =>
            {
                c.SetValue("headline", title, Da);
                c.SetValue("summary", summary, Da);
                c.SetValue("body", $"<p>{summary} Læs mere om vores service og selvbetjeningsmuligheder.</p>", Da);
                c.SetValue("selfServiceLinks", "Bestil tid · Find selvbetjening · Se ventetider", Da);
                c.SetValue("metaTitle", $"{title} – Vejle Kommune", Da);
                c.SetValue("metaDescription", summary, Da);
            });
        }

        var contentPages = new[]
        {
            ("Om Vejle Kommune", "Vejle Kommune er en dansk kommune i Region Syddanmark med ca. 117.000 indbyggere."),
            ("Kontakt", "Kontakt Vejle Kommune via telefon, mail eller besøg Borgerservice på Vejle Rådhus."),
            ("Politik og demokrati", "Følg byrådets arbejde, læs dagsordener og deltag i kommunale høringer."),
        };
        foreach (var (title, summary) in contentPages)
        {
            CreatePublic(contentService, title, home.Id, DoctypeKeys.ContentPageAlias, c =>
            {
                c.SetValue("headline", title, Da);
                c.SetValue("body", $"<p>{summary}</p>", Da);
                c.SetValue("metaTitle", $"{title} – Vejle Kommune", Da);
                c.SetValue("metaDescription", summary, Da);
            });
        }

        var settings = contentService.Create("Site settings", -1, DoctypeKeys.SiteSettingsAlias);
        settings.SetValue("siteName", "Vejle Kommune");
        settings.SetValue("organizationName", "Vejle Kommune");
        settings.SetValue("organizationUrl", "https://vejle.dk");
        settings.SetValue("contactEmail", "post@vejle.dk");
        contentService.Save(settings);

        logger.LogInformation("Content seed complete: home + news x5 + events x3 + services x5 + content x3 + settings");
        return true;
    }

    private static IContent CreatePublic(
        IContentService contentService,
        string daName,
        int parentId,
        string contentTypeAlias,
        Action<IContent> configure)
    {
        var content = contentService.Create(daName, parentId, contentTypeAlias);
        content.SetCultureName(daName, Da);
        configure(content);
        contentService.Save(content);
        contentService.Publish(content, new[] { Da });
        return content;
    }
}
