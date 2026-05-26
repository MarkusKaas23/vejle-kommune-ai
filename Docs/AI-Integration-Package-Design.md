# AI Integration Package — Design & Implementation Reference

> **Living document.** Updated after each build phase of the Vejle Kommune AI Pattern catalogue.
> Purpose: serve as the design foundation for a future production-grade *Limbo.Umbraco.AI* package.

---

## Overview

This document captures how each AI integration pattern was implemented in the Vejle Kommune thesis demo, what the pattern's architectural intent is, and how it should be realised in a properly packaged, editor-usable Umbraco 17+ package.

The demo uses a lightweight `IAiProvider` abstraction (currently backed by Gemini) with an `AuditingAiProvider` decorator that writes AI call audit records to the Umbraco log. A production package would keep this abstraction and extend it with configurable provider backends (Azure OpenAI, Anthropic, etc.).

---

## Cross-Cutting Concerns

These apply to every pattern and must be designed into the package at the foundation level.

### AI Provider Abstraction

```
IAiProvider
  └── AuditingAiProvider (decorator)
        └── GeminiProvider (or AzureOpenAiProvider, etc.)
```

`AiRequest` carries: `Prompt`, `SystemPrompt`, optional `Images` (multimodal), optional `JsonResponseSchema` (structured output), and `AiCallContext` (pattern name + node ID for audit).

**Package design:** Ship with at least two providers (Gemini, Azure OpenAI). Configuration via `appsettings.json` section `Limbo:Ai`. Provider selection per-feature or globally. All providers implement `IAiProvider`; the decorator chain is assembled in DI.

### AI Call Audit Log

Every outbound AI call writes a structured log entry:

```
pattern | nodeId | provider/model | contentChars | latencyMs | costUsd | inTokens | outTokens | timestamp
```

**Never** the content itself — only metadata. This satisfies DPO-grade auditability for a municipality (GDPR Art. 30 Records of Processing Activities).

**Package design:** Surface these records in a dedicated **Audit Log dashboard** in the Umbraco backoffice. Filterable by pattern, date range, node. Show running cost totals. Export to CSV.

### PII Risk

All current patterns send editorial prose to an external AI provider. For a municipality this is a governance concern. Mitigation options (in order of strength):

1. **Azure OpenAI with DPA** — data stays in EU, covered by Microsoft's GDPR DPA.
2. **Gemini paid tier with enterprise DPA** — same principle.
3. **Field-level opt-out** — properties tagged `[AiOptOut]` are stripped before any AI call.
4. **On-premise model** (Ollama/vLLM) — no data leaves the municipality's infrastructure.

**Package design:** Ship a `[AiOptOut]` attribute (or a document type property checkbox) and a pre-send PII scrubber hook. Document tier/DPA requirements prominently in the README.

---

## Pattern 1 — Sync Suggestion: SEO Meta Description

### What it does
When an editor opens the SEO tab of a content node, a button calls the AI to generate a meta title, meta description, Open Graph title, and Open Graph description from the page content. The editor reviews and accepts or overrides before saving.

### Current implementation (L2)
- Integrates with **SeoToolkit.Umbraco.MetaFields** via `IAIGenerationService` (`VejleAiGenerationService`).
- Endpoint: `POST /umbraco/api/vejle/seo/generate-meta-description`
- UI: SeoToolkit's native **"✨ Generate with AI"** button — no custom backoffice extension needed.
- Structured Gemini output via JSON schema (`title`, `metaDescription`, `openGraphTitle`, `openGraphDescription`).

### Package design
- Ship as a **SeoToolkit adapter** — implement `IAIGenerationService` and register it. Editors get the existing SeoToolkit button with no additional UI work.
- Alternatively, ship a standalone **Content App** tab on any document type, with a "Generate SEO fields" panel that shows a preview diff before applying.
- Configuration: which document types have SEO generation enabled, max character limits per field.

### Key implementation notes
- Gemini structured output (JSON schema) prevents markdown contamination in meta fields.
- System prompt must be municipality-aware: tone, Danish vs English, max lengths.
- Error handling: 429 → friendly message ("quota exceeded, try again later"), 5xx → log + silent fail (never block the editor).

---

## Pattern 2 — Sync Suggestion: Alt Text (Multimodal)

### What it does
When an editor is on a media item, a button sends the image bytes to the AI and returns a concise Danish alt text (≤125 chars). The editor reviews and saves.

### Current implementation (L2)
- Endpoint: `POST /umbraco/api/vejle/media/generate-alt-text`
- Reads image from `MediaFileManager.FileSystem`, sends as `AiImage(bytes, mimeType)`.
- Supports JPEG, PNG, GIF, WebP. Handles both plain `/media/...` path and Umbraco's JSON `{"src":"..."}` envelope.
- Returns Danish alt text trimmed to ≤125 chars.

### Package design
- **Media Content App** — a small panel visible on Image media items with a "Generate alt text" button and a character counter.
- On click: POST to the API, show result in an editable text field, Save button writes to the `umbracoFile` alt text property.
- Optionally: a **batch tool** in a backoffice dashboard that finds all media items with empty alt text and queues generation jobs.

### Key implementation notes
- Model must support vision (multimodal). Gemini 2.5 Flash works. GPT-4o works. Not all models do.
- Image bytes are sent inline (base64 `inline_data`). For large images consider resizing before sending to reduce cost and latency.
- Danish system prompt is important for a municipality — alt text language must match the content language.

---

## Pattern 3 — Gate / Validator: Tone of Voice

### What it does
Before a content node is published, the AI inspects all text fields and checks for compliance with Vejle Kommune's tone-of-voice guidelines (plain language, citizen-friendly, no bureaucratic jargon). If issues are found, publishing is blocked with actionable feedback.

### Current implementation (L2)
- `INotificationAsyncHandler<ContentPublishingNotification>` — fires in the publish pipeline.
- Extracts text from all culture variants using `content.AvailableCultures` (required for culture-variant properties).
- Strips HTML, sends to Gemini with structured schema: `{ passes: bool, reason: string, suggestions: string[] }`.
- On failure: `notification.CancelOperation(EventMessage("Tone of voice", reason))` — publish is blocked, editor sees the reason inline in the backoffice.
- **Fail-open**: any AI/infra error is caught, logged, and the publish proceeds. The gate never blocks due to AI unavailability.

### Package design
- **Configurable per document type** — a backoffice settings page lets admins choose which document types are gated and which are merely warned (warning vs. blocking mode).
- **Tone-of-voice context editor** — a rich text area in settings where admins author the prompt/policy. The prompt IS the governance artefact ("prompt-as-policy"). Version-controlled via uSync.
- **Notification handler registration** — automatic via `IComposer`. No code required from the integrating developer.
- **Override permission** — a user group permission "Can bypass AI gate" allows designated editors to publish despite AI objections (with audit log entry).

### Key implementation notes
- Culture-variant properties require `property.GetValue(culture)` — `GetValue()` without culture returns null.
- HTML must be stripped before sending — rich text fields contain markup that confuses tone analysis.
- The JSON schema (`passes`, `reason`, `suggestions`) gives Gemini a deterministic response shape. Without it, Gemini sometimes returns prose instead of a parseable verdict.
- Fail-open is non-negotiable: a municipality cannot have publishing blocked by an AI outage.

---

## Pattern 4 — Async Transform: Translation

### What it does
An editor (or automated trigger) queues a translation job for a content node. A background worker calls the AI to translate all text fields from Danish to a target language, registers the culture variant (`SetCultureName`), writes the translated values, and saves the node as an **unpublished draft**. The editor reviews and publishes the variant.

### Current implementation (L2)
- `TranslationQueue`: singleton `Channel<TranslationJob>` (unbounded, single reader) + `ConcurrentDictionary` for job states.
- `TranslationWorker`: `BackgroundService` that drains the channel. Uses `IServiceScopeFactory` for scoped Umbraco services per job.
- All text fields translated in a single Gemini call (JSON dict in → JSON dict out) for consistency and cost efficiency.
- Rich-text fields: unwraps Umbraco's `{"markup":"<html>"}` envelope before sending, re-wraps after.
- `content.SetCultureName(variantName, culture)` is required to create the variant record — without it, `SetValue` calls persist to the DB but the variant never appears as "created" in the backoffice.
- Saved with `contentService.Save()` (not Publish) — editor must review before publishing.
- Endpoints: `POST /translate` → 202 + jobId, `GET /status/{jobId}`.

### Package design
- **Content App panel** on any culture-variant document type: dropdown of target languages (populated from configured languages in Umbraco), "Translate" button, live job status indicator (polling the status endpoint).
- **Notification handler trigger**: option to auto-queue translation on da-DK publish (configurable per document type). Makes translation fully hands-off for the editor.
- **Variant publish gate**: after auto-translation, the translated variant remains unpublished. A separate notification (`ContentPublishingNotification` on the en-US variant) can be configured to require a human to explicitly publish it.
- **Job persistence**: in-memory queue is lost on restart. Production package should use **Hangfire** or **Umbraco's QueuedHostedService** with a persistent store (SQLite or the Umbraco DB).

### Key implementation notes
- `Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true })` is efficient and backpressure-safe.
- `IServiceScopeFactory` + `CreateAsyncScope()` per job is mandatory — `IContentService` is scoped and cannot be used from a singleton background service directly.
- Target language name (for the prompt) derived from culture code in a `switch` expression — extend this as more languages are added.
- In-memory job state is lost on restart — communicate this limitation clearly to editors in the UI ("jobs queued before restart are no longer visible but may still have been saved").

---

## Pattern 5 — Async Analyze: Accessibility Audit

### What it does
An editor (or automated trigger) queues an accessibility audit for a content node. A background worker sends the text content to the AI for a WCAG/plain-language analysis and stores structured **findings** in memory. The content tree is **never modified**. Editors review findings and manually fix issues, or dismiss them. Rollback is trivial — dismiss = remove from the findings store.

### Current implementation (L2)
- `AccessibilityQueue`: same channel + dictionary pattern as `TranslationQueue`, with an additional `Dismiss(jobId)` method (trivial rollback).
- `AccessibilityWorker`: strips HTML from all auditable fields, sends plain text to Gemini with a WCAG/plain-language system prompt.
- Gemini returns a JSON array of `AccessibilityFinding` records (`field`, `issue`, `suggestion`, `severity`).
- Endpoints: `POST /audit` → 202 + jobId, `GET /findings/{jobId}`, `DELETE /findings/{jobId}` (dismiss).
- The `DELETE` endpoint is architecturally load-bearing: it models the trivial rollback that distinguishes this pattern from Async Transform.

### Package design
- **Content App panel** on all document types: "Run accessibility audit" button, findings list with severity icons (error/warning/info), dismiss button per finding, "Fix now" button that scrolls to the relevant field.
- **Scheduled batch audit**: nightly job audits all published nodes, stores findings, sends a summary email to the content team. Configurable schedule, configurable recipient list.
- **Findings persistence**: same L3 concern as Translation — use Hangfire or Umbraco DB for production. A custom DB table `AiAccessibilityFinding` (nodeId, field, issue, suggestion, severity, auditedAt, dismissedAt, dismissedBy) gives a full history.
- **Dashboard view**: a dedicated backoffice section listing all current findings across all nodes, sortable by severity, filterable by document type or content area.

### Key implementation notes
- The plain-language/WCAG prompt must be tuned to the municipality's specific guidelines. In Denmark: FOB (Forskning og Bevidning) standards, not just general WCAG.
- Structured JSON array output schema prevents Gemini returning prose. Each finding must be independently actionable.
- Findings are only as good as the text input — stripping HTML and unwrapping rich-text envelopes is critical before analysis.
- The architectural distinction from Async Transform (mutates vs. annotates) is what makes rollback semantics different. This must be preserved in the package design.

---

## Planned Patterns (Not Yet Implemented)

### Pattern 6 — Generative Pipeline: Document → Content
A multi-step AI transform that takes an uploaded PDF/Word document (e.g., a press release, council minutes, policy paper) and produces a structured Umbraco content node. Steps: extract text → classify document type → extract structured fields → create draft node. No existing node is modified; the output is a new draft for editorial review.

**Package design:** File upload widget in the backoffice, document type selector, field mapping preview (AI-extracted value → Umbraco property), "Create draft" button. The generated node is always unpublished.

### Pattern 7 — Agent Tool: MCP-Based Content Management
The LLM is given tools via the Model Context Protocol (MCP) and can drive the CMS directly — read nodes, create nodes, publish, query content. Used for bulk operations or content migrations that are too complex to script but too risky to fully automate.

**Package design:** MCP server exposing Umbraco management operations as tools. Each tool has an approval gate — the AI proposes an action, a human approves. Audit log captures every tool call. Scope is configurable per user/role.

---

## Software Architecture: Package Structure

### Packaging Philosophy — Core + Optional Modules

A commercial package should allow customers to buy and install only what they need. The optimal model is **one Core NuGet package + one NuGet package per feature + one bundle package** that pulls everything in. This mirrors how successful Umbraco packages like Umbraco Commerce and uMarketingSuite are structured.

```
NuGet package dependency graph:

Limbo.Umbraco.AI                        ← Bundle (depends on all modules below)
├── Limbo.Umbraco.AI.SeoGeneration
├── Limbo.Umbraco.AI.AltText
├── Limbo.Umbraco.AI.ToneOfVoice
├── Limbo.Umbraco.AI.Translation
├── Limbo.Umbraco.AI.Accessibility
├── Limbo.Umbraco.AI.DocumentIngestion
└── Limbo.Umbraco.AI.AgentTools
      │
      └── (all depend on) Limbo.Umbraco.AI.Core
```

**What each layer contains:**

`Limbo.Umbraco.AI.Core` — always required, no editor features:
- `IAiProvider` abstraction + `AiRequest/Response/Image`
- `AuditingAiProvider` decorator + audit log infrastructure
- Provider implementations: `GeminiProvider`, `AzureOpenAiProvider`
- Settings infrastructure (`appsettings.json` section, backoffice settings page)
- PII scrubber hook
- Shared backoffice shell (settings section, audit log dashboard)

`Limbo.Umbraco.AI.SeoGeneration` — depends on Core:
- `VejleAiGenerationService` (implements SeoToolkit `IAIGenerationService`)
- Optional: standalone SEO Content App if SeoToolkit is not installed

`Limbo.Umbraco.AI.AltText` — depends on Core:
- Media Content App with "Generate alt text" button
- Batch audit tool (all images with empty alt text)

`Limbo.Umbraco.AI.ToneOfVoice` — depends on Core:
- `ContentPublishingNotification` handler
- Backoffice policy editor (tone-of-voice prompt authoring)
- Per-document-type enable/disable + warn vs. block mode

`Limbo.Umbraco.AI.Translation` — depends on Core:
- Translation queue + worker + controller
- Document Content App with language picker + job status
- Optional: auto-trigger on publish notification handler

`Limbo.Umbraco.AI.Accessibility` — depends on Core:
- Accessibility queue + worker + controller
- Document Content App with findings panel + dismiss
- Optional: scheduled nightly batch audit

`Limbo.Umbraco.AI.DocumentIngestion` — depends on Core:
- File upload + extraction pipeline
- Document-type-to-field mapper
- Draft node creation workflow

`Limbo.Umbraco.AI.AgentTools` — depends on Core:
- MCP server exposing Umbraco tools
- Per-tool approval gates
- Scoped permission model

`Limbo.Umbraco.AI` (bundle) — depends on all of the above:
- Empty package, just NuGet dependencies
- Useful for "I want everything" installs

### Repository Structure

One Git repository, multiple projects:

```
Limbo.Umbraco.AI/
├── src/
│   ├── Limbo.Umbraco.AI.Core/
│   │   ├── Providers/
│   │   │   ├── Gemini/
│   │   │   └── AzureOpenAi/
│   │   ├── Auditing/
│   │   ├── Settings/
│   │   └── BackOffice/
│   │       ├── Dashboard/         ← Audit log, cost tracking
│   │       └── Settings/          ← Provider config UI
│   │
│   ├── Limbo.Umbraco.AI.SeoGeneration/
│   ├── Limbo.Umbraco.AI.AltText/
│   ├── Limbo.Umbraco.AI.ToneOfVoice/
│   ├── Limbo.Umbraco.AI.Translation/
│   ├── Limbo.Umbraco.AI.Accessibility/
│   ├── Limbo.Umbraco.AI.DocumentIngestion/
│   ├── Limbo.Umbraco.AI.AgentTools/
│   └── Limbo.Umbraco.AI/          ← Bundle (no code, only .csproj refs)
│
├── tests/
│   ├── Limbo.Umbraco.AI.Core.Tests/
│   └── Limbo.Umbraco.AI.Integration.Tests/
│
└── docs/
```

### Who is the customer? Two distinct deployment models

This is a critical design decision that affects everything from the settings UI to the licensing model. There are two fundamentally different customer types, and the package must support both.

---

**Model A — Limbo-hosted AI (end-customer buys a managed service)**

The customer is a municipality, public institution, or organisation. They are not IT people. They should never see an API key, a provider name, or a config file. They just want the features to work.

In this model:
- Limbo operates its own AI infrastructure (e.g. Azure OpenAI with a Danish DPA in place).
- The package ships pre-configured to call **Limbo's own API gateway**, not a provider directly.
- The customer authenticates to Limbo's gateway with a **license key** — one field in the backoffice settings page.
- Limbo controls provider selection, rate limiting, failover, cost, and GDPR compliance centrally.
- The customer pays Limbo on a subscription or usage basis. They never touch an API key.

This is the right model for non-technical end customers. It also gives Limbo full control over the AI provider relationship, margins, and data governance.

```
[Umbraco site] → [Limbo AI Gateway] → [Azure OpenAI / Gemini / etc.]
                        ↑
                  Limbo manages:
                  - provider selection
                  - failover
                  - rate limiting
                  - DPA/GDPR
                  - cost tracking
```

---

**Model B — Bring Your Own Key (developer/agency installs the package)**

The customer is a Umbraco developer or agency building a solution. They are technical. They have their own Azure OpenAI subscription or Gemini project, and they want to wire it in themselves.

In this model:
- The package ships with a **backoffice settings page** where the developer pastes their API endpoint, API key, and selects the model. No JSON files, no code changes.
- Rate limiting, failover, and cost tracking are the developer's responsibility.
- GDPR compliance depends on the provider and DPA the developer has in place.

This is the right model for agencies and developers building custom solutions. It makes the package maximally flexible and self-hostable.

```
[Umbraco site] → [Azure OpenAI / Gemini] (developer's own subscription)
```

---

**Recommended approach: ship both, default to Model A**

The package should ship with Model A as the default (a Limbo license key is all that's needed to get started) and Model B as an advanced option (a "Use custom provider" toggle in settings that reveals the BYOK fields). This means:

- Non-technical customers get a zero-configuration experience.
- Technical customers and agencies get full control.
- Limbo gets a recurring revenue stream from Model A customers while Model B drives developer adoption.

### Licensing options for commercial sale

| Model | How it works | Best for |
|---|---|---|
| Managed service (Model A) | Customer pays Limbo per use or on subscription; Limbo operates the AI infrastructure | Municipalities, non-technical orgs |
| BYOK license (Model B) | One-time or annual license key; customer supplies their own API key | Agencies, developers, self-hosters |
| Open Core | Core is MIT, feature modules are commercial (either model) | Community adoption + monetisation |

The **Open Core** model is worth considering for the BYOK tier: releasing `Limbo.Umbraco.AI.Core` as MIT encourages community adoption and third-party provider implementations, while the feature modules generate revenue. The managed service tier (Model A) remains fully commercial regardless.

---

## Backoffice Extension Points (Umbraco 17)

| Feature | Extension type | Notes |
|---|---|---|
| SEO generation button | SeoToolkit `IAIGenerationService` | Native — no custom UI |
| Alt text button | Media Content App (Vite/Lit) | Custom panel on Image media type |
| Tone-of-voice gate | `INotificationAsyncHandler` | No UI — inline publish error message |
| Translation panel | Document Content App | Language dropdown + status badge |
| Accessibility panel | Document Content App | Findings list + dismiss |
| Audit log | Custom Dashboard section | Table + CSV export |
| Settings | Custom section or Settings tree node | Tone policy editor, provider config |

---

## L3 Concerns (Production — Out of Thesis Scope)

These are deliberately not implemented in the thesis demo but must be named for the package roadmap. Where relevant, the concern is tagged by which deployment model it applies to.

- **Job queue durability** — in-memory queues are lost on restart. Use Hangfire + persistent store. *(Both models)*
- **Provider failover** — if the AI backend is unavailable, retry with backoff or surface a clear error. *(Model A: Limbo handles this centrally in the gateway. Model B: the integrating developer's responsibility.)*
- **Rate limiting** — per-editor and per-node request throttling to prevent quota exhaustion. *(Model A: enforced at Limbo's gateway. Model B: developer configures limits in the backoffice settings page.)*
- **Findings persistence** — accessibility findings currently in-memory; need a DB table with full history, dismissal audit trail, and "fixed" confirmation. *(Both models)*
- **Translation rollback UX** — currently rollback = unpublish/delete the en-US variant manually. A package should offer a one-click "Remove translation" action. *(Both models)*
- **Prompt versioning** — tone-of-voice prompts should be version-controlled with a change log, not just free-text fields. *(Both models)*
- **Cost budgets** — configurable monthly cost caps with automatic disable when exceeded. *(Model A: Limbo enforces this per license/subscription. Model B: developer sets a cap in backoffice settings.)*
- **GDPR data residency** — all data must stay in the EU for public sector customers. *(Model A: Limbo's responsibility — enforced by choosing an EU-hosted provider with a Danish/EU DPA. Model B: developer's responsibility — the settings UI should warn if a non-EU endpoint is configured.)*
- **License key validation** — Model A customers authenticate with a Limbo license key; the package must validate it at startup and gracefully disable features if it lapses, without crashing the Umbraco site. *(Model A only)*

---

*Last updated: Phase 6 (Async Analyze — Accessibility). Next: Phase 7 (Generative Pipeline).*
