using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroMCP.Attributes;
using ZeroMCP.Metadata;
using ZeroMCP.Options;
using ZeroMCP.Schema;

namespace ZeroMCP.Discovery;

/// <summary>
/// Discovers all [McpTool]-tagged controller actions at startup and builds the tool registry.
/// This runs once and the result is cached for the lifetime of the application.
/// </summary>
public sealed class McpToolDiscoveryService
{
    private readonly IApiDescriptionGroupCollectionProvider _apiDescriptionProvider;
    private readonly EndpointDataSource _endpointDataSource;
    private readonly McpSchemaBuilder _schemaBuilder;
    private readonly ZeroMCPOptions _options;
    private readonly ILogger<McpToolDiscoveryService> _logger;

    // Lazy-initialized registry
    private IReadOnlyDictionary<string, McpToolDescriptor>? _registry;
#if NET9_0_OR_GREATER
    private readonly Lock _lock = new();
#else
    private readonly object _lock = new();
#endif

    public McpToolDiscoveryService(
        IApiDescriptionGroupCollectionProvider apiDescriptionProvider,
        EndpointDataSource endpointDataSource,
        McpSchemaBuilder schemaBuilder,
        IOptions<ZeroMCPOptions> options,
        ILogger<McpToolDiscoveryService> logger)
    {
        _apiDescriptionProvider = apiDescriptionProvider;
        _endpointDataSource = endpointDataSource;
        _schemaBuilder = schemaBuilder;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Returns the full registry of discovered MCP tools.
    /// Built lazily on first access, then cached.
    /// </summary>
    public IReadOnlyDictionary<string, McpToolDescriptor> GetRegistry()
    {
        if (_registry is not null) return _registry;

        lock (_lock)
        {
            if (_registry is not null) return _registry;
            _registry = BuildRegistry();
        }

        return _registry;
    }

    /// <summary>Returns all discovered tool descriptors.</summary>
    public IEnumerable<McpToolDescriptor> GetTools() => GetRegistry().Values;

    /// <summary>Looks up a tool by name.</summary>
    public McpToolDescriptor? GetTool(string name)
    {
        GetRegistry().TryGetValue(name, out var descriptor);
        return descriptor;
    }

    private Dictionary<string, McpToolDescriptor> BuildRegistry()
    {
        var registry = new Dictionary<string, McpToolDescriptor>(StringComparer.OrdinalIgnoreCase);

        var allDescriptions = _apiDescriptionProvider.ApiDescriptionGroups.Items
            .SelectMany(g => g.Items);

        foreach (var apiDescription in allDescriptions)
        {
            // Must be a controller action
            if (apiDescription.ActionDescriptor is not ControllerActionDescriptor controllerDescriptor)
                continue;

            // Must have [McpTool]
            var mcpAttr = controllerDescriptor.MethodInfo
                .GetCustomAttributes(typeof(McpAttribute), inherit: false)
                .FirstOrDefault() as McpAttribute;

            if (mcpAttr is null)
                continue;

            // Apply optional filter
            if (_options.ToolFilter is not null && !_options.ToolFilter(mcpAttr.Name))
            {
                _logger.LogDebug("Tool '{ToolName}' excluded by ToolFilter", mcpAttr.Name);
                continue;
            }

            // Detect name collisions
            if (registry.ContainsKey(mcpAttr.Name))
            {
                _logger.LogWarning(
                    "Duplicate MCP tool name '{ToolName}' on {Controller}.{Action} — skipping. " +
                    "Each [McpTool] name must be unique.",
                    mcpAttr.Name,
                    controllerDescriptor.ControllerName,
                    controllerDescriptor.ActionName);
                continue;
            }

            var descriptor = BuildDescriptor(apiDescription, controllerDescriptor, mcpAttr);
            if (descriptor.Endpoint is null)
                _logger.LogWarning("No matching endpoint found for {Controller}.{Action}; CreatedAtAction/link generation may fail.",
                    controllerDescriptor.ControllerName, controllerDescriptor.ActionName);
            registry[descriptor.Name] = descriptor;

            _logger.LogDebug(
                "Registered MCP tool '{ToolName}' → {HttpMethod} {RelativeUrl}",
                descriptor.Name,
                descriptor.HttpMethod,
                descriptor.RelativeUrl);
        }

        // Discover minimal API endpoints with McpToolEndpointMetadata
        foreach (var endpoint in _endpointDataSource.Endpoints)
        {
            var mcpMeta = endpoint.Metadata.GetMetadata<McpToolEndpointMetadata>();
            if (mcpMeta is null) continue;

            if (_options.ToolFilter is not null && !_options.ToolFilter(mcpMeta.Name))
            {
                _logger.LogDebug("Tool '{ToolName}' excluded by ToolFilter", mcpMeta.Name);
                continue;
            }

            if (registry.ContainsKey(mcpMeta.Name))
            {
                _logger.LogWarning("Duplicate MCP tool name '{ToolName}' (minimal API) — skipping.", mcpMeta.Name);
                continue;
            }

            var minDescriptor = BuildMinimalApiDescriptor(endpoint, mcpMeta);
            registry[minDescriptor.Name] = minDescriptor;

            _logger.LogDebug(
                "Registered MCP tool '{ToolName}' (minimal) → {HttpMethod} {RelativeUrl}",
                minDescriptor.Name,
                minDescriptor.HttpMethod,
                minDescriptor.RelativeUrl);
        }

        _logger.LogInformation("ZeroMCP: discovered {Count} MCP tool(s)", registry.Count);
        return registry;
    }

    private McpToolDescriptor BuildMinimalApiDescriptor(Endpoint endpoint, McpToolEndpointMetadata meta)
    {
        var routeParams = new List<McpParameterDescriptor>();
        var httpMethod = "GET";
        var relativeUrl = "";

        if (endpoint is RouteEndpoint routeEndpoint)
        {
            var pattern = routeEndpoint.RoutePattern;
            relativeUrl = pattern.RawText?.TrimStart('/') ?? "";
            foreach (var param in pattern.Parameters)
            {
                routeParams.Add(new McpParameterDescriptor
                {
                    Name = param.Name ?? "",
                    ParameterType = typeof(string),
                    IsRequired = !param.IsOptional,
                    Description = null
                });
            }
        }

        var methodMeta = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        if (methodMeta?.HttpMethods is { Count: > 0 })
            httpMethod = methodMeta.HttpMethods.First();

        var descriptor = new McpToolDescriptor
        {
            Name = meta.Name,
            Description = meta.Description,
            Tags = meta.Tags,
            RequiredRoles = meta.Roles,
            RequiredPolicy = meta.Policy,
            ApiDescription = null,
            ActionDescriptor = null,
            Endpoint = endpoint,
            RouteParameters = routeParams,
            QueryParameters = [],
            Body = null,
            HttpMethod = httpMethod,
            RelativeUrl = relativeUrl
        };

        if (_options.IncludeInputSchemas)
            descriptor.InputSchemaJson = _schemaBuilder.BuildSchema(descriptor);

        return descriptor;
    }

    private McpToolDescriptor BuildDescriptor(
        ApiDescription apiDescription,
        ControllerActionDescriptor controllerDescriptor,
        McpAttribute mcpAttr)
    {
        var routeParams = new List<McpParameterDescriptor>();
        var queryParams = new List<McpParameterDescriptor>();
        McpBodyDescriptor? body = null;

        foreach (var param in apiDescription.ParameterDescriptions)
        {
            switch (param.Source.Id)
            {
                case "Path":
                    routeParams.Add(new McpParameterDescriptor
                    {
                        Name = param.Name,
                        ParameterType = param.Type ?? typeof(string),
                        IsRequired = param.IsRequired,
                        Description = param.ModelMetadata?.Description
                    });
                    break;

                case "Query":
                    queryParams.Add(new McpParameterDescriptor
                    {
                        Name = param.Name,
                        ParameterType = param.Type ?? typeof(string),
                        IsRequired = param.IsRequired,
                        Description = param.ModelMetadata?.Description
                    });
                    break;

                case "Body":
                    body = new McpBodyDescriptor
                    {
                        BodyType = param.Type ?? typeof(object),
                        ParameterName = param.Name
                    };
                    break;
            }
        }

        var descriptor = new McpToolDescriptor
        {
            Name = mcpAttr.Name,
            Description = !string.IsNullOrWhiteSpace(mcpAttr.Description)
                ? mcpAttr.Description
                : XmlDocHelper.GetMethodSummary(controllerDescriptor.MethodInfo),
            Tags = mcpAttr.Tags,
            RequiredRoles = mcpAttr.Roles,
            RequiredPolicy = mcpAttr.Policy,
            ApiDescription = apiDescription,
            ActionDescriptor = controllerDescriptor,
            Endpoint = FindEndpointForAction(controllerDescriptor),
            RouteParameters = routeParams,
            QueryParameters = queryParams,
            Body = body,
            HttpMethod = apiDescription.HttpMethod ?? "GET",
            RelativeUrl = apiDescription.RelativePath ?? string.Empty
        };

        // Build and attach JSON Schema
        if (_options.IncludeInputSchemas)
        {
            descriptor.InputSchemaJson = _schemaBuilder.BuildSchema(descriptor);
        }

        return descriptor;
    }

    /// <summary>
    /// Finds the RouteEndpoint that corresponds to the given controller action so we can set it on the synthetic request.
    /// This avoids 500s when the action (e.g. CreatedAtAction) uses endpoint-aware services like LinkGenerator.
    /// </summary>
    private Endpoint? FindEndpointForAction(ControllerActionDescriptor controllerDescriptor)
    {
        foreach (var endpoint in _endpointDataSource.Endpoints)
        {
            var actionMeta = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (actionMeta is null) continue;
            // Prefer Id match; fallback to controller+action in case descriptor instances differ
            if (string.Equals(actionMeta.Id, controllerDescriptor.Id, StringComparison.Ordinal))
                return endpoint;
            if (string.Equals(actionMeta.ControllerName, controllerDescriptor.ControllerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(actionMeta.ActionName, controllerDescriptor.ActionName, StringComparison.OrdinalIgnoreCase))
                return endpoint;
        }
        return null;
    }
}
