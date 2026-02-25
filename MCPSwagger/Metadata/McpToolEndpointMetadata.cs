using Microsoft.AspNetCore.Routing;

namespace SwaggerMcp.Metadata;

/// <summary>
/// Metadata attached to minimal API endpoints to expose them as MCP tools.
/// Use the extension method <see cref="McpToolEndpointExtensions"/>.<c>WithMcpTool</c> to add this to an endpoint.
/// </summary>
public sealed class McpToolEndpointMetadata
{
    public string Name { get; }
    public string? Description { get; }
    public string[]? Tags { get; }

    public McpToolEndpointMetadata(string name, string? description = null, string[]? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
        Tags = tags;
    }
}
