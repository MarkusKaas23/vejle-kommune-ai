using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace VejleKommune.Code.Features.AgentTools;

/// <summary>
/// Phase 8 – Agent Tool (MCP).
/// Registers UmbracoMcpTools as a scoped service (IContentService is scoped,
/// so the tools class must be scoped too).
/// </summary>
public sealed class AgentToolsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.Services.AddScoped<UmbracoMcpTools>();
    }
}
