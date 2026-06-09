using System.Text.Json;
using Umbraco.AI.Agent.Core.Agents;
using Umbraco.AI.Core.Connections;
using Umbraco.AI.Core.Contexts;
using Umbraco.AI.Core.Guardrails;
using Umbraco.AI.Core.Models;
using Umbraco.AI.Core.Profiles;
using Umbraco.AI.Core.Settings;
using Umbraco.AI.Core.Tools.Scopes;
using Umbraco.AI.OpenAI;
using Umbraco.AI.Prompt.Core.Prompts;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace VejleKommune;

// ─────────────────────────────────────────────────────────────────────────────
// Composer — registers the notification handler so it runs on startup
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Registers the Vejle AI seed data handler with Umbraco's notification pipeline.</summary>
public sealed class VejleAiSeedComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) =>
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, VejleAiSeedDataHandler>();
}

// ─────────────────────────────────────────────────────────────────────────────
// Handler — seeds all Umbraco.AI configuration on first startup
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Seeds Umbraco.AI with Vejle Kommune–specific configuration on first startup:
/// <list type="bullet">
///   <item>A Google/Gemini <see cref="AIConnection"/> (API key from user secrets via <c>$</c> prefix)</item>
///   <item>A Danish municipal brand-voice <see cref="AIContext"/></item>
///   <item>GDPR and borgersprog (citizen-language) <see cref="AIGuardrail"/>s</item>
///   <item>A default <see cref="AIProfile"/> (chat, gemini-2.5-flash, temperature 0.4)</item>
///   <item>Four <see cref="AIPrompt"/> templates: SEO, resumé, borgernær omskrivning, overskrift</item>
///   <item>Three Copilot <see cref="AIAgent"/>s: indholds-, medie- og kommunal rådgiver</item>
/// </list>
/// Idempotent: if the Gemini connection already exists the handler exits immediately.
/// </summary>
public sealed class VejleAiSeedDataHandler(
    IAIConnectionService connectionService,
    IAIProfileService profileService,
    IAIContextService contextService,
    IAIGuardrailService guardrailService,
    IAISettingsService settingsService,
    IAIPromptService promptService,
    IAIAgentService agentService,
    AIToolScopeCollection toolScopes,
    ILogger<VejleAiSeedDataHandler> logger)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private const string ConnectionAlias = "gemini-vejle";

    public async Task HandleAsync(
        UmbracoApplicationStartedNotification notification,
        CancellationToken ct)
    {
        // Idempotency: skip completely if already seeded.
        var existing = await connectionService.GetConnectionByAliasAsync(ConnectionAlias, ct);
        if (existing is not null)
        {
            logger.LogDebug("[VejleAI Seed] Already seeded — skipping.");
            return;
        }

        logger.LogInformation("[VejleAI Seed] First startup — seeding Vejle Kommune AI configuration…");

        try
        {
            await SeedAsync(ct);
            logger.LogInformation("[VejleAI Seed] Seed completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[VejleAI Seed] Seed failed: {Message}", ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private async Task SeedAsync(CancellationToken ct)
    {
        // ── 1. Context ────────────────────────────────────────────────────────
        // Defines Vejle Kommune's tone-of-voice. Injected into every AI call so
        // editors never have to repeat brand guidelines in their own prompts.

        var context = await contextService.SaveContextAsync(new AIContext
        {
            Alias = "vejle-kommunestil",
            Name = "Vejle Kommune – Kommunikationsstil",
            Resources =
            [
                new AIContextResource
                {
                    ResourceTypeId = "brand-voice",
                    Name = "Kommunikationspolitik",
                    Description = "Vejle Kommunes tone of voice, sprogpolitik og skrivestil",
                    SortOrder = 0,
                    Settings = new
                    {
                        ToneDescription =
                            "Vi kommunikerer borgernært, klart og direkte. " +
                            "Brug aktive verber og undgå bureaukratisk sprog. " +
                            "Tiltale borgeren som 'du'. " +
                            "Hold sætninger korte og konkrete.",

                        TargetAudience =
                            "Borgere i Vejle Kommune i alle aldre og med forskellig baggrund. " +
                            "Skriv så alle kan forstå det — også dem uden uddannelse eller med " +
                            "dansk som andetsprog.",

                        StyleGuidelines =
                            "Brug nutidsform. Start med det vigtigste. " +
                            "Undgå nominalisering ('behandling af ansøgning' → 'behandler ansøgningen'). " +
                            "Brug punktlister til tre eller flere elementer. " +
                            "Skriv på et letlæseligt niveau (B1). " +
                            "Brug 'du' og 'vi' frem for passiv og upersonlig form.",

                        AvoidPatterns =
                            "Bureaukratiske vendinger: i henhold til, for så vidt angår, pågældende, " +
                            "foranstaltet, jf., iht. " +
                            "Lange sammensatte substantiver. " +
                            "Fagtermer uden forklaring. " +
                            "Passiv, når aktiv er mulig. " +
                            "Upersonlig form: 'det bemærkes', 'det skal nævnes'."
                    },
                    InjectionMode = AIContextResourceInjectionMode.Always
                }
            ]
        }, ct);

        // ── 2. Connection ─────────────────────────────────────────────────────
        // The $ prefix tells Umbraco.AI to resolve the value from IConfiguration.
        // The actual API key lives in user secrets:
        //   dotnet user-secrets set "VejleKommune:Ai:Gemini:ApiKey" "YOUR-KEY"

        var connection = await connectionService.SaveConnectionAsync(new AIConnection
        {
            Alias = ConnectionAlias,
            Name = "Google Gemini (Vejle)",
            ProviderId = "openai",
            Settings = new OpenAIProviderSettings
            {
                ApiKey = "$VejleKommune:Ai:Gemini:ApiKey",
                Endpoint = "https://generativelanguage.googleapis.com/v1beta/openai/",
            },
            IsActive = true
        }, ct);

        // ── 3. Guardrails ─────────────────────────────────────────────────────

        // 3a. Borgersprog — warns when AI output contains bureaucratic language
        var borgersprogGuardrail = await guardrailService.SaveGuardrailAsync(new AIGuardrail
        {
            Alias = "borgersprog",
            Name = "Borgersprog – Kommunikationskvalitet",
            Rules =
            [
                new AIGuardrailRule
                {
                    EvaluatorId = "llm-judge",
                    Name = "Tjek for bureaukratisk sprog",
                    Phase = AIGuardrailPhase.PostGenerate,
                    Action = AIGuardrailAction.Warn,
                    SortOrder = 0,
                    Config = ToJsonElement(new
                    {
                        evaluationCriteria =
                            "Evaluer om teksten indeholder bureaukratisk sprog, unødigt lange sætninger, " +
                            "fagtermer uden forklaring, passiv konstruktion eller tung nominalisering. " +
                            "Advar, hvis mere end 20% af sætningerne er svære at forstå for en gennemsnitlig borger.",
                        safetyThreshold = 0.75
                    })
                }
            ]
        }, ct);

        // 3b. GDPR — automatically redacts Danish CPR numbers, email and phone
        var gdprGuardrail = await guardrailService.SaveGuardrailAsync(new AIGuardrail
        {
            Alias = "gdpr-beskyttelse",
            Name = "GDPR – Personoplysningsbeskyttelse",
            Rules =
            [
                new AIGuardrailRule
                {
                    EvaluatorId = "regex",
                    Name = "Slet CPR-numre",
                    Phase = AIGuardrailPhase.PostGenerate,
                    Action = AIGuardrailAction.Redact,
                    SortOrder = 0,
                    Config = ToJsonElement(new
                    {
                        // Danish CPR: DDMMYY-XXXX or DDMMYYXXXX
                        pattern    = @"\b\d{6}[-–]?\d{4}\b",
                        ignoreCase = false,
                        multiline  = false
                    })
                },
                new AIGuardrailRule
                {
                    EvaluatorId = "regex",
                    Name = "Slet e-mailadresser",
                    Phase = AIGuardrailPhase.PostGenerate,
                    Action = AIGuardrailAction.Redact,
                    SortOrder = 1,
                    Config = ToJsonElement(new
                    {
                        pattern    = @"[a-zA-Z0-9.\_%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}",
                        ignoreCase = true,
                        multiline  = false
                    })
                },
                new AIGuardrailRule
                {
                    EvaluatorId = "regex",
                    Name = "Slet telefonnumre",
                    Phase = AIGuardrailPhase.PostGenerate,
                    Action = AIGuardrailAction.Redact,
                    SortOrder = 2,
                    Config = ToJsonElement(new
                    {
                        // Danish landline / mobile: +45 xx xx xx xx or 8 digits with optional spaces
                        pattern    = @"(\+45[\s\-]?)?\b\d{2}[\s\-]?\d{2}[\s\-]?\d{2}[\s\-]?\d{2}\b",
                        ignoreCase = false,
                        multiline  = false
                    })
                }
            ]
        }, ct);

        // ── 4. Default Chat Profile ───────────────────────────────────────────
        var profile = await profileService.SaveProfileAsync(new AIProfile
        {
            Alias = "vejle-chat",
            Name = "Vejle Default Chat",
            Capability = AICapability.Chat,
            ConnectionId = connection.Id,
            Model = new AIModelRef("openai", "gemini-2.5-flash"),
            Settings = new AIChatProfileSettings
            {
                Temperature = 0.4f,          // balanced: not too creative, not too rigid
                ContextIds  = [context.Id],  // always injects the municipality brand voice
                GuardrailIds =
                [
                    borgersprogGuardrail.Id,
                    gdprGuardrail.Id
                ]
            }
        }, ct);

        // Mark as the default so IAIChatService.GetChatResponseAsync() without a profileId works
        var aiSettings = await settingsService.GetSettingsAsync(ct);
        aiSettings.DefaultChatProfileId = profile.Id;
        await settingsService.SaveSettingsAsync(aiSettings, ct);

        // ── 5. Prompts ────────────────────────────────────────────────────────
        // These appear as a drop-down next to text areas and text boxes in the
        // backoffice once Umbraco.AI.Prompt is installed.

        // Short text fields only (SEO fields, headline)
        var shortTextEditors = new[]
        {
            "Umb.PropertyEditorUi.TextArea",
            "Umb.PropertyEditorUi.TextBox",
        };

        // All text fields including rich text body
        var allTextEditors = new[]
        {
            "Umb.PropertyEditorUi.TextArea",
            "Umb.PropertyEditorUi.TextBox",
            "Umb.PropertyEditorUi.Tiptap",   // Body / RichText fields (Umbraco 17)
        };

        // 5a. SEO description — reads the whole content node (IncludeEntityContext=true)
        //     to generate a relevant 150–160 char Danish meta description
        await promptService.SavePromptAsync(new AIPrompt
        {
            Alias       = "seo-beskrivelse",
            Name        = "SEO-beskrivelse",
            Description = "Generér en SEO-optimeret metabeskrivelse på dansk (150–160 tegn)",
            Instructions =
                "Skriv en SEO-optimeret metabeskrivelse på dansk til denne side. " +
                "Beskrivelsen skal være 150–160 tegn, indeholde relevante søgeord naturligt " +
                "og opfordre borgeren til at klikke. " +
                "Brug borgernært sprog uden fagtermer. Returner kun beskrivelsen.",
            ProfileId          = profile.Id,
            IsActive           = true,
            IncludeEntityContext = true,
            OptionCount        = 1,
            Scope = new AIPromptScope
            {
                AllowRules = [new AIPromptScopeRule { PropertyEditorUiAliases = shortTextEditors }]
            }
        }, ct);

        // 5b. Summarise — condenses selected text to one clear Danish paragraph
        await promptService.SavePromptAsync(new AIPrompt
        {
            Alias       = "resumér-indhold",
            Name        = "Resumér indhold",
            Description = "Kondensér den markerede tekst til ét kort, klart afsnit",
            Instructions =
                "Resumér følgende tekst i ét kort, klart afsnit på dansk, " +
                "der fanger de vigtigste pointer:\n\n{{currentValue}}\n\n" +
                "Returner kun resuméet.",
            ProfileId            = profile.Id,
            IsActive             = true,
            IncludeEntityContext = false,
            OptionCount          = 3,
            Scope = new AIPromptScope() // empty = appears on all text-based editors
        }, ct);

        // 5c. Borgernær omskrivning — rewrites bureaucratic language to plain Danish
        await promptService.SavePromptAsync(new AIPrompt
        {
            Alias       = "borgernær-omskrivning",
            Name        = "Gør borgernært",
            Description = "Omskriv teksten til klart, enkelt borgersprog",
            Instructions =
                "Omskriv følgende tekst til klart, enkelt borgersprog på dansk. " +
                "Undgå bureaukratiske vendinger og nominalisering. " +
                "Brug aktive verber og 'du'-tiltale:\n\n{{currentValue}}\n\n" +
                "Returner kun den omskrevne tekst.",
            ProfileId            = profile.Id,
            IsActive             = true,
            IncludeEntityContext = false,
            OptionCount          = 1,
            Scope = new AIPromptScope() // empty = appears on all text-based editors
        }, ct);

        // 5d. Headline suggestion — proposes concise, action-oriented Danish headings
        await promptService.SavePromptAsync(new AIPrompt
        {
            Alias       = "foreslå-overskrift",
            Name        = "Foreslå overskrift",
            Description = "Foreslå en klar, handlingsorienteret overskrift (max 60 tegn)",
            Instructions =
                "Foreslå en kort, klar og handlingsorienteret overskrift på dansk til denne tekst. " +
                "Max 60 tegn. Fortæl tydeligt borgeren, hvad siden handler om:\n\n{{currentValue}}\n\n" +
                "Returner kun overskriften.",
            ProfileId            = profile.Id,
            IsActive             = true,
            IncludeEntityContext = false,
            OptionCount          = 3,
            Scope = new AIPromptScope
            {
                AllowRules = [new AIPromptScopeRule { PropertyEditorUiAliases = shortTextEditors }]
            }
        }, ct);

        // ── 6. Copilot Agents ─────────────────────────────────────────────────
        // These appear in the Copilot chat sidebar. Each agent is scoped to the
        // relevant backoffice section and pre-loaded with the municipality context.

        var allToolScopeIds = toolScopes.Select(x => x.Id).ToArray();

        // 6a. Content assistant — general-purpose writing helper for editors
        await agentService.SaveAgentAsync(new AIAgent
        {
            Alias       = "indholdsassistent",
            Name        = "Indholdsassistent",
            Description = "Hjælper med at skrive og redigere indhold til Vejle Kommunes website",
            ProfileId   = profile.Id,
            SurfaceIds  = ["copilot"],
            Scope = new AIAgentScope
            {
                AllowRules = [new AIAgentScopeRule { Sections = ["content"] }]
            },
            Config = new AIStandardAgentConfig
            {
                ContextIds          = [context.Id],
                Instructions        =
                    "Du er en indholdsassistent for Vejle Kommune. " +
                    "Hjælp redaktørerne med at skrive og redigere tekster, der er klare, borgernære " +
                    "og i overensstemmelse med kommunens kommunikationspolitik. " +
                    "Brug altid 'du'-tiltale og aktive verber. Skriv på dansk.\n\n" +
                    "Backoffice-links: Når du refererer til et indholdsnode i backoffice, " +
                    "indsæt altid et direkte link i Umbraco 17-formatet: " +
                    "https://localhost:44337/umbraco/section/content/workspace/document/edit/{nodeKey}/da-DK/ " +
                    "— brug den nodeKey (GUID) du fik fra get_umbraco_content. " +
                    "Brug IKKE det gamle format med #/content/content/edit/.\n\n" +
                    "Sidestruktur: Indhold på en side er organiseret i felter som 'Modules' (inline blokke) " +
                    "og 'Referenced Accordions' (genbrugte globale elementer). " +
                    "Beskriv altid hvilken felt du arbejder med, og advar redaktøren " +
                    "hvis en ændring i et globalt element vil påvirke flere sider.",
                AllowedToolScopeIds = allToolScopeIds
            },
            IsActive = true
        }, ct);

        // 6b. Media assistant — writes alt text and captions for accessibility
        await agentService.SaveAgentAsync(new AIAgent
        {
            Alias       = "medie-assistent",
            Name        = "Medie-assistent",
            Description = "Hjælper med alt-tekster, billedtekster og mediebeskrivelser",
            ProfileId   = profile.Id,
            SurfaceIds  = ["copilot"],
            Scope = new AIAgentScope
            {
                AllowRules = [new AIAgentScopeRule { Sections = ["media"] }]
            },
            Config = new AIStandardAgentConfig
            {
                ContextIds          = [context.Id],
                Instructions        =
                    "Du er en medie-assistent for Vejle Kommune. " +
                    "Hjælp redaktørerne med at skrive gode alt-tekster (beskriv visuelt indhold præcist " +
                    "for blinde og svagtseende), billedtekster og mediebeskrivelser. " +
                    "Fokusér på tilgængelighed og klarhed. Skriv på dansk.",
                AllowedToolScopeIds = allToolScopeIds
            },
            IsActive = true
        }, ct);

        // 6c. Municipal advisor — helps editors explain services, processes and self-service
        await agentService.SaveAgentAsync(new AIAgent
        {
            Alias       = "kommunal-rådgiver",
            Name        = "Kommunal Rådgiver",
            Description = "Vejleder om kommunale services, processer og selvbetjeningsløsninger",
            ProfileId   = profile.Id,
            SurfaceIds  = ["copilot"],
            Scope = new AIAgentScope
            {
                AllowRules = [new AIAgentScopeRule { Sections = ["content"] }]
            },
            Config = new AIStandardAgentConfig
            {
                ContextIds          = [context.Id],
                Instructions        =
                    "Du er en kommunal rådgiver for Vejle Kommune. " +
                    "Hjælp redaktørerne med at beskrive kommunale services, ansøgningsprocesser, " +
                    "selvbetjeningsløsninger og regler på en klar og borgernær måde. " +
                    "Brug aktive verber og 'du'-tiltale. " +
                    "Henvis altid til professionel juridisk eller faglig rådgivning ved tvivlsspørgsmål. " +
                    "Skriv på dansk.",
                AllowedToolScopeIds = allToolScopeIds
            },
            IsActive = true
        }, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static JsonElement ToJsonElement(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}
