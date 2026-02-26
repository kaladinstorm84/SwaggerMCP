using Microsoft.AspNetCore.Routing;
using ZeroMCP.Metadata;

namespace ZeroMCP.Extensions;

/// <summary>
/// Extension methods for exposing minimal API endpoints as MCP tools.
/// </summary>
public static class McpToolEndpointExtensions
{
    /// <summary>
    /// Marks this endpoint as an MCP tool with the given name and optional description.
    /// The endpoint will appear in tools/list and can be invoked via tools/call.
    /// </summary>
    /// <param name="builder">The endpoint convention builder (e.g. from MapGet, MapPost).</param>
    /// <param name="name">Tool name in snake_case (e.g. "get_weather").</param>
    /// <param name="description">Optional description shown to the LLM.</param>
    /// <param name="tags">Optional tags for grouping.</param>
    /// <param name="roles">Optional role names; tool is only listed if the user is in at least one role.</param>
    /// <param name="policy">Optional authorization policy name; tool is only listed if the user satisfies the policy.</param>
    public static TBuilder WithMcpTool<TBuilder>(
        this TBuilder builder,
        string name,
        string? description = null,
        string[]? tags = null,
        string[]? roles = null,
        string? policy = null)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new McpToolEndpointMetadata(name, description, tags, roles, policy));
        });
        return builder;
    }
}
