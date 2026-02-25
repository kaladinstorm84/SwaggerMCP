using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using SwaggerMcp.Attributes;

namespace SwaggerMcp.Discovery;

/// <summary>
/// Holds all the metadata needed to describe and invoke a single MCP tool.
/// Built at startup from the ApiDescription and McpToolAttribute, immutable at runtime.
/// </summary>
public sealed class McpToolDescriptor
{
    /// <summary>The tool name exposed to MCP clients.</summary>
    public string Name { get; init; } = default!;

    /// <summary>The tool description shown to the LLM.</summary>
    public string? Description { get; init; }

    /// <summary>Optional tags from McpToolAttribute.</summary>
    public string[]? Tags { get; init; }

    /// <summary>The full ApiDescription for this action (route, HTTP method, params). Null for minimal API endpoints.</summary>
    public ApiDescription? ApiDescription { get; init; }

    /// <summary>The controller action descriptor — used to build the invoker. Null for minimal API endpoints.</summary>
    public ControllerActionDescriptor? ActionDescriptor { get; init; }

    /// <summary>The endpoint for minimal API tools. When set, dispatch uses RequestDelegate instead of controller invoker.</summary>
    public Endpoint? Endpoint { get; init; }

    /// <summary>
    /// Parameters that come from the route template (e.g. /orders/{id}).
    /// </summary>
    public IReadOnlyList<McpParameterDescriptor> RouteParameters { get; init; } = [];

    /// <summary>
    /// Parameters that come from the query string.
    /// </summary>
    public IReadOnlyList<McpParameterDescriptor> QueryParameters { get; init; } = [];

    /// <summary>
    /// The body parameter, if any. Will be a complex type.
    /// </summary>
    public McpBodyDescriptor? Body { get; init; }

    /// <summary>
    /// The merged JSON Schema for all inputs (route + query + body flattened).
    /// Null if IncludeInputSchemas is false.
    /// </summary>
    public string? InputSchemaJson { get; set; }

    /// <summary>HTTP method (GET, POST, etc.)</summary>
    public string HttpMethod { get; init; } = default!;

    /// <summary>Relative URL template (e.g. "api/orders/{id}")</summary>
    public string RelativeUrl { get; init; } = default!;
}

public sealed class McpParameterDescriptor
{
    public string Name { get; init; } = default!;
    public Type ParameterType { get; init; } = default!;
    public bool IsRequired { get; init; }
    public string? Description { get; init; }
}

public sealed class McpBodyDescriptor
{
    public Type BodyType { get; init; } = default!;
    public string ParameterName { get; init; } = default!;
}
