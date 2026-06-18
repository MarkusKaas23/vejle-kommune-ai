using uSync.BackOffice;
using Umbraco.AI.Extensions;          // AddUmbracoAI
using Umbraco.AI.Agent.Extensions;    // AddUmbracoAIAgent
using Umbraco.AI.Prompt.Extensions;   // AddUmbracoAIPrompt

// Set DataDirectory so SQLite path resolves before Umbraco's first DB check fires.
AppDomain.CurrentDomain.SetData(
    "DataDirectory",
    Path.Combine(Directory.GetCurrentDirectory(), "umbraco", "Data"));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .AddUmbracoAI()          // Registers the AI section, Core, Web, Persistence
    .AddUmbracoAIAgent()     // Registers Agent runtime + Copilot sidebar
    .AddUmbracoAIPrompt()    // Registers Prompt property-action dropdown
    .AdduSync()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();


app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

// Explicitly map all attribute-routed [ApiController] classes (e.g. ContentConverterController).
// Umbraco's built-in endpoint setup only maps backoffice/website routes; custom controllers
// that live outside those areas need an explicit MapControllers() call.
app.MapControllers();

await app.RunAsync();
