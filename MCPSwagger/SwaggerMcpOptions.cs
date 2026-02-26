using Microsoft.AspNetCore.Http;

namespace ZeroMCP.Options;

/// <summary>
/// Configuration options for the ZeroMCP middleware.
/// </summary>
public sealed class ZeroMCPOptions
{
    /// <summary>
    /// The route prefix for the MCP endpoint. Default is "/mcp".
    /// </summary>
    public string RoutePrefix { get; set; } = "/mcp";

    /// <summary>
    /// The server name advertised during MCP handshake. Defaults to the entry assembly name.
    /// </summary>
    public string? ServerName { get; set; }

    /// <summary>
    /// The server version advertised during MCP handshake. Defaults to "1.0.0".
    /// </summary>
    public string ServerVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Whether to include JSON Schema definitions in tool input descriptions.
    /// Enables the LLM to understand parameter types and constraints. Default is true.
    /// </summary>
    public bool IncludeInputSchemas { get; set; } = true;

    /// <summary>
    /// Optional predicate to further filter which [McpTool]-tagged actions are exposed at discovery time (by name only).
    /// Useful for environment-specific exclusions (e.g. exclude admin tools in non-production).
    /// </summary>
    public Func<string, bool>? ToolFilter { get; set; }

    /// <summary>
    /// Optional predicate to filter which tools are returned in tools/list per request.
    /// Receives the tool name and the current HTTP context (e.g. to check user, headers, or environment).
    /// Return true to include the tool, false to hide it. When null, no per-request filter is applied
    /// (role/policy filters on descriptors still apply).
    /// </summary>
    public Func<string, HttpContext, bool>? ToolVisibilityFilter { get; set; }

    /// <summary>
    /// Header names to forward from the incoming MCP request into the synthetic HttpContext
    /// when dispatching tool calls. Enables the dispatched action to see the same auth (e.g. Bearer token).
    /// Default is ["Authorization"]. Set to empty or null to disable forwarding.
    /// </summary>
    public IReadOnlyList<string>? ForwardHeaders { get; set; } = ["Authorization"];

    /// <summary>
    /// Request header name used to read and propagate a correlation ID. If present, the same value is echoed in the response and in logs.
    /// If absent, a new GUID is generated. Default is "X-Correlation-ID". Set to null or empty to disable correlation ID handling.
    /// </summary>
    public string? CorrelationIdHeader { get; set; } = "X-Correlation-ID";

    /// <summary>
    /// When true, tags the current <see cref="System.Diagnostics.Activity"/> (if any) with MCP tool invocation details
    /// (mcp.tool, mcp.status_code, mcp.is_error, mcp.duration_ms). Use with OpenTelemetry or similar. Default is false.
    /// </summary>
    public bool EnableOpenTelemetryEnrichment { get; set; }
}
