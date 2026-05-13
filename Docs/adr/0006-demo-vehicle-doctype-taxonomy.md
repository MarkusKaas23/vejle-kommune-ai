# Demo vehicle doctype taxonomy

The Demo vehicle has 7 public-facing doctypes (vary by culture), 1 internal doctype (DA-only), 2 media types, and 4 cross-cutting compositions. Each doctype earns its place against a specific Pattern requirement; nothing is included for site-completeness.

**Public-facing doctypes:** HomePage, NewsListPage, NewsArticle, EventListPage, Event, ServicePage, ContentPage. NewsListPage and EventListPage are kept as distinct doctypes (not collapsed into a generic ListPage) because Schema enrichment requires two distinct CollectionPage instances with different child Schema.org types — collapsing them would lose that.

**Internal:** SiteSettings (DA-only).

**Media:** Image (extended with required alt-text + provenance fields), Document (Word/PDF, used as input by the Generative pipeline, stored under `/imports/`).

**Compositions:** SeoMetadata (`metaTitle`, `metaDescription`, `metaGeneratedBy`), AccessibilityMetadata (`lastAuditAt`, `findings` block list), TranslationMetadata (`translationGeneratedBy`, `translationAcceptedAt`), SchemaOrgOverride (optional `schemaTypeOverride`, optional `additionalJsonLd`). Applied to all public-facing doctypes.

**Self-service links** are modelled as a `SelfServiceLink` element type inside a Block List Editor on ServicePage — not as a navigable doctype. Title varies by culture; URL stays fixed across variants. Reflects real kommune information architecture; avoids orphan link-nodes.

**Schema.org default mappings:** HomePage → `WebSite` + `GovernmentOrganization` (the only sanctioned multi-block exception, per Google's recommendation for organisational sites). NewsArticle → `NewsArticle`. NewsListPage / EventListPage → `CollectionPage`. Event → `Event`. ServicePage → `GovernmentService`. ContentPage → `WebPage`. Per-node `schemaTypeOverride` exists but is the exception, not the rule. The HomePage two-block exception justifies the SchemaOrgOverride composition's existence — itself part of the Schema enrichment Pattern's claim about override mechanisms.

**Acknowledged trade-off — AccessibilityMetadata as composition not sidecar:** Storing audit findings as a block list on the doctype mixes content with audit state; the findings expire when the content changes. A production implementation would separate them into a dedicated audit store or sidecar service. The composition approach is accepted for thesis-demo simplicity and **must** be acknowledged explicitly in the Async analyze Pattern chapter as a scope statement, not a flaw.

**Provenance fields are load-bearing.** `*GeneratedBy` / `*AcceptedAt` (defined as **Provenance field** in CONTEXT.md) are how the headline Editor control axis becomes measurable across all Patterns. Removing them would force prose claims; they are not optional.
