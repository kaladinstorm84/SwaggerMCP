using System.Text.Json;
using Microsoft.Extensions.Logging;
//using ModelContextProtocol.Server;
using SwaggerMcp.Discovery;
using SwaggerMcp.Dispatch;

namespace SwaggerMcp.Transport;

/// <summary>
/// Handles MCP tool listing and invocation by bridging the MCP SDK
/// to our internal discovery and dispatch infrastructure.
/// </summary>
internal sealed class McpSwaggerToolHandler
{
    private readonly McpToolDiscoveryService _discovery;
    private readonly McpToolDispatcher _dispatcher;
    private readonly ILogger<McpSwaggerToolHandler> _logger;

    public McpSwaggerToolHandler(
        McpToolDiscoveryService discovery,
        McpToolDispatcher dispatcher,
        ILogger<McpSwaggerToolHandler> logger)
    {
        _discovery = discovery;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <summary>
    /// Returns all registered MCP tools in the format the SDK expects.
    /// Called during the MCP tools/list request.
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
    /// Handles a tools/call request from the MCP client.
    /// </summary>
    /// <param name="sourceContext">Optional HTTP context of the MCP request; when set, configured headers (e.g. Authorization) are forwarded to the dispatched action.</param>
    public async Task<McpToolResult> HandleCallAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonElement> args,
        CancellationToken cancellationToken,
        HttpContext? sourceContext = null)
    {
        var descriptor = _discovery.GetTool(toolName);

        if (descriptor is null)
        {
            _logger.LogWarning("MCP client requested unknown tool '{ToolName}'", toolName);
            return McpToolResult.Error($"Unknown tool: {toolName}");
        }

        var result = await _dispatcher.DispatchAsync(descriptor, args, cancellationToken, sourceContext);

        if (result.IsSuccess)
        {
            return McpToolResult.Success(result.Content, result.ContentType);
        }

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
