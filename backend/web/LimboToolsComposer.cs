using Umbraco.AI.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using VejleKommune.Code.Features.AgentTools;

namespace VejleKommune;

/// <summary>
/// Registers Limbo's custom Umbraco.AI agent tools.
///
/// Kept in the web project (not code) because Umbraco.AI.Extensions
/// (which provides builder.AITools()) is only referenced from web.csproj.
///
/// Current tools:
///   SetBlockListItemValueTool — block-item-level write access for Copilot (ADR-0013).
/// </summary>
public sealed class LimboToolsComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AITools().Add<SetBlockListItemValueTool>();
    }
}
