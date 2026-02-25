namespace SwaggerMcp.Options;

/// <summary>
/// Configuration options for the SwaggerMcp middleware.
/// </summary>
public sealed class SwaggerMcpOptions
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
    /// Optional predicate to further filter which [McpTool]-tagged actions are exposed.
    /// Useful for environment-specific exclusions.
    /// </summary>
    public Func<string, bool>? ToolFilter { get; set; }

    /// <summary>
    /// Header names to forward from the incoming MCP request into the synthetic HttpContext
    /// when dispatching tool calls. Enables the dispatched action to see the same auth (e.g. Bearer token).
    /// Default is ["Authorization"]. Set to empty or null to disable forwarding.
    /// </summary>
    public IReadOnlyList<string>? ForwardHeaders { get; set; } = ["Authorization"];
}
