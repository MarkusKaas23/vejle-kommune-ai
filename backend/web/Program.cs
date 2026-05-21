using uSync.BackOffice;

// Set DataDirectory so SQLite path resolves before Umbraco's first DB check fires.
AppDomain.CurrentDomain.SetData(
    "DataDirectory",
    Path.Combine(Directory.GetCurrentDirectory(), "umbraco", "Data"));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
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

await app.RunAsync();
