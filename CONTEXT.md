# Vejle Kommune AI Patterns

A thesis research project exploring AI integrations in a CMS context (Umbraco 17). The argument: AI-in-CMS integrations cluster into a small set of architectural Patterns, each with distinct trade-offs in latency, editor control, governance, and implementation complexity. The fictional Vejle Kommune municipality site is the Demo vehicle on which the Patterns are exercised.

## Language

**Pattern**:
An architectural shape for integrating AI into a CMS, characterised by where it runs, who initiates it, and how its output reaches content. The thesis's unit of evaluation.
_Avoid_: Feature, technique, integration

**Use case**:
A concrete instance of a Pattern implemented against the Demo vehicle. Useful for demonstration but not the unit of evaluation.
_Avoid_: Feature, scenario

**Demo vehicle**:
The fictional Vejle Kommune municipality website (content + frontend) used to give the Patterns something realistic to run on. Its content model exists to enable the Patterns, not as an end in itself.

**Sync suggestion** (Pattern):
AI proposes a value at edit time; an editor accepts or overrides before save. Use cases: SEO metadata, alt text.

**Gate / validator** (Pattern):
AI inspects content at a workflow boundary (typically pre-publish) and blocks, warns, or annotates. Use case: tone of voice.

**Async transform** (Pattern):
AI runs over many existing nodes off the editor's critical path and **mutates the CMS** — new or updated nodes appear in the content tree. Rollback means deleting or unpublishing nodes. Use case: translation.

**Async analyze** (Pattern):
AI runs over many existing nodes off the editor's critical path and emits **findings alongside the CMS** — the content tree is unchanged until a human acts on a finding. Rollback is irrelevant; findings are dismissed. Use case: accessibility audit.

**Generative pipeline** (Pattern):
A multi-step AI transform that takes an external artefact and produces structured CMS output. Use case: document → content.

**Agent tool** (Pattern):
The LLM is given tools (e.g. via MCP) and drives the CMS itself. Use case: MCP-based content migration.

**Schema enrichment** (Pattern):
AI emits structured data derived from the CMS model rather than mutating it. Use case: Schema.org / GEO output.

**Cut rule**:
Under time pressure, when two Use cases map to the same Pattern, one is dropped before any Pattern itself is dropped. (Currently only fires for SEO + alt text under **Sync suggestion** — those two are deliberately kept doubled to demonstrate that the Pattern is input-modality-agnostic.)

**Evaluation grid**:
The six-axis framework on which every Pattern is compared, plus one per-Pattern flag. Axes: **Latency** (continuous, seconds), **Editor control** (ordinal 1–5: none / notify-only / review-after / review-before / full-override), **Reversibility** (ordinal 1–3: trivial / manual-but-bounded / destructive), **Failure mode visibility** (ordinal 1–3: immediate-to-editor / at-workflow-boundary / silent-until-review), **Cost** ($/1k actions, provider stated), **Implementation complexity** (LOC + new dependencies + Umbraco extension points used). Headline axes for thesis discussion: **Editor control**, **Failure mode visibility**, **Cost**.

**PII risk** (per-Pattern flag):
A flag, not an axis. Values: **none / possible / likely**, with a mitigation note (e.g., "Azure OpenAI with DPA mitigates; Gemini free tier does not"). Carried in the pattern table and discussed in a dedicated paragraph per pattern chapter — too important in a municipality context to bury in a column.

**Prompt-as-policy**:
A property of the **Gate / validator** Pattern: the prompt itself is the governance artefact (e.g., the Umbraco.AI tone-of-voice Context). For this Pattern, "Implementation complexity" lives largely in prompt authoring and version control rather than in code. Noted qualitatively, not as a separate axis.

**Implementation depth** (L1 / L2 / L3):
The bar a Pattern's implementation has to clear to count as evidence in the thesis.
- **L1** — console / harness only. A standalone runner against a corpus, no backoffice integration. Evidences quantitative axes only.
- **L2** — editor-usable in Umbraco backoffice. Telemetry captures all 6 axes. **Default depth.** "No production hardening" is explicit — queue durability, error recovery, provider failover are L3 concerns.
- **L3** — production-shaped (queues, recovery, monitoring, deployment). Out of thesis scope. Each Pattern chapter names its specific L3 concerns explicitly rather than implementing them.

**Demo-vehicle corpus**:
Content authored for the fictional Vejle Kommune site itself (~20–50 nodes). The integrated demo runs on this. Sufficient for editor-experience claims but too small for honest Latency/Cost numbers on Async patterns.

**Synthetic corpus**:
A purpose-sized content set scraped from publicly available vejle.dk pages (~100 nodes for translation, sized per pattern), used by L1 harnesses to produce credible quantitative measurements. Source is cited explicitly in the thesis; only public published content is used (no PII).

**L3 concerns**:
Production properties identified but deliberately not implemented. Per-Pattern named explicitly in the relevant chapter (e.g., for **Agent tool**: auth scope, approval gates, rollback UX). Naming them is part of the thesis contribution — it sets the research boundary honestly.

**Source variant**:
The DA culture variant of an Umbraco node — authoritative, authored or scraped. Translation pipelines read from this.

**Translated variant**:
The EN culture variant of an Umbraco node, AI-generated by the **Async transform** Pattern. **Unpublished by default**; an editor reviews and publishes.

**Variant publish gate**:
The editor's act of publishing a **Translated variant**. Acts as both the Editor control mechanism for **Async transform** (review-before on the ordinal axis) AND a citizen-safety guard against unreviewed AI content surfacing on a public municipal site. Configurable: an autopublish-on-translate setting bypasses the gate, dropping Editor control to "none". The thesis demos the gated default; the Pattern chapter documents both configurations as a sub-table.

**Provenance field**:
The cross-Pattern convention for measuring Editor control: every property an AI Pattern can populate is paired with `*GeneratedBy` (which provider/model produced it) and, where there's a review gate, `*AcceptedAt` (timestamp of editor acceptance). Concrete instances: `metaGeneratedBy` (SEO), `altGeneratedBy` + `altAcceptedAt` (alt text), `translationGeneratedBy` + `translationAcceptedAt` (translation). These fields make "editor override rate" a measured number rather than a prose claim — they ARE the headline Editor control evidence. Load-bearing for the thesis; not optional.

**AI call audit record**:
A metadata-only log entry written for every outbound AI call: `{patternName, nodeId, providerName, contentLengthChars, latencyMs, costUsd, timestampUtc}`. **Never the content itself.** Captures DPO-grade auditability ("what was sent where, when, how big") without storing or transmitting PII. Flows through the telemetry pipeline; required for all Patterns.

**Author-as-editor**:
The methodology choice for the thesis: the author plays the editor role when demonstrating Patterns. Selection bias is real and acknowledged explicitly in the methodology section. The Vejle Kommune digitalisering outreach is pursued as a stretch upgrade — if it lands, author-as-editor numbers become "preliminary validation, subsequently corroborated by N real-editor sessions"; if it doesn't, the thesis is intact.

**Pre-committed decision rules**:
The accept / edit / reject criteria for each Pattern, defined in the telemetry schema **before** the Pattern is implemented and before any AI output is observed. Standard technique for reducing selection bias in author-as-evaluator studies. The criteria are checked into the codebase alongside the telemetry schema; subsequent edits to them are themselves an audit signal.

**Path categorisation** (Runtime / Editorial / Design-time):
A second-order property of every Pattern — *where* in the system the AI call sits. It is structurally distinct from the Pattern itself.
- **Runtime path**: AI is in the request path. Latency is citizen-visible; cost is per-page-load. **Zero Patterns in this catalogue** — a deliberate scope choice (runtime-path AI in CMS is mostly personalisation, out of thesis scope).
- **Editorial path**: AI is in the editor's workflow. Latency is absorbed by the editor; cost is per-editor-action. **Six Patterns**: Sync suggestion (×2), Gate / validator, Async transform, Async analyze, Generative pipeline, Agent tool.
- **Design-time path**: AI is used to configure the system at build/dev time; the runtime is deterministic. **One Pattern**: Schema enrichment.
This categorisation is itself a thesis contribution — it draws the boundary between Patterns whose properties are dominated by editor-experience trade-offs vs. those dominated by request-time trade-offs. Schema enrichment is intentionally the only Design-time path Pattern; the contrast is the analytical move.

## Relationships

- A **Pattern** has zero or more **Use cases**
- A **Use case** belongs to exactly one **Pattern**
- All **Use cases** are exercised on the **Demo vehicle**
- The thesis evaluates **Patterns**; the **Demo vehicle** makes the evaluation tangible

## Example dialogue

> **Advisor:** "How do you justify keeping both SEO and alt text in the thesis?"
> **Author:** "They're both **Use cases** of the **Sync suggestion Pattern**, so methodologically they let me show that the Pattern's properties (low latency, editor in the loop, easy to override) hold across input modalities — text-in/text-out and image-in/text-out. If time runs out, the **Cut rule** says one of them goes."

## Flagged ambiguities

- "Feature" was used for both **Pattern** and **Use case** — resolved: a Feature is not a thesis-level concept; use **Pattern** for the architectural shape and **Use case** for its instance.
- "Async batch" was used for both translation and accessibility audit — resolved: these are distinct Patterns (**Async transform** mutates the CMS; **Async analyze** annotates alongside it). They differ in storage model, review workflow, and rollback semantics.
- "Governance" was initially proposed as a single evaluation axis — resolved: decomposed into **Reversibility**, **Failure mode visibility**, **Editor control**, **Cost**, and a separate **PII risk** flag. Tone-of-voice's "prompt-as-policy" property is captured qualitatively, not as a 7th axis.