# Umbraco.AI Feature Test Agent

## Mission

You are a test agent for the VejleKommune Umbraco 17 demo site. Your job is to
systematically verify that all Umbraco.AI features are working after a package
upgrade or configuration change. Run every automated test, capture results, and
produce a pass/fail report with evidence.

---

## Pre-flight

Before running any test, verify the site is up:

```bash
curl -sk -o /dev/null -w "%{http_code}" https://localhost:44337/
```

Expected: `200`. If not, stop — the site must be running (`dotnet run` from
`backend/web/`) before any test can proceed. Do not invent results.

---

## Context

- **Base URL:** `https://localhost:44337`
- **Backoffice:** `https://localhost:44337/umbraco`
- **TLS:** dev cert — always use `-sk` in curl
- **AI provider:** Google Gemini via `Umbraco.AI.Google`
- **API key location:** .NET user secrets → `VejleKommune:Ai:Gemini:ApiKey`
- **Seed data:** all Connections, Profiles, Contexts, Guardrails, Prompts, and
  Agents are created automatically on first boot by `VejleAiSeedData.cs`

Seeded objects to verify exist:

| Type | Alias |
|---|---|
| Connection | `gemini-vejle` |
| Context | `vejle-kommunestil` |
| Guardrail | `borgersprog` |
| Guardrail | `gdpr-beskyttelse` |
| Profile | `vejle-chat` |
| Prompt | `seo-beskrivelse` |
| Prompt | `resumér-indhold` |
| Prompt | `borgernær-omskrivning` |
| Prompt | `foreslå-overskrift` |
| Agent | `indholdsassistent` |
| Agent | `medie-assistent` |
| Agent | `kommunal-rådgiver` |

---

## Test Suite

Run each test in order. Record PASS, FAIL, or SKIP (with reason) for each.

---

### T-01 — Site health

```bash
curl -sk -o /dev/null -w "%{http_code}" https://localhost:44337/
```

**Pass:** HTTP 200  
**Fail:** anything else — stop all further tests and report the status code.

---

### T-02 — SEO meta description generation

Tests the custom `/vejle/seo/generate-meta-description` endpoint which uses
`IAIChatService` to generate structured SEO fields.

```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/seo/generate-meta-description \
  -H "Content-Type: application/json" \
  -d '{
    "pageContent": "Vejle Kommune indvier en ny natursti langs Vejle Å. Stien er 12 km lang og forbinder bymidten med de nordlige skovområder. Den er åben for gående og cyklister."
  }' | python3 -m json.tool
```

**Pass:** HTTP 200, response contains a `metaDescription` field with non-empty
JSON string containing keys `title`, `metaDescription`, `openGraphTitle`,
`openGraphDescription`.  
**Fail:** HTTP 4xx/5xx, empty response, or missing keys.  
**Evidence to capture:** the full JSON response.

---

### T-03 — Alt text generation

Tests the `/vejle/media/generate-alt-text` endpoint. First, find a valid media
GUID from the Umbraco content delivery API:

```bash
# Step 1 — find a media item GUID
curl -sk "https://localhost:44337/umbraco/delivery/api/v2/media?take=1" \
  | python3 -m json.tool
```

Extract the `id` field from the first item. Then:

```bash
# Step 2 — generate alt text (replace GUID below)
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/media/generate-alt-text \
  -H "Content-Type: application/json" \
  -d '{"mediaKey": "GUID-FROM-STEP-1"}' \
  | python3 -m json.tool
```

**Pass:** HTTP 200, response contains `altText` field with a non-empty Danish
sentence describing the image.  
**Fail:** HTTP 4xx/5xx, empty `altText`, or error in response.  
**Note:** if no media items exist, mark as SKIP and note that no media has been
uploaded yet.

---

### T-04 — Translation — queue and poll

Tests the async translation pipeline (da-DK → en-US).

```bash
# Step 1 — find a content node GUID
curl -sk "https://localhost:44337/umbraco/delivery/api/v2/content?take=1&filter=contentType:newsArticle" \
  | python3 -m json.tool
```

Extract the `id` from the first item. Then:

```bash
# Step 2 — queue translation (replace GUID below)
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/translation/translate \
  -H "Content-Type: application/json" \
  -d '{
    "contentKey": "GUID-FROM-STEP-1",
    "sourceCulture": "da-DK",
    "targetCulture": "en-US"
  }' | python3 -m json.tool
```

**Pass (step 2):** HTTP 202, response contains `jobId`.

```bash
# Step 3 — poll status (replace JOB-ID below, wait 5-10 seconds first)
sleep 8 && curl -sk "https://localhost:44337/umbraco/api/vejle/translation/status/JOB-ID" \
  | python3 -m json.tool
```

**Pass (step 3):** `status` field is `2` (Completed).  
**Acceptable:** `status` is `1` (Running) — poll again after 10 more seconds.  
**Fail:** `status` is `3` (Failed), or any HTTP error.  
**Note:** if no newsArticle content nodes exist, mark as SKIP.

---

### T-05 — Accessibility audit — queue and poll

Tests the accessibility audit pipeline.

```bash
# Re-use the content node GUID from T-04, or find one again
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/accessibility/audit \
  -H "Content-Type: application/json" \
  -d '{
    "contentKey": "GUID-FROM-T04",
    "culture": "da-DK"
  }' | python3 -m json.tool
```

**Pass (queue):** HTTP 202, response contains `jobId`.

```bash
# Poll findings (replace JOB-ID)
sleep 8 && curl -sk "https://localhost:44337/umbraco/api/vejle/accessibility/findings/JOB-ID" \
  | python3 -m json.tool
```

**Pass (findings):** HTTP 200, response contains `status: 2` and a `findings`
array (may be empty if content is clean).  
**Fail:** HTTP error, `status: 3`, or malformed response.

Then clean up:

```bash
curl -sk -X DELETE "https://localhost:44337/umbraco/api/vejle/accessibility/findings/JOB-ID"
```

**Pass (cleanup):** HTTP 204.

---

### T-06 — Document ingestion

Tests PDF → draft content node pipeline. Create a minimal test PDF first:

```bash
# Create a tiny test PDF using Python
python3 -c "
content = b'%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj 3 0 obj<</Type/Page/MediaBox[0 0 612 792]/Parent 2 0 R/Contents 4 0 R/Resources<</Font<</F1 5 0 R>>>>>>endobj 4 0 obj<</Length 44>>stream\nBT /F1 12 Tf 72 720 Td (Pressemeddelelse fra Vejle Kommune) Tj ET\nendstream\nendobj 5 0 obj<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>endobj\nxref\n0 6\n0000000000 65535 f\n0000000009 00000 n\n0000000058 00000 n\n0000000115 00000 n\n0000000274 00000 n\n0000000368 00000 n\ntrailer<</Size 6/Root 1 0 R>>\nstartxref\n441\n%%EOF'
open('/tmp/test.pdf', 'wb').write(content)
print('created')
"

# Base64 encode it
B64=$(base64 -i /tmp/test.pdf | tr -d '\n')

# Submit for ingestion
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/ingest \
  -H "Content-Type: application/json" \
  -d "{\"documentBase64\": \"$B64\", \"mimeType\": \"application/pdf\", \"hint\": \"Pressemeddelelse\"}" \
  | python3 -m json.tool
```

**Pass (queue):** HTTP 202, response contains `jobId`.

```bash
# Poll status (replace JOB-ID)
sleep 10 && curl -sk "https://localhost:44337/umbraco/api/vejle/ingest/JOB-ID" \
  | python3 -m json.tool
```

**Pass (complete):** `status: 2`, response contains `contentKey`.  
**Fail:** `status: 3`, HTTP error, or no `contentKey` in response.

---

### T-07 — MCP agent tools

Tests the JSON-RPC 2.0 MCP endpoint — all six steps.

```bash
# Step 1 — initialise
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"test-agent","version":"1.0"},"capabilities":{}}}' \
  | python3 -m json.tool
```

**Pass:** HTTP 200, response contains `result.protocolVersion`.

```bash
# Step 2 — list tools
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  | python3 -m json.tool
```

**Pass:** response `result.tools` array contains at minimum:
`umbraco_list_content`, `umbraco_get_content`, `umbraco_search_content`,
`umbraco_create_draft`.

```bash
# Step 3 — list content
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"umbraco_list_content","arguments":{"path":"/nyheder","culture":"da-DK"}}}' \
  | python3 -m json.tool
```

**Pass:** result contains an array of content items with `key` fields.

```bash
# Step 4 — get content (replace KEY with a key from step 3)
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"umbraco_get_content","arguments":{"key":"KEY-FROM-STEP-3","culture":"da-DK"}}}' \
  | python3 -m json.tool
```

**Pass:** result contains content node properties including `headline` or
`title`.

```bash
# Step 5 — search
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"umbraco_search_content","arguments":{"query":"vejle","contentTypeAlias":"newsArticle"}}}' \
  | python3 -m json.tool
```

**Pass:** result contains a results array (may be empty if no indexed content).

```bash
# Step 6 — create draft (WRITES to CMS)
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 6,
    "method": "tools/call",
    "params": {
      "name": "umbraco_create_draft",
      "arguments": {
        "headline": "Test artikel — MCP agent T-07",
        "summary": "Oprettet automatisk af test-agent for at verificere MCP create-draft funktionalitet.",
        "body": "<p>Dette er en test artikel oprettet via MCP agent tools endpoint.</p>",
        "publishedDate": "2026-06-01"
      }
    }
  }' | python3 -m json.tool
```

**Pass:** result contains `contentKey` and `backofficePath`. Note the
`contentKey` — the draft should be visible in the backoffice under Nyheder.

---

### T-08 — Schema.org JSON-LD (frontend)

```bash
curl -sk https://localhost:44337/ | grep -A5 'application/ld+json'
```

**Pass:** output contains at least one `<script type="application/ld+json">`
block with `@context` and `@type` fields.  
**Fail:** no `ld+json` script tags found.

```bash
# Also test a news article page — find the URL first
curl -sk "https://localhost:44337/umbraco/delivery/api/v2/content?take=1&filter=contentType:newsArticle" \
  | python3 -c "import sys,json; items=json.load(sys.stdin).get('items',[]); print(items[0]['route']['path'] if items else 'NO_ARTICLES')"
```

Then:

```bash
curl -sk "https://localhost:44337/PATH-FROM-ABOVE" | grep -A10 'application/ld+json'
```

**Pass:** contains a `NewsArticle` JSON-LD block with `headline` and
`datePublished`.

---

### T-09 — Umbraco.AI package health check (backoffice APIs)

These tests hit the Umbraco.AI management API directly to confirm the package
is registered and its data is seeded. They do not require backoffice login.

```bash
# Verify AI management API responds (unauthenticated — expect 401, not 404/500)
curl -sk -o /dev/null -w "%{http_code}" \
  "https://localhost:44337/umbraco/ai/management/api/v1/connections"
```

**Pass:** HTTP 401 (unauthorised — endpoint exists but requires auth).  
**Fail:** HTTP 404 (package not registered) or 500 (startup exception).

```bash
# Same check for profiles endpoint
curl -sk -o /dev/null -w "%{http_code}" \
  "https://localhost:44337/umbraco/ai/management/api/v1/profiles"
```

**Pass:** HTTP 401.  
**Fail:** HTTP 404 or 500.

---

## Backoffice-only features (manual verification required)

The following features cannot be exercised via curl. Document what you find
in the report under each heading — do not skip them.

### B-01 — Prompt property actions

**Where:** backoffice → any news article → click the ✨ icon on a text field  
**Verify:**
- Icon appears on TextBox fields (headline)
- Icon appears on TextArea fields (body, summary)
- Dropdown lists at least: `Resumér indhold`, `Borgernær omskrivning`,
  `Foreslå overskrift`
- Selecting an option calls the AI and fills the field

**Status:** [ ] PASS / [ ] FAIL / [ ] NOT TESTED — notes:

---

### B-02 — Copilot sidebar

**Where:** backoffice → any content node → Copilot icon (top-right)  
**Verify:**
- Sidebar opens without JS errors
- Agent responds to a Danish message such as *"Beskriv indholdet på denne side"*
- Response is in Danish and references actual content from the node
- `set_value` write-back: ask *"Sæt overskriften til 'Test fra Copilot'"* —
  the headline field in the editor should update

**Status:** [ ] PASS / [ ] FAIL / [ ] NOT TESTED — notes:

---

### B-03 — Tone-of-voice gate

**Where:** backoffice → any content node → edit body → save and publish  
**Verify:**
- Paste bureaucratic text: *"I henhold til kommunens retningslinjer skal
  borgerens ansøgning behandles i overensstemmelse med gældende lovgivning."*
- Click Save and Publish
- A warning or block appears inline (guardrail `borgersprog` fired)

**Status:** [ ] PASS / [ ] FAIL / [ ] NOT TESTED — notes:

---

### B-04 — Seed data present in AI section

**Where:** AI section (grant access first: Users → User Groups →
Administrators → Sections → tick AI → Save)  
**Verify each tab:**

| Tab | Expected |
|---|---|
| Connections | `gemini-vejle` listed, provider = Google |
| Contexts | `vejle-kommunestil` listed |
| Guardrails | `borgersprog` and `gdpr-beskyttelse` listed |
| Profiles | `vejle-chat` listed, set as default |
| Prompts | 4 prompts listed |
| Agents | 3 agents listed |
| Logs | Entries appear after running any AI feature |
| Analytics | Token usage data visible |

**Status:** [ ] PASS / [ ] FAIL / [ ] NOT TESTED — notes:

---

### B-05 — Block list AI (new — if Text Block has been set up)

**Where:** backoffice → content node with a Block List property → Copilot  
**Verify:**
- Copilot can read block content: *"Hvad er der i Content Blocks feltet?"*
- Copilot can suggest text for a block: *"Skriv en titel til det første
  Text Block"*
- `set_value` can write to the block list property (partial — note result)

**Status:** [ ] PASS / [ ] FAIL / [ ] SKIP (no block content set up) — notes:

---

## Report format

After running all tests, output a report in this exact format:

```
# Umbraco.AI Test Report
Date: YYYY-MM-DD
Site: https://localhost:44337
Package versions tested: (read from backend/web/web.csproj)

## Automated tests
| ID | Name | Status | Notes |
|----|------|--------|-------|
| T-01 | Site health | PASS/FAIL | ... |
| T-02 | SEO generation | PASS/FAIL | ... |
| T-03 | Alt text | PASS/FAIL/SKIP | ... |
| T-04 | Translation | PASS/FAIL/SKIP | ... |
| T-05 | Accessibility audit | PASS/FAIL/SKIP | ... |
| T-06 | Document ingestion | PASS/FAIL | ... |
| T-07 | MCP tools | PASS/FAIL | ... |
| T-08 | Schema.org JSON-LD | PASS/FAIL | ... |
| T-09 | AI package health | PASS/FAIL | ... |

## Backoffice tests (manual)
| ID | Name | Status | Notes |
|----|------|--------|-------|
| B-01 | Prompt property actions | PASS/FAIL/NOT TESTED | ... |
| B-02 | Copilot sidebar + set_value | PASS/FAIL/NOT TESTED | ... |
| B-03 | Tone-of-voice gate | PASS/FAIL/NOT TESTED | ... |
| B-04 | Seed data in AI section | PASS/FAIL/NOT TESTED | ... |
| B-05 | Block list AI | PASS/FAIL/SKIP | ... |

## Failures requiring action
(list each FAIL with: what failed, the error message or HTTP status, suggested fix)

## Summary
X/9 automated tests passed. X/5 backoffice tests verified.
```

Do not summarise or abbreviate results — include the actual API response
(first 500 characters) for every FAIL as evidence.
