# Limbo Engineering Guidelines

This file captures Limbo's coding standards and conventions. Claude Code should read and apply
these rules when generating or reviewing code in any Limbo/Skybrud project.

---

## C# Naming Conventions

- **Types, properties, methods**: PascalCase (e.g. `NewsRepository`, `GetBySlug`)
- **Private fields**: _camelCase with underscore prefix (e.g. `_contentService`)
- **Local variables**: camelCase (e.g. `newsItems`)
- **Names must be in English** (American English: *color* not *colour*, *organization* not *organisation*)
- Check for typos and misspellings before committing

## C# Code Style

- Open brackets `{` on the **same line** (not on a new line)
- Use **file-scoped namespace declarations** — avoids unnecessary indentation
- Use **actual type names** instead of `var` for variable declarations; `var` is allowed for unusually long class names
- Use **collection expressions** `[]` (or `Array.Empty<T>`) instead of `new T[0]` for empty arrays
- Use `IReadOnlyList<T>` (or `IEnumerable<T>`) when returning immutable collections
- Use dictionaries for fast lookups
- Remove unused imports before committing (`CTRL+R, CTRL+G` in VS)
- Each type must live in a file that matches its namespace and type name
- **Do not commit code that produces new compiler errors or warnings** (unless on a WIP branch with a documented reason)

## Assembly & Namespace Naming

- Assembly name and root namespace reflect the **client name** in PascalCase
- The `web` project: `ClientName.Web` (assembly name and root namespace)
- The `code` project: `ClientName` (assembly name and root namespace)
- Set these explicitly in the `.csproj` `<PropertyGroup>`:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <AssemblyName>RoskildeKommune</AssemblyName>
  <RootNamespace>RoskildeKommune</RootNamespace>
</PropertyGroup>
```

## Umbraco Content Type Aliases

- Aliases use **lower camelCase**
- Always **English**, following American English conventions
- Prefixed with the first two letters of the client/project name (e.g. `ro` for Roskilde Kommune, `ve` for Vejle Kommune)
- Pattern: `{prefix}{SiteType?}{PageType}` — the SiteType segment (e.g. `MainSite`, `SubSite`) is optional for single-site solutions but recommended for future-proofing

**Common examples (prefix `li` = Limbo):**

| Page type         | Alias             |
|-------------------|-------------------|
| Front page        | `liFrontPage`     |
| Content page      | `liContentPage`   |
| News article      | `liNewsPage`      |
| News list         | `liNewsList`      |
| Event             | `liEventPage`     |
| Event list        | `liEventList`     |
| Hearing           | `liHearingPage`   |
| Hearing list      | `liHearingList`   |
| Self-service page | `liSelfServicePage` |
| Self-service list | `liSelfServiceList` |

For multi-site solutions:
- `liMainSiteFrontPage`
- `liSubSiteFrontPage`
- `liIntraFrontPage`

## Umbraco Property Aliases

Property aliases use **lower camelCase**.

**Common:**

| Property              | Alias                       |
|-----------------------|-----------------------------|
| Site name             | `siteName`                  |
| Main navigation       | `mainNavigation`            |
| Secondary navigation  | `secondaryNavigation`       |
| Front page            | `frontPage`                 |
| Title                 | `title`                     |
| Teaser                | `teaser`                    |
| Hide from search      | `hideFromSearch`            |
| 404 page              | `notFoundPage`              |
| Search page           | `searchPage`                |
| Outbound redirect     | `outboundRedirect`          |
| Postal code           | `postalCode` (not `zipCode`)|

**Open Graph:**

| Property              | Alias                       |
|-----------------------|-----------------------------|
| OG title              | `ogTitle`                   |
| OG description        | `ogDescription`             |
| OG image (single)     | `ogImage`                   |
| OG image (multiple)   | `ogImages`                  |

**SEO:**

| Property              | Alias                       |
|-----------------------|-----------------------------|
| SEO title             | `seoTitle`                  |
| SEO description       | `seoMetaDescription`        |

**Sitemap:**

| Property              | Alias                         |
|-----------------------|-------------------------------|
| Hide from sitemap     | `hideFromSitemap`             |
| Page priority         | `sitemapPagePriority`         |
| Page change frequency | `sitemapPageChangeFrequency`  |

**Umbraco built-ins:**

| Property              | Alias                         |
|-----------------------|-------------------------------|
| Redirect              | `umbracoRedirect`             |
| Internal redirect ID  | `umbracoInternalRedirectId`   |
| Hide from navigation  | `umbracoNaviHide`             |
| URL alias             | `umbracoUrlAlias`             |
| URL name              | `umbracoUrlName`              |

## Block Naming

Blocks follow the pattern: `{prefix}{Name}Block`

Rules:
1. Must start with the client/solution prefix
2. Must end with `Block`
3. Use `List` suffix for collections (not plural noun): `liNewsListBlock`, not `liNewsBlock` or `liNewsesBlock`
4. Optionally include site type between prefix and name

**Examples:**

| Block type   | Alias               |
|--------------|---------------------|
| Image        | `liImageBlock`      |
| Image list   | `liImageListBlock`  |
| News list    | `liNewsListBlock`   |
| Quote        | `liQuoteBlock`      |
| RTE / Rich text | `liRteBlock` or `liRichTextBlock` |
| Vimeo video  | `liVimeoVideoBlock` |
| YouTube video| `liYouTubeVideoBlock` |

Note: frontend uses a reversed convention (`BlockNewsList`) — this is intentional and separate.

## Repository vs. Service

**Repository** — reads existing data from optimized sources:
- Fetches from content cache, Examine indexes, or in-memory collections
- Side-effect free (no writes, no state changes)
- Returns domain or view-ready models
- Answers: *"what data exists / how do I find it?"*
- Example: `NewsRepository` querying Examine for published news items

**Service** — encapsulates business logic and mutation:
- Handles CRUD operations against database or content store
- Validates input, enforces business rules
- May raise events, update related entities, trigger side effects
- Answers: *"what should happen / how do we change the system?"*
- Example: `NewsService` creating, updating, or deleting news items

**Dependency direction:**
- Repositories **may** call services
- Services **should avoid** calling repositories (prevents circular DI and blurred responsibilities)
- When a service must call a repository, document why and watch for circular DI

## uSync Configuration (Umbraco 10+)

Default configuration — add to `appsettings.json`:

```json
"uSync": {
  "Settings": {
    "ExportOnSave": "Settings",
    "UIEnabledGroups": "Settings"
  },
  "Sets": {
    "Default": {
      "Handlers": {
        "DictionaryHandler": {
          "Group": "Settings"
        }
      }
    }
  }
}
```

This configures uSync to export settings and dictionaries on save only.
Member type sync is **off by default** — enable explicitly if needed.

## Secrets Management

- **Never** store secrets (connection strings, API keys, etc.) in `appsettings.json` or anywhere in the repository
- Store secrets in **1Password**
- **Local development**: use `dotnet user-secrets`
  - Initialize once in the `web` folder: `dotnet user-secrets init`
  - Set a secret: `dotnet user-secrets set "ConnectionStrings:umbracoDbDSN" "value"`
  - Secrets land in `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json`
- **DevOps (dev/stage/prod)**: add as pipeline variables per environment
- In `appsettings.json`, add a **placeholder comment** pointing to the 1Password link — never the actual value
- Document each secret in `README.md` with the `dotnet user-secrets set` command