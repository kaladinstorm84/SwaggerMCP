using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwaggerMcp.Transport;

namespace SwaggerMcp.Transport;

/// <summary>
/// Handles the streamable HTTP MCP transport protocol.
/// Implements the JSON-RPC 2.0 envelope that MCP uses over HTTP.
/// 
/// Supported methods:
///   initialize        — handshake, returns server capabilities
///   tools/list        — returns all registered tools
///   tools/call        — invokes a tool and returns its result
/// </summary>
internal sealed class McpHttpEndpointHandler
{
    private readonly McpSwaggerToolHandler _toolHandler;
    private readonly string _serverName;
    private readonly string _serverVersion;
    private readonly ILogger<McpHttpEndpointHandler> _logger;

    public McpHttpEndpointHandler(
        McpSwaggerToolHandler toolHandler,
        string serverName,
        string serverVersion,
        ILogger<McpHttpEndpointHandler> logger)
    {
        _toolHandler = toolHandler;
        _serverName = serverName;
        _serverVersion = serverVersion;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        // GET: return a short description so the URL isn't blank in a browser
        if (context.Request.Method == "GET")
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                protocol = "MCP",
                transport = "streamable HTTP",
                message = "Send POST requests with JSON-RPC 2.0 body. Methods: initialize, tools/list, tools/call.",
                server = _serverName,
                version = _serverVersion,
                example = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new { protocolVersion = "2024-11-05", clientInfo = new { name = "client", version = "1.0" } }
                }
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            return;
        }

        if (!context.Request.HasJsonContentType() && context.Request.Method == "POST")
        {
            context.Response.StatusCode = 415;
            await context.Response.WriteAsync("Content-Type must be application/json");
            return;
        }

        JsonDocument? requestDoc = null;

        try
        {
            requestDoc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        }
        catch
        {
            await WriteErrorAsync(context, null, -32700, "Parse error", null);
            return;
        }

        var root = requestDoc.RootElement;

        if (!root.TryGetProperty("jsonrpc", out var jsonrpc) || jsonrpc.GetString() != "2.0")
        {
            await WriteErrorAsync(context, null, -32600, "Invalid Request: missing jsonrpc 2.0", null);
            return;
        }

        root.TryGetProperty("id", out var id);
        var idValue = id.ValueKind == JsonValueKind.Undefined ? (object?)null : id.GetRawText();

        if (!root.TryGetProperty("method", out var methodEl))
        {
            await WriteErrorAsync(context, idValue, -32600, "Invalid Request: missing method", null);
            return;
        }

        var method = methodEl.GetString() ?? "";

        root.TryGetProperty("params", out var @params);

        _logger.LogDebug("MCP request: method={Method}", method);

        try
        {
            var responsePayload = method switch
            {
                "initialize" => HandleInitialize(@params),
                "notifications/initialized" => null, // fire and forget, no response
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolsCallAsync(@params, context),
                _ => throw new McpMethodNotFoundException($"Method not found: {method}")
            };

            if (responsePayload is null)
            {
                context.Response.StatusCode = 204;
                return;
            }

            await WriteResultAsync(context, idValue, responsePayload);
        }
        catch (McpMethodNotFoundException ex)
        {
            await WriteErrorAsync(context, idValue, -32601, ex.Message, null);
        }
        catch (McpInvalidParamsException ex)
        {
            await WriteErrorAsync(context, idValue, -32602, ex.Message, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing MCP method '{Method}'", method);
            await WriteErrorAsync(context, idValue, -32603, "Internal error", ex.Message);
        }
    }

    private object HandleInitialize(JsonElement @params)
    {
        return new
        {
            protocolVersion = "2024-11-05",
            serverInfo = new { name = _serverName, version = _serverVersion },
            capabilities = new
            {
                tools = new { listChanged = false }
            }
        };
    }

    private object HandleToolsList()
    {
        var tools = _toolHandler.GetToolDefinitions().Select(t => new
        {
            name = t.Name,
            description = t.Description,
            inputSchema = t.InputSchema
        });

        return new { tools };
    }

    private async Task<object> HandleToolsCallAsync(JsonElement @params, HttpContext httpContext)
    {
        if (@params.ValueKind == JsonValueKind.Undefined)
            throw new McpInvalidParamsException("tools/call requires params");

        if (!@params.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            throw new McpInvalidParamsException("tools/call requires params.name");

        var toolName = nameEl.GetString()!;

        // Extract arguments as a flat dictionary
        var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (@params.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
                args[prop.Name] = prop.Value;
        }

        var result = await _toolHandler.HandleCallAsync(toolName, args, httpContext.RequestAborted, httpContext);

        // MCP tool result format
        return new
        {
            content = new[]
            {
                new
                {
                    type = result.ContentType.StartsWith("application/json") ? "text" : "text",
                    text = result.Content
                }
            },
            isError = result.IsError
        };
    }

    private static async Task WriteResultAsync(HttpContext context, object? id, object result)
    {
        var response = new
        {
            jsonrpc = "2.0",
            id,
            result
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200;
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        }));
    }

    private static async Task WriteErrorAsync(HttpContext context, object? id, int code, string message, string? data)
    {
        var error = data is not null
            ? (object)new { code, message, data }
            : new { code, message };

        var response = new { jsonrpc = "2.0", id, error };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = 200; // JSON-RPC errors still return HTTP 200
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}

internal sealed class McpMethodNotFoundException : Exception
{
    public McpMethodNotFoundException(string message) : base(message)
    {
    }
}

internal sealed class McpInvalidParamsException : Exception
{
    public McpInvalidParamsException(string message) : base(message)
    {
    }
}
