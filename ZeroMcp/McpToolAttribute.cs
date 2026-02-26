namespace ZeroMCP.Attributes;

/// <summary>
/// Marks an ASP.NET Core controller action as an MCP tool.
/// Only actions with this attribute will be exposed via the MCP endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class McpAttribute : Attribute
{
    /// <summary>
    /// The tool name exposed to the MCP client. Use snake_case by convention.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// A description of what this tool does. Shown to the LLM to help it decide when to use the tool.
    /// If not provided, falls back to XML doc comments on the action method.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional tags for grouping or filtering tools.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Optional role names. When set, the tool is only included in tools/list if the current user is in at least one of these roles.
    /// Requires authentication and authorization to be configured (e.g. AddAuthentication, AddAuthorization).
    /// </summary>
    public string[]? Roles { get; set; }

    /// <summary>
    /// Optional authorization policy name. When set, the tool is only included in tools/list if the current user satisfies this policy.
    /// Requires AddAuthorization() and a policy with the given name to be configured.
    /// </summary>
    public string? Policy { get; set; }

    /// <param name="name">The tool name in snake_case (e.g. "get_order", "create_customer")</param>
    public McpAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }
}
