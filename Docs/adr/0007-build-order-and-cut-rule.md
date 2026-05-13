# Build order and Cut rule

Patterns are built in this order, with Phase 0 (cross-cutting infrastructure) collapsed into Phase 1 (Schema enrichment) so the first demoable deliverable is a running Vejle Kommune site rendering valid Schema.org JSON-LD on every page — not 1–2 weeks of invisible plumbing.

| Phase | Pattern / scope | Why this slot |
|---|---|---|
| **0+1 (combined)** | Content model, seed content, minimal Razor views, provider abstraction (Gemini stub), telemetry skeleton, **Schema enrichment** complete | First milestone is a running, schema-valid site. Validates content model + frontend pipeline before any AI dependency. |
| **2** | Sync suggestion (SEO) | Simplest AI call; exercises provider abstraction and telemetry; provenance fields debut. |
| **3** | Gate / validator (tone of voice) | Most visible Umbraco.AI-native capability; validates Umbraco.AI Contexts wiring before more complex Patterns build on top. (Swapped earlier than my initial proposal.) |
| **4** | Sync suggestion (alt text) | Adds multimodal on top of a now-twice-validated provider + event-hook stack; cheap once Phases 2–3 are done. |
| **5** | Async transform (translation) | First async Pattern; builds the background job runner; adds Variant publish gate UX; includes L1 harness on synthetic 100-node corpus. |
| **6** | Async analyze (accessibility) | Reuses the background job runner; adds findings UX; L1 harness. |
| **7** | Generative pipeline (doc → content) | Most complex backoffice integration; reuses provider abstraction and the unpublished-by-default review gate (third use). |
| **8** | Agent tool (MCP migration) | Last because L1-only; structurally separate from the L2 backoffice infrastructure; doesn't pay back into later Phases. |

**Cut rule:** Drop Patterns from the bottom of the build order under time pressure. Cut order: Agent tool → Generative pipeline → Async analyze → Gate / validator. **Floor: Sync suggestion (×2) + Async transform.** Below the floor, descope L2 → L1 for a Pattern at the floor before cutting it. Schema enrichment costs almost nothing relative to its analytical value (Phase 0+1 footprint) and sits in the "cut last" bucket above the floor, not on it.

**Schema enrichment is defended via Option A: AI as Design-time path, contrasted with the six Editorial path Patterns.** Per-page rendering is purely deterministic; the LLM is used at dev time to populate `schemaTypeOverride` values for unusual cases. Chapter intro: "Unlike the six runtime-path Patterns, Schema enrichment uses AI as a design-time configuration tool; the per-page rendering path is purely deterministic." (Note: "runtime-path" in that sentence is shorthand for "non-design-time"; the full path categorisation in CONTEXT.md splits non-design-time into Runtime path and Editorial path — all six are Editorial path.)
