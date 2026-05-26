using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace VejleKommune.Code.Features.AgentTools;

/// <summary>
/// Phase 8 – Agent Tool (MCP), L1 implementation.
/// Implements a minimal Model Context Protocol (MCP) server over HTTP.
/// Protocol: JSON-RPC 2.0, single endpoint, all methods dispatched here.
///
/// Supported methods:
///   initialize   → returns server info + tool capability declaration
///   tools/list   → returns the four Umbraco tool definitions
///   tools/call   → dispatches to UmbracoMcpTools and returns result
///
/// L1 scope: works as a harness (curl/MCP client) but has no backoffice UI
/// and no approval gate on write operations. L2 blockers are documented in
/// AI-Integration-Package-Design.md and ADR-0012.
/// </summary>
[ApiController]
[Route("umbraco/api/vejle/mcp")]
public sealed class AgentToolsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly UmbracoMcpTools _tools;

    public AgentToolsController(UmbracoMcpTools tools)
    {
        _tools = tools;
    }

    /// <summary>MCP JSON-RPC endpoint. All methods route through here.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [Produces("application/json")]
    public IActionResult Handle([FromBody] McpRequest request)
    {
        object result = request.Method switch
        {
            "initialize"   => HandleInitialize(),
            "tools/list"   => HandleToolsList(),
            "tools/call"   => HandleToolsCall(request.Params),
            _              => throw new McpMethodNotFoundException(request.Method),
        };

        return Ok(new McpResponse("2.0", request.Id, Result: result));
    }

    // ── Method handlers ───────────────────────────────────────────────────

    private static McpInitializeResult HandleInitialize() =>
        new(
            ProtocolVersion: "2024-11-05",
            ServerInfo: new McpServerInfo("umbraco-vejle-kommune", "1.0.0"),
            Capabilities: new McpCapabilities(Tools: new { listChanged = false }));

    private static McpToolsListResult HandleToolsList() =>
        new(UmbracoMcpTools.Definitions);

    private McpToolCallResult HandleToolsCall(JsonElement? paramsEl)
    {
        if (paramsEl is null)
            return new([new McpContent("text", "Missing params for tools/call.")], IsError: true);

        McpToolCallParams? call = paramsEl.Value.Deserialize<McpToolCallParams>(JsonOpts);
        if (call is null || string.IsNullOrWhiteSpace(call.Name))
            return new([new McpContent("text", "tools/call requires 'name'.")], IsError: true);

        return _tools.Dispatch(call.Name, call.Arguments);
    }
}

// ── Exception for unknown methods ─────────────────────────────────────────

[Serializable]
public sealed class McpMethodNotFoundException : Exception
{
    public McpMethodNotFoundException(string method)
        : base($"MCP method not found: {method}") { }
}
