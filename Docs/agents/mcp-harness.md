# MCP Harness — Agent Tool L1 Demo

Demonstrates a three-step agentic workflow: LLM initialises → lists Nyheder → reads an
article → creates a new related draft. Each step is a separate JSON-RPC call to the MCP
endpoint. In a real MCP client (e.g. Claude Desktop with MCP configured) these calls happen
automatically inside the LLM loop; here we drive them manually with curl.

## Endpoint

```
POST https://localhost:44337/umbraco/api/vejle/mcp
Content-Type: application/json
```

---

## Step 1 — Initialise the MCP session

```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
      "protocolVersion": "2024-11-05",
      "clientInfo": { "name": "vejle-harness", "version": "1.0" },
      "capabilities": {}
    }
  }' | python3 -m json.tool
```

Expected: server name `umbraco-vejle-kommune`, protocol `2024-11-05`, tools capability declared.

---

## Step 2 — Discover available tools

```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/list",
    "params": {}
  }' | python3 -m json.tool
```

Expected: four tools listed — `umbraco_list_content`, `umbraco_get_content`,
`umbraco_search_content`, `umbraco_create_draft`.

---

## Step 3 — List content under /nyheder

```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 3,
    "method": "tools/call",
    "params": {
      "name": "umbraco_list_content",
      "arguments": { "path": "/nyheder", "culture": "da-DK" }
    }
  }' | python3 -m json.tool
```

Expected: JSON array of news articles with keys, names, content types, and publish status.
Copy one `key` value for Step 4.

---

## Step 4 — Read a specific article

Replace `<KEY>` with a key from Step 3.

```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 4,
    "method": "tools/call",
    "params": {
      "name": "umbraco_get_content",
      "arguments": { "key": "<KEY>", "culture": "da-DK" }
    }
  }' | python3 -m json.tool
```

Expected: all property values for that article (headline, summary, body as unwrapped HTML,
publishedDate, etc.).

---

## Step 5 — Search content by keyword

```bash
curl -sk -X POST https://localhost:44337/umbraco/api/vejle/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 5,
    "method": "tools/call",
    "params": {
      "name": "umbraco_search_content",
      "arguments": { "query": "cykelsti", "contentTypeAlias": "newsArticle" }
    }
  }' | python3 -m json.tool
```

Expected: matching articles containing "cykelsti".

---

## Step 6 — Create a draft (write operation)

⚠ This mutates the CMS. At L1 there is no approval gate — the draft is created immediately.
At L2 this call would be intercepted and require backoffice approval before execution.

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
        "headline": "Ny gangbro åbner ved Vejle Å",
        "summary": "Vejle Kommune indvier en ny gangbro over Vejle Å som del af naturstien.",
        "body": "<p>Den nye gangbro over Vejle Å åbner officielt den 1. juni 2026.</p><p>Broen er 45 meter lang og er en del af den natursti, der forbinder Vejle centrum med skovområderne mod nord.</p>",
        "publishedDate": "2026-05-26"
      }
    }
  }' | python3 -m json.tool
```

Expected: `contentKey` of the new node + `backofficePath` link. Check the Umbraco backoffice
under Nyheder to see the unpublished draft.

---

## What this demonstrates

Running Steps 1–6 manually mirrors exactly what an LLM does autonomously inside an MCP loop.
Given a prompt like *"Browse Nyheder and create a follow-up article about the new bridge"*,
a Claude MCP client would: initialise → list tools → list /nyheder → read articles → call
`umbraco_create_draft` with synthesised content — all without human input between steps.

The review gate (unpublished draft + backoffice approval before publish) is the architectural
boundary that keeps the human in control of what actually reaches the public site.
