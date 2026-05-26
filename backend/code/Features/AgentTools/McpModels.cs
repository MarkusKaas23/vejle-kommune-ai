using System.Text.Json.Serialization;

namespace VejleKommune.Code.Features.AgentTools;

// ── MCP JSON-RPC wire types ────────────────────────────────────────────────
// Minimal implementation of the Model Context Protocol (2024-11-05 spec).
// Only the subset required for tool calling is implemented.
// Reference: https://spec.modelcontextprotocol.io/specification/

public sealed record McpRequest(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")]      object? Id,
    [property: JsonPropertyName("method")]  string Method,
    [property: JsonPropertyName("params")]  System.Text.Json.JsonElement? Params);

public sealed record McpResponse(
    [property: JsonPropertyName("jsonrpc")] string JsonRpc,
    [property: JsonPropertyName("id")]      object? Id,
    [property: JsonPropertyName("result")]  object? Result = null,
    [property: JsonPropertyName("error")]   McpError? Error = null);

public sealed record McpError(
    [property: JsonPropertyName("code")]    int Code,
    [property: JsonPropertyName("message")] string Message);

// ── initialize ────────────────────────────────────────────────────────────

public sealed record McpServerInfo(
    [property: JsonPropertyName("name")]    string Name,
    [property: JsonPropertyName("version")] string Version);

public sealed record McpCapabilities(
    [property: JsonPropertyName("tools")]   object Tools);

public sealed record McpInitializeResult(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("serverInfo")]       McpServerInfo ServerInfo,
    [property: JsonPropertyName("capabilities")]     McpCapabilities Capabilities);

// ── tools/list ────────────────────────────────────────────────────────────

public sealed record McpToolsListResult(
    [property: JsonPropertyName("tools")] IReadOnlyList<McpTool> Tools);

public sealed record McpTool(
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("inputSchema")] object InputSchema);

// ── tools/call ────────────────────────────────────────────────────────────

public sealed record McpToolCallParams(
    [property: JsonPropertyName("name")]      string Name,
    [property: JsonPropertyName("arguments")] System.Text.Json.JsonElement Arguments);

public sealed record McpToolCallResult(
    [property: JsonPropertyName("content")]   IReadOnlyList<McpContent> Content,
    [property: JsonPropertyName("isError")]   bool IsError = false);

public sealed record McpContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);
