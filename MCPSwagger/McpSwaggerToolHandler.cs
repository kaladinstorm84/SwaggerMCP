using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZeroMCP.Discovery;
using ZeroMCP.Dispatch;
using ZeroMCP.Observability;
using ZeroMCP.Options;

namespace ZeroMCP.Transport;

/// <summary>
/// Handles MCP tool listing and invocation by bridging the MCP SDK
/// to our internal discovery and dispatch infrastructure.
/// </summary>
internal sealed class McpSwaggerToolHandler
{
    private readonly McpToolDiscoveryService _discovery;
    private readonly McpToolDispatcher _dispatcher;
    private readonly ZeroMCPOptions _options;
    private readonly IMcpMetricsSink _metricsSink;
    private readonly ILogger<McpSwaggerToolHandler> _logger;

    public McpSwaggerToolHandler(
        McpToolDiscoveryService discovery,
        McpToolDispatcher dispatcher,
        IOptions<ZeroMCPOptions> options,
        IMcpMetricsSink metricsSink,
        ILogger<McpSwaggerToolHandler> logger)
    {
        _discovery = discovery;
        _dispatcher = dispatcher;
        _options = options.Value;
        _metricsSink = metricsSink;
        _logger = logger;
    }

    /// <summary>
    /// Returns all registered MCP tools in the format the SDK expects (no per-request filtering).
    /// For role/policy/visibility filtering use <see cref="GetToolDefinitionsAsync"/> with an <see cref="HttpContext"/>.
    /// </summary>
    public IEnumerable<McpToolDefinition> GetToolDefinitions()
    {
        foreach (var descriptor in _discovery.GetTools())
        {
            yield return new McpToolDefinition
            {
                Name = descriptor.Name,
                Description = BuildDescription(descriptor),
                InputSchema = descriptor.InputSchemaJson is not null
                    ? JsonDocument.Parse(descriptor.InputSchemaJson).RootElement
                    : DefaultEmptySchema()
            };
        }
    }

    /// <summary>
    /// Returns MCP tools visible to the current request (filtered by roles, policy, and ToolVisibilityFilter).
    /// Called during the MCP tools/list request when governance is used.
    /// </summary>
    public async Task<IReadOnlyList<McpToolDefinition>> GetToolDefinitionsAsync(HttpContext? context, CancellationToken cancellationToken = default)
    {
        var list = new List<McpToolDefinition>();
        foreach (var descriptor in _discovery.GetTools())
        {
            if (context is not null && !await IsVisibleAsync(descriptor, context, cancellationToken).ConfigureAwait(false))
                continue;
            list.Add(new McpToolDefinition
            {
                Name = descriptor.Name,
                Description = BuildDescription(descriptor),
                InputSchema = descriptor.InputSchemaJson is not null
                    ? JsonDocument.Parse(descriptor.InputSchemaJson).RootElement
                    : DefaultEmptySchema()
            });
        }
        return list;
    }

    private async Task<bool> IsVisibleAsync(McpToolDescriptor descriptor, HttpContext context, CancellationToken cancellationToken)
    {
        if (descriptor.RequiredRoles is { Length: > 0 })
        {
            var inRole = false;
            foreach (var role in descriptor.RequiredRoles)
            {
                if (context.User.IsInRole(role))
                {
                    inRole = true;
                    break;
                }
            }
            if (!inRole)
            {
                _logger.LogDebug("Tool '{ToolName}' hidden from tools/list: user not in required roles", descriptor.Name);
                return false;
            }
        }

        if (!string.IsNullOrEmpty(descriptor.RequiredPolicy))
        {
            var authService = context.RequestServices.GetService<IAuthorizationService>();
            if (authService is null)
            {
                _logger.LogDebug("Tool '{ToolName}' hidden: RequiredPolicy set but IAuthorizationService not available", descriptor.Name);
                return false;
            }
            var result = await authService.AuthorizeAsync(context.User, null, descriptor.RequiredPolicy!).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                _logger.LogDebug("Tool '{ToolName}' hidden from tools/list: policy '{Policy}' not satisfied", descriptor.Name, descriptor.RequiredPolicy);
                return false;
            }
        }

        if (_options.ToolVisibilityFilter is not null && !_options.ToolVisibilityFilter(descriptor.Name, context))
        {
            _logger.LogDebug("Tool '{ToolName}' excluded by ToolVisibilityFilter", descriptor.Name);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Handles a tools/call request from the MCP client.
    /// </summary>
    /// <param name="sourceContext">Optional HTTP context of the MCP request; when set, configured headers (e.g. Authorization) are forwarded to the dispatched action.</param>
    public async Task<McpToolResult> HandleCallAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken,
        HttpContext? sourceContext = null)
    {
        var correlationId = sourceContext?.Items[McpHttpEndpointHandler.CorrelationIdItemKey] as string;
        var descriptor = _discovery.GetTool(toolName);

        if (descriptor is null)
        {
            _logger.LogWarning("MCP client requested unknown tool: ToolName={ToolName}, CorrelationId={CorrelationId}", toolName, correlationId ?? "");
            return McpToolResult.Error($"Unknown tool: {toolName}");
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await _dispatcher.DispatchAsync(descriptor, args, cancellationToken, sourceContext);
        stopwatch.Stop();

        var statusCode = result.StatusCode;
        var isError = !result.IsSuccess;
        var durationMs = stopwatch.Elapsed.TotalMilliseconds;

        _metricsSink.RecordToolInvocation(toolName, statusCode, isError, durationMs, correlationId);

        _logger.Log(isError ? LogLevel.Warning : LogLevel.Debug,
            "Tool invocation: ToolName={ToolName}, StatusCode={StatusCode}, IsError={IsError}, DurationMs={DurationMs}, CorrelationId={CorrelationId}",
            toolName, statusCode, isError, durationMs, correlationId ?? "");

        if (_options.EnableOpenTelemetryEnrichment && Activity.Current is { } activity)
        {
            activity.SetTag("mcp.tool", toolName);
            activity.SetTag("mcp.status_code", statusCode);
            activity.SetTag("mcp.is_error", isError);
            activity.SetTag("mcp.duration_ms", durationMs);
            if (!string.IsNullOrEmpty(correlationId))
                activity.SetTag("mcp.correlation_id", correlationId);
        }

        if (result.IsSuccess)
            return McpToolResult.Success(result.Content, result.ContentType);

        return McpToolResult.Error(
            $"Tool '{toolName}' failed with HTTP {result.StatusCode}: {result.Content}");
    }

    private static string BuildDescription(McpToolDescriptor descriptor)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrWhiteSpace(descriptor.Description))
            sb.Append(descriptor.Description);

        // Append HTTP method and route so the LLM has additional context
        sb.Append($" [{descriptor.HttpMethod} /{descriptor.RelativeUrl}]");

        if (descriptor.Tags is { Length: > 0 })
            sb.Append($" (tags: {string.Join(", ", descriptor.Tags)})");

        return sb.ToString().Trim();
    }

    private static JsonElement DefaultEmptySchema()
    {
        return JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
    }
}

/// <summary>Tool definition passed to the MCP SDK.</summary>
public sealed class McpToolDefinition
{
    public string Name { get; init; } = default!;
    public string Description { get; init; } = default!;
    public JsonElement InputSchema { get; init; }
}

/// <summary>Result returned from a tool invocation.</summary>
public sealed class McpToolResult
{
    public bool IsError { get; private init; }
    public string Content { get; private init; } = default!;
    public string ContentType { get; private init; } = "application/json";

    public static McpToolResult Success(string content, string contentType = "application/json") =>
        new() { IsError = false, Content = content, ContentType = contentType };

    public static McpToolResult Error(string message) =>
        new() { IsError = true, Content = message, ContentType = "text/plain" };
}
