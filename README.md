# Vejle Kommune — AI Thesis Demo

Umbraco 17 project demonstrating 8 AI use cases in a municipal CMS context.
Built as part of a graduation thesis at Limbo Works.

## Prerequisites

- .NET 10 SDK
- Docker (for SQL Server)
- Node.js (frontend tooling, if applicable)

## Local setup

### 1. Start SQL Server

```bash
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrong!Passw0rd" \
  -p 1433:1433 --name vejle-sql -d mcr.microsoft.com/mssql/server:2022-latest
```

### 2. Configure secrets

All secrets are stored via `dotnet user-secrets`. Never commit real values to `appsettings.json`.

```bash
cd backend/web

dotnet user-secrets set "ConnectionStrings:umbracoDbDSN" \
  "Server=localhost,1433;Database=VejleKommune_dev;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=true"

dotnet user-secrets set "VejleKommune:Ai:Gemini:ApiKey" "<value from 1Password>"
```

### 3. Run

```bash
cd backend/web
dotnet run
```

Umbraco installs unattended on first boot. Default backoffice credentials:

- **URL**: https://localhost:44337/umbraco
- **Email**: admin@vejle-thesis.dk
- **Password**: Admin1234!

## Project structure

```
backend/
  code/     — Domain logic, AI providers, bootstrap, SEO
  web/      — Umbraco host, views, appsettings
Docs/
  adr/      — Architecture Decision Records (ADR-0001 through ADR-0011)
CLAUDE.md   — Claude Code agent instructions
LIMBO_GUIDELINES.md — Limbo coding standards
```

## AI use cases

| # | Feature | Status |
|---|---------|--------|
| 1 | Content generation | Planned |
| 2 | SEO meta description | Planned |
| 3 | Translation (da-DK → en-US) | Planned |
| 4 | Image alt text | Planned |
| 5 | Content moderation | Planned |
| 6 | Semantic search | Planned |
| 7 | Site chatbot | Planned |
| 8 | Accessibility checker | Planned |
