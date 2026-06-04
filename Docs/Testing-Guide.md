# Feature Testing Guide

> **Base URL:** `https://localhost:44337`
> All curl commands use `-sk` (skip TLS cert check — dev cert only). Run the site first: `dotnet run` from `backend/web/` or press F5 in your IDE.

---

## 0 — Umbraco.AI backoffice setup

### Seed data — auto-configured on first boot

`VejleAiSeedData.cs` runs automatically on `UmbracoApplicationStartedNotification` and creates everything below if it doesn't already exist. You do **not** need to click through the backoffice to create Connections, Profiles, Prompts, or Agents — they are all seeded for you.

| Object | Alias / ID | Notes |
|---|---|---|
| **Connection** | `gemini-vejle` | Google provider, API key resolved from user secret |
| **Context** | `vejle-kommunestil` | Danish municipal brand voice, injected into every request |
| **Guardrail** | `borgersprog` | LLM-judge warns on bureaucratic language (post-generate) |
| **Guardrail** | `gdpr-beskyttelse` | Regex redacts CPR numbers, emails, Danish phone numbers |
| **Profile** | `vejle-chat` | Gemini 2.5 Flash, temp 0.4, set as default chat profile |
| **Prompt** | `seo-beskrivelse` | SEO title + meta desc, sends full content node to AI |
| **Prompt** | `resumér-indhold` | Summarise body text, 3 style options |
| **Prompt** | `borgernær-omskrivning` | Rewrite bureaucratic text in plain Danish |
| **Prompt** | `foreslå-overskrift` | Suggest headlines (max 60 chars), 3 options |
| **Agent** | `indholdsassistent` | Content section, surfaces in Copilot sidebar |
| **Agent** | `medie-assistent` | Media section, focused on alt text |
| **Agent** | `kommunal-rådgiver` | Content section, municipal services advisor |

**Before first run** — set your Gemini API key in user secrets (one-time setup):

```bash
cd backend/web
dotnet user-secrets set "VejleKommune:Ai:Gemini:ApiKey" "YOUR-GEMINI-API-KEY"
```

### Step 1 — Grant yourself access to the AI section (required)

The AI section is standalone and not included in any user group by default.

1. Log into `https://localhost:44337/umbraco`
2. Navigate to **Users → User Groups → Administrators**
3. Under **Sections**, tick **AI**
4. Save the group, then refresh your browser — a new **AI** section appears in the left nav

Once you open the AI section you'll find the seeded Connection, Context, Guardrails, Profile, Prompts, and Agents already waiting. No further manual configuration is needed for the 8 core features to work.

---

## 1 — Schema.org / JSON-LD enrichment

**What to check:** structured data blocks appear in the page `<head>`.

**How to test:**
1. Open any published page in a browser, e.g. `https://localhost:44337`
2. Right-click → View Page Source
3. Search for `application/ld+json` — you should find one or more `<script>` blocks
4. The home page produces two blocks: `WebSite` and `GovernmentOrganization`
5. A news article page produces a `NewsArticle` block with `headline`, `datePublished`, `url`
6. An event page produces an `Event` block with `startDate`, `endDate`, and `location`

**What good output looks like:**
```html
<script type="application/ld+json">
{"@context":"https://schema.org","@type":"WebSite","url":"https://localhost:44337/","name":"Velkommen til Vejle Kommune","inLanguage":"da-DK"}
</script>
```

**No AI involved** — this is deterministic at render time. If the block is missing, check that the layout view calls `SchemaOrgRenderer.RenderJsonLd(page)`.

---

## 2 — SEO Meta Description generation

**Method A — Backoffice button (real editor flow):**
1. Log into the Umbraco backoffice: `https://localhost:44337/umbraco`
2. Open any content node (e.g. a news article under Nyheder)
3. Click the **SEO** tab (added by SeoToolkit)
4. Click **✨ Generate with AI**
5. Wait ~2–4 seconds — Gemini fills in Title, Meta Description, OG Title, OG Description
6. Review the fields, then Save if happy

**Method B — Direct API (tests the full stack without the backoffice):**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/seo/generate-meta-description \
  -H "Content-Type: application/json" \
  -d '{
    "pageContent": "Vejle Kommune indvier en ny natursti langs Vejle Å. Stien er 12 km lang og forbinder bymidten med de nordlige skovområder. Den er åben for gående og cyklister."
  }' | python3 -m json.tool
```

**Expected response:**
```json
{
  "metaDescription": "{\"title\":\"...\",\"metaDescription\":\"...\",\"openGraphTitle\":\"...\",\"openGraphDescription\":\"...\"}"
}
```

> **160-character hint** — that message is SeoToolkit's own built-in UI validation on the meta description field. The AI is instructed via the system prompt in `VejleAiGenerationService.cs` to keep output under 160 chars, but the UI hint is always visible regardless.

---

## 3 — Tone-of-voice gate

**What it does:** blocks (or warns) when you try to publish content that fails Vejle's tone guidelines.

**How to test:**
1. Open any content node in the backoffice
2. Edit the body text — paste something deliberately bureaucratic/jargon-heavy:
   > *"I henhold til kommunens retningslinjer skal borgerens ansøgning behandles i overensstemmelse med gældende lovgivning og administrative procedurer."*
3. Click **Save and Publish**
4. The gate fires — if the AI classifies it as a tone failure, you see a red error message inline in the backoffice with the reason and suggestions
5. The publish is blocked; the node stays in draft

**Testing a pass:** write a clear, citizen-friendly sentence and publish — it should go through without any message.

**Fail-open behaviour:** if Gemini is unavailable (quota, network), the publish proceeds silently. The gate never blocks due to AI infrastructure failure — check the Umbraco log (`/umbraco/Data/Logs/`) to see the error entry.

> **Note:** there is no API endpoint for the tone gate — it lives entirely inside the `ContentPublishingNotification` pipeline. The only way to trigger it is through the backoffice publish action.

---

## 4 — Alt text generation

**Requires:** a media item GUID. Find it in the backoffice: Media section → click an image → the URL contains the GUID, or check the node key in the Info tab.

```bash
# Replace the GUID with your own media item key
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/media/generate-alt-text \
  -H "Content-Type: application/json" \
  -d '{
    "mediaKey": "e9cb2b4e-5808-4c94-98fd-832b15ebad01"
  }' | python3 -m json.tool
```

**Expected response:**
```json
{
  "altText": "To voksne og to børn cykler på en grøn cykelsti omgivet af træer. Alle bærer hjelme."
}
```

**Finding your media GUID:**
- Backoffice → Media section → click any image
- Look at the browser URL: `.../edit/NNNN` gives the integer ID, not the GUID
- Use the **Info** tab on the media node — the Key field shows the GUID
- Or right-click any image on the site → copy image URL → find the media item in the Media section by name

**Supported formats:** JPEG, PNG, GIF, WebP. SVG and PDF return 400.

---

## 5 — Translation (da-DK → en-US)

**Requires:** the GUID key of a published content node. Find it the same way as media — backoffice → Info tab on any content node → Key field.

**Step 1 — Queue the job:**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/translation/translate \
  -H "Content-Type: application/json" \
  -d '{
    "contentKey": "YOUR-CONTENT-NODE-GUID-HERE",
    "sourceCulture": "da-DK",
    "targetCulture": "en-US"
  }' | python3 -m json.tool
```

**Expected response (202 Accepted):**
```json
{
  "jobId": "a1b2c3d4e5f6...",
  "message": "Translation queued. Poll /umbraco/api/vejle/translation/status/... for progress."
}
```

**Step 2 — Poll for completion (copy the jobId from above):**
```bash
curl -sk https://localhost:44337/umbraco/api/vejle/translation/status/YOUR-JOB-ID \
  | python3 -m json.tool
```

**Status values:** `0` = Queued, `1` = Running, `2` = Completed, `3` = Failed

**When completed:** open the content node in the backoffice — switch the language dropdown at the top to English (United States). You should see all text fields filled in English, saved as an **unpublished draft**. Review and click Publish to make it live.

---

## 6 — Accessibility audit

**Requires:** the GUID key of a content node (same as translation above).

**Step 1 — Queue the audit:**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/accessibility/audit \
  -H "Content-Type: application/json" \
  -d '{
    "contentKey": "YOUR-CONTENT-NODE-GUID-HERE",
    "culture": "da-DK"
  }' | python3 -m json.tool
```

**Step 2 — Poll for findings:**
```bash
curl -sk https://localhost:44337/umbraco/api/vejle/accessibility/findings/YOUR-JOB-ID \
  | python3 -m json.tool
```

**Expected findings response:**
```json
{
  "jobId": "...",
  "status": 2,
  "findings": [
    {
      "field": "body",
      "issue": "Sætningen er meget lang og svær at følge",
      "suggestion": "Del sætningen op i to kortere sætninger",
      "severity": "warning"
    }
  ]
}
```

**Step 3 — Dismiss findings (rollback — marks the audit as reviewed):**
```bash
curl -sk -X DELETE https://localhost:44337/umbraco/api/vejle/accessibility/findings/YOUR-JOB-ID
```
Returns 204 No Content. The findings are removed from memory. The content node itself is **never modified** by the audit — only findings are stored/dismissed.

---

## 7 — Document ingestion (PDF → draft content node)

**Requires:** a PDF file. The test document from Phase 7 development was a Danish press release. Any PDF works.

**Step 1 — Base64-encode your PDF:**
```bash
# macOS / Linux
base64 -i your-document.pdf | tr -d '\n' > doc.b64

# Then copy the contents of doc.b64 into the curl command below
```

**Step 2 — Submit for ingestion:**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/ingest \
  -H "Content-Type: application/json" \
  -d '{
    "documentBase64": "PASTE-BASE64-STRING-HERE",
    "mimeType": "application/pdf",
    "hint": "Pressemeddelelse fra Teknik og Miljø"
  }' | python3 -m json.tool
```

**The `hint` field is optional** but improves extraction quality — tell the AI what kind of document it is.

**Step 3 — Poll for completion:**
```bash
curl -sk https://localhost:44337/umbraco/api/vejle/ingest/YOUR-JOB-ID \
  | python3 -m json.tool
```

**When completed:** the response includes `contentKey`. Open the backoffice → Content → Nyheder — a new unpublished draft appears with headline, summary, body, and date extracted from the PDF. Review and publish from the backoffice.

> **DOCX also works:** change `mimeType` to `application/vnd.openxmlformats-officedocument.wordprocessingml.document`.

---

## 8 — MCP Agent Tools

The MCP server is a JSON-RPC 2.0 endpoint. All six steps below hit the same URL.

**Endpoint:** `POST https://localhost:44337/umbraco/api/vejle/mcp`

**Step 1 — Initialise:**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"test","version":"1.0"},"capabilities":{}}}' \
  | python3 -m json.tool
```

**Step 2 — List available tools:**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  | python3 -m json.tool
```
Expected: four tools — `umbraco_list_content`, `umbraco_get_content`, `umbraco_search_content`, `umbraco_create_draft`.

**Step 3 — List content under /nyheder:**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"umbraco_list_content","arguments":{"path":"/nyheder","culture":"da-DK"}}}' \
  | python3 -m json.tool
```
Copy a `key` value from the response for the next step.

**Step 4 — Read a specific node:**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"umbraco_get_content","arguments":{"key":"PASTE-KEY-HERE","culture":"da-DK"}}}' \
  | python3 -m json.tool
```

**Step 5 — Search content:**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"umbraco_search_content","arguments":{"query":"cykelsti","contentTypeAlias":"newsArticle"}}}' \
  | python3 -m json.tool
```

**Step 6 — Create a draft (⚠ writes to CMS):**
```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 6,
    "method": "tools/call",
    "params": {
      "name": "umbraco_create_draft",
      "arguments": {
        "headline": "Test artikel oprettet via MCP",
        "summary": "Denne artikel blev oprettet automatisk via MCP agent tools.",
        "body": "<p>Dette er brødteksten til testartiklen.</p>",
        "publishedDate": "2026-05-27"
      }
    }
  }' | python3 -m json.tool
```
Expected: `contentKey` + `backofficePath`. The draft appears under Nyheder in the backoffice as unpublished.

---

## 9 — Umbraco.AI Prompt property actions

The `Umbraco.AI.Prompt` package adds an AI action dropdown directly next to text and textarea fields in the backoffice content editor. The four seeded prompts appear here automatically.

**How to use:**
1. Open any content node in the backoffice (e.g. a news article under Nyheder)
2. Click into a text field — a small **✨** or AI wand icon appears at the right edge of the field
3. Click the icon — a dropdown lists the available prompts scoped to that property editor:
   - **Resumér indhold** (summarise) — on TextArea fields: body, summary
   - **Borgernær omskrivning** (plain-language rewrite) — on TextArea fields
   - **Foreslå overskrift** (suggest headline) — on TextBox fields: headline, title
   - **SEO-beskrivelse** — available via the SEO tab's Generate button (uses `IncludeEntityContext`)
4. Select a prompt option — the AI rewrites or fills the field in place
5. Accept or discard the suggestion before saving

**What the prompts do (seeded configuration):**

| Prompt alias | Field types | Options | What it does |
|---|---|---|---|
| `resumér-indhold` | TextArea | Kort / Mellemlang / Detaljeret | Summarises the current field value in 1–3 sentences |
| `borgernær-omskrivning` | TextArea | (single option) | Rewrites bureaucratic text in clear citizen-facing Danish |
| `foreslå-overskrift` | TextBox | Formel / Engagerende / Spørgsmålsform | Proposes a headline ≤60 chars based on the body |
| `seo-beskrivelse` | TextArea + TextBox | (single option) | Generates SEO title + meta desc using the full content node |

> **Context is always injected** — every prompt call includes the `vejle-kommunestil` context resource, so all output follows Vejle's brand voice and tone guidelines without any extra configuration.

---

## 10 — Umbraco.AI Copilot chat sidebar

The `Umbraco.AI.Agent.Copilot` package adds a chat panel that slides in from the right side of the backoffice. Three agents are seeded and available immediately.

**How to open the Copilot sidebar:**
1. Open any content node or media item in the backoffice
2. Look for the **Copilot** icon in the top-right corner of the editor (speech bubble or sparkle icon)
3. Click it — the sidebar slides in with a chat interface
4. The active agent is chosen based on the section you are in (Content → `indholdsassistent` or `kommunal-rådgiver`, Media → `medie-assistent`)

**Seeded agents:**

**`indholdsassistent` — Content assistant**
- Available in: Content section
- What to try: *"Skriv et resumé af denne artikel"*, *"Hvad mangler der i dette indhold?"*, *"Foreslå en bedre overskrift"*
- The agent has access to the current content node and can read its fields

**`kommunal-rådgiver` — Municipal advisor**
- Available in: Content section
- What to try: *"Hvad er kommunens procedure for byggetilladelser?"*, *"Hjælp mig med at forklare denne regel på borgerdansk"*
- Focused on municipal services and citizen communication

**`medie-assistent` — Media assistant**
- Available in: Media section
- What to try: *"Beskriv dette billede"*, *"Generer en alt-tekst til dette billede"*
- Focused on accessibility and image descriptions

> **Note:** if the Copilot icon is not visible, confirm that `Umbraco.AI.Agent.Copilot` is installed (`web.csproj` ✓) and that the `indholdsassistent` agent has `SurfaceIds = ["copilot"]` (already set by seed data).

---

## 11 — Umbraco.AI backoffice section — what to explore

After completing Step 0 (grant AI section access), the **AI** section in the left nav exposes the full configuration UI. Everything below was seeded automatically — these views are for inspection and adjustment.

### Connections
AI section → **Connections** → `gemini-vejle`

Shows the Google provider, model settings, and that the API key is stored as a configuration reference (`$VejleKommune:Ai:Gemini:ApiKey`) rather than a plain-text value. To test with a different model or provider, create a second connection here and assign it to a new profile.

### Contexts
AI section → **Contexts** → `vejle-kommunestil`

The brand-voice resource injected into every AI call. Edit `ToneDescription`, `TargetAudience`, `StyleGuidelines`, and `AvoidPatterns` here to change how the AI writes without touching any code. Changes take effect immediately.

### Guardrails
AI section → **Guardrails**

Two guardrails are seeded:

- **`borgersprog`** — LLM-judge rule. Fires post-generate and warns (does not block) when AI output contains bureaucratic language patterns. Check the Logs section to see when it has triggered.
- **`gdpr-beskyttelse`** — Regex rules. Automatically redacts CPR numbers (`\b\d{6}[-–]?\d{4}\b`), email addresses, and Danish phone numbers from AI output before it is written to content fields.

To test `gdpr-beskyttelse`: open any prompt action on a text field and manually include a CPR number in the field value. The redacted version should appear in the AI output.

### Profiles
AI section → **Profiles** → `vejle-chat`

The default chat profile: Gemini 2.5 Flash, temperature 0.4, with `vejle-kommunestil` context and both guardrails attached. To experiment with a different model (e.g. `gemini-2.0-flash` for lower cost), edit the model here — no code change needed.

### Prompts
AI section → **Prompts**

All four seeded prompts appear here. Each shows its alias, the property editor scopes it appears in, and the option list. Edit prompt text, add new options, or adjust `IncludeEntityContext` from this UI.

### Agents
AI section → **Agents**

All three seeded agents appear here with their section scope and surface bindings. The `SurfaceIds = ["copilot"]` binding is what causes them to appear in the Copilot sidebar.

### Logs
AI section → **Logs**

Every AI call made through Umbraco.AI is logged here: timestamp, profile used, token counts, and the full prompt/response. Useful for debugging unexpected output or verifying that the context and guardrails fired as expected.

### Analytics
AI section → **Analytics**

Aggregated usage statistics: calls per day, token consumption by profile, average response time. Useful for estimating API costs and identifying which features are used most heavily.

---

## Quick reference — all endpoints

| Feature | Method | Endpoint |
|---|---|---|
| SEO meta (API test) | POST | `/umbraco/api/vejle/seo/generate-meta-description` |
| Alt text | POST | `/umbraco/api/vejle/media/generate-alt-text` |
| Translation — queue | POST | `/umbraco/api/vejle/translation/translate` |
| Translation — status | GET | `/umbraco/api/vejle/translation/status/{jobId}` |
| Accessibility — queue | POST | `/umbraco/api/vejle/accessibility/audit` |
| Accessibility — findings | GET | `/umbraco/api/vejle/accessibility/findings/{jobId}` |
| Accessibility — dismiss | DELETE | `/umbraco/api/vejle/accessibility/findings/{jobId}` |
| Document ingestion — queue | POST | `/umbraco/api/vejle/ingest` |
| Document ingestion — status | GET | `/umbraco/api/vejle/ingest/{jobId}` |
| MCP agent tools | POST | `/umbraco/api/vejle/mcp` |

| Feature | How to test |
|---|---|
| Schema.org JSON-LD | View page source → search `ld+json` |
| SEO meta generation | Backoffice → content node → SEO tab → ✨ Generate with AI |
| Tone-of-voice gate | Backoffice → publish a node with jargon-heavy text |
| Prompt property actions | Backoffice → content node → click ✨ icon on any text field |
| Copilot sidebar | Backoffice → content/media node → click Copilot icon (top right) |
| Contexts / Guardrails | AI section → Contexts / Guardrails |
| Call logs | AI section → Logs |
| Usage analytics | AI section → Analytics |

---

*Base URL for all endpoints: `https://localhost:44337`*
