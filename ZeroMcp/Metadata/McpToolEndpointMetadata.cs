using Microsoft.AspNetCore.Routing;

namespace ZeroMCP.Metadata;

/// <summary>
/// Metadata attached to minimal API endpoints to expose them as MCP tools.
/// Use the extension method <see cref="McpToolEndpointExtensions"/>.<c>WithMcpTool</c> to add this to an endpoint.
/// </summary>
public sealed class McpToolEndpointMetadata
{
    public string Name { get; }
    public string? Description { get; }
    public string[]? Tags { get; }
    public string[]? Roles { get; }
    public string? Policy { get; }

    public McpToolEndpointMetadata(string name, string? description = null, string[]? tags = null, string[]? roles = null, string? policy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
        Tags = tags;
        Roles = roles;
        Policy = policy;
    }
}
