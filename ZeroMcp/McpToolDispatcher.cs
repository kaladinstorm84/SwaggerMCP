using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZeroMCP.Discovery;

namespace ZeroMCP.Dispatch;

/// <summary>
/// The result of an in-process MCP tool dispatch.
/// </summary>
public sealed class DispatchResult
{
    public bool IsSuccess { get; init; }
    public int StatusCode { get; init; }
    public string Content { get; init; } = string.Empty;
    public string ContentType { get; init; } = "application/json";

    public static DispatchResult Success(int statusCode, string content, string contentType = "application/json") =>
        new() { IsSuccess = true, StatusCode = statusCode, Content = content, ContentType = contentType };

    public static DispatchResult Failure(int statusCode, string error) =>
        new() { IsSuccess = false, StatusCode = statusCode, Content = error };
}

/// <summary>
/// Dispatches MCP tool calls directly in-process by constructing a synthetic HttpContext,
/// invoking the action through ASP.NET Core's full pipeline, and capturing the response.
/// 
/// This means all action filters, validation, and authorization attributes on the target
/// controller action execute normally — MCP is just another caller.
/// </summary>
public sealed class McpToolDispatcher
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SyntheticHttpContextFactory _contextFactory;
    private readonly IActionDescriptorCollectionProvider _actionDescriptorProvider;
    private readonly ILogger<McpToolDispatcher> _logger;

    public McpToolDispatcher(
        IServiceScopeFactory scopeFactory,
        SyntheticHttpContextFactory contextFactory,
        IActionDescriptorCollectionProvider actionDescriptorProvider,
        ILogger<McpToolDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _contextFactory = contextFactory;
        _actionDescriptorProvider = actionDescriptorProvider;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches the given tool with the provided JSON arguments.
    /// Returns the serialized response from the action.
    /// </summary>
    /// <param name="sourceContext">Optional MCP request context; when set, configured headers (e.g. Authorization) are forwarded to the synthetic request.</param>
    public async Task<DispatchResult> DispatchAsync(
        McpToolDescriptor descriptor,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken = default,
        HttpContext? sourceContext = null)
    {
        _logger.LogDebug("Dispatching MCP tool '{ToolName}' with {ArgCount} argument(s)",
            descriptor.Name, args.Count);

        // Each dispatch gets its own DI scope, mirroring real request scoping
        await using var scope = _scopeFactory.CreateAsyncScope();

        HttpContext context;
        try
        {
            context = _contextFactory.Build(descriptor, args, scope, sourceContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build synthetic HttpContext for tool '{ToolName}'", descriptor.Name);
            return DispatchResult.Failure(400, $"Failed to bind arguments: {ex.Message}");
        }

        // Set endpoint when available so pipeline (e.g. CreatedAtAction, LinkGenerator) sees a matched endpoint
        if (descriptor.Endpoint is not null)
            context.SetEndpoint(descriptor.Endpoint);

        if (descriptor.Endpoint is not null && descriptor.ActionDescriptor is null)
        {
            return await DispatchMinimalEndpointAsync(descriptor, context);
        }

        if (descriptor.ActionDescriptor is null)
        {
            _logger.LogError("Tool '{ToolName}' has neither ActionDescriptor nor Endpoint", descriptor.Name);
            return DispatchResult.Failure(500, "Invalid tool descriptor");
        }

        // Controller action path: build ActionContext and invoke via IActionInvokerFactory
        var routeData = new RouteData(context.Request.RouteValues);
        var actionContext = new ActionContext(context, routeData, descriptor.ActionDescriptor!);

        // Get the invoker factory and create an invoker for this action
        var invokerFactory = scope.ServiceProvider.GetRequiredService<IActionInvokerFactory>();
        var invoker = invokerFactory.CreateInvoker(actionContext);

        if (invoker is null)
        {
            _logger.LogError("Could not create action invoker for tool '{ToolName}'", descriptor.Name);
            return DispatchResult.Failure(500, "Failed to create action invoker");
        }

        try
        {
            // Execute the action — this runs the full filter pipeline
            await invoker.InvokeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during dispatch of tool '{ToolName}'", descriptor.Name);
            return DispatchResult.Failure(500, $"Internal error: {ex.Message}");
        }

        return await ExtractResponseAsync(context, descriptor.Name);
    }

    private static bool IsAuthenticated(HttpContext context) =>
        context.User?.Identities?.Any(i => i.IsAuthenticated) == true;

    private static bool RequiresAuthentication(McpToolDescriptor descriptor)
    {
        // AllowAnonymous on either endpoint or action wins
        if (descriptor.Endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            return false;

        if (descriptor.ActionDescriptor?.EndpointMetadata?.OfType<IAllowAnonymous>().Any() == true)
            return false;

        // Any Authorize on endpoint or action means auth is required
        var hasAuthorizeEndpoint = descriptor.Endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null;
        var hasAuthorizeAction = descriptor.ActionDescriptor?.EndpointMetadata?.OfType<IAuthorizeData>().Any() == true;
        return hasAuthorizeEndpoint || hasAuthorizeAction;
    }

    private async Task<DispatchResult> DispatchMinimalEndpointAsync(McpToolDescriptor descriptor, HttpContext context)
    {
        context.SetEndpoint(descriptor.Endpoint!);
        try
        {
            await descriptor.Endpoint!.RequestDelegate!(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during dispatch of minimal tool '{ToolName}'", descriptor.Name);
            return DispatchResult.Failure(500, $"Internal error: {ex.Message}");
        }
        return await ExtractResponseAsync(context, descriptor.Name);
    }

    private async Task<DispatchResult> ExtractResponseAsync(HttpContext context, string toolName)
    {
        var statusCode = context.Response.StatusCode;

        // Rewind the response body stream
        if (context.Response.Body is MemoryStream ms)
        {
            ms.Position = 0;
            var responseBody = await new StreamReader(ms, Encoding.UTF8).ReadToEndAsync();
            var contentType = context.Response.ContentType ?? "application/json";

            _logger.LogDebug("Tool '{ToolName}' returned {StatusCode}, {Bytes} bytes",
                toolName, statusCode, ms.Length);

            if (statusCode >= 200 && statusCode < 300)
                return DispatchResult.Success(statusCode, responseBody, contentType);

            // Non-2xx: still return content but mark as failure
            return DispatchResult.Failure(statusCode, responseBody);
        }

        // No body
        if (statusCode >= 200 && statusCode < 300)
            return DispatchResult.Success(statusCode, string.Empty);

        return DispatchResult.Failure(statusCode, $"Request failed with status {statusCode}");
    }
}
