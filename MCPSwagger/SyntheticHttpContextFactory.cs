using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using SwaggerMcp.Discovery;
using SwaggerMcp.Options;

namespace SwaggerMcp.Dispatch;

/// <summary>
/// Constructs synthetic HttpContext instances for in-process action dispatch.
/// Translates MCP JSON arguments into a realistic HttpContext that ASP.NET Core's
/// model binding pipeline can consume as if it were a real HTTP request.
/// </summary>
public sealed class SyntheticHttpContextFactory
{
    private readonly IHttpContextFactory _httpContextFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly SwaggerMcpOptions _options;

    public SyntheticHttpContextFactory(
        IHttpContextFactory httpContextFactory,
        IServiceProvider serviceProvider,
        IOptions<SwaggerMcpOptions> options)
    {
        _httpContextFactory = httpContextFactory;
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    /// <summary>
    /// Builds a synthetic HttpContext populated from the MCP tool arguments.
    /// Route values, query string, and body stream are all set appropriately.
    /// If sourceContext is provided and ForwardHeaders is configured, copies those headers.
    /// </summary>
    public HttpContext Build(
        McpToolDescriptor descriptor,
        IReadOnlyDictionary<string, JsonElement> args,
        IServiceScope scope,
        HttpContext? sourceContext = null)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
        features.Set<IServiceProvidersFeature>(new SyntheticRequestServicesFeature(scope.ServiceProvider));
        features.Set<IItemsFeature>(new ItemsFeature());

        var context = new DefaultHttpContext(features);
        context.RequestServices = scope.ServiceProvider;

        // Set HTTP method and path
        context.Request.Method = descriptor.HttpMethod.ToUpperInvariant();
        context.Request.PathBase = PathString.Empty;
        context.Request.Path = "/" + descriptor.RelativeUrl.TrimStart('/');
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");

        // Bind route values
        var routeValues = new RouteValueDictionary();
        foreach (var routeParam in descriptor.RouteParameters)
        {
            if (args.TryGetValue(routeParam.Name, out var value))
            {
                var stringValue = ExtractStringValue(value);
                routeValues[routeParam.Name] = stringValue;

                // Also rewrite the path with the actual value substituted
                context.Request.Path = new PathString(
                    context.Request.Path.Value!.Replace(
                        $"{{{routeParam.Name}}}",
                        Uri.EscapeDataString(stringValue),
                        StringComparison.OrdinalIgnoreCase));
            }
        }

        context.Request.RouteValues = routeValues;

        // Ambient controller/action so LinkGenerator and CreatedAtAction can resolve routes
        if (descriptor.ActionDescriptor is not null)
        {
            routeValues["controller"] = descriptor.ActionDescriptor.ControllerName;
            routeValues["action"] = descriptor.ActionDescriptor.ActionName;
        }

        // Set up routing feature so model binding can read route values
        context.Features.Set<IRoutingFeature>(new RoutingFeature
        {
            RouteData = new RouteData(routeValues)
        });

        // Bind query string
        var queryParts = new List<string>();
        foreach (var queryParam in descriptor.QueryParameters)
        {
            if (args.TryGetValue(queryParam.Name, out var value))
            {
                queryParts.Add($"{Uri.EscapeDataString(queryParam.Name)}={Uri.EscapeDataString(ExtractStringValue(value))}");
            }
        }

        if (queryParts.Count > 0)
        {
            context.Request.QueryString = new QueryString("?" + string.Join("&", queryParts));
        }

        // Bind body — collect all non-route, non-query args and serialize as JSON body
        if (descriptor.Body is not null)
        {
            var bodyArgs = new Dictionary<string, JsonElement>();

            // Collect body properties: all args that aren't route or query params
            var nonBodyParamNames = new HashSet<string>(
                descriptor.RouteParameters.Select(p => p.Name)
                    .Concat(descriptor.QueryParameters.Select(p => p.Name)),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (key, val) in args)
            {
                if (!nonBodyParamNames.Contains(key))
                    bodyArgs[key] = val;
            }

            var bodyJson = JsonSerializer.Serialize(bodyArgs, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
            context.Request.Body = new MemoryStream(bodyBytes);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bodyBytes.Length;
        }

        // Set response body to a writable MemoryStream so we can capture output
        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        // Forward headers from the MCP request (e.g. Authorization) so the dispatched action sees the same auth
        if (sourceContext is not null && _options.ForwardHeaders is { Count: > 0 })
        {
            var sourceHeaders = sourceContext.Request.Headers;
            var names = _options.ForwardHeaders;
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                if (sourceHeaders.TryGetValue(name, out var value))
                    context.Request.Headers[name] = value;
            }
        }

        return context;
    }

    private static string ExtractStringValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        _ => element.GetRawText()
    };
}

/// <summary>Minimal IServiceProvidersFeature for synthetic requests when RequestServicesFeature constructor is not compatible.</summary>
internal sealed class SyntheticRequestServicesFeature : IServiceProvidersFeature
{
    public SyntheticRequestServicesFeature(IServiceProvider requestServices) => RequestServices = requestServices;
    public IServiceProvider RequestServices { get; set; }
}

/// <summary>Minimal IRoutingFeature implementation for synthetic requests.</summary>
internal sealed class RoutingFeature : IRoutingFeature
{
    public RouteData? RouteData { get; set; }
}
