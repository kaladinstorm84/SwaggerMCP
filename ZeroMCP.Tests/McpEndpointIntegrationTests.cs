using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using ZeroMCP;
using Xunit;

namespace ZeroMCP.Tests;

public sealed class McpEndpointIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public McpEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = McpProtocolConstants.ProtocolVersion, clientInfo = new { name = "test", version = "1.0" } }
        });

        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["protocolVersion"]!.GetValue<string>().Should().Be(McpProtocolConstants.ProtocolVersion);
        result["serverInfo"]!.AsObject()["name"]!.GetValue<string>().Should().Be("Orders API");
    }

    // --- Production Hardening: compatibility tests (Phase 1) ---

    [Fact]
    public async Task Compatibility_ProtocolVersion_IsLocked()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 100,
            method = "initialize",
            @params = new { protocolVersion = McpProtocolConstants.ProtocolVersion, clientInfo = new { name = "compat", version = "1.0" } }
        });
        var result = response["result"]!.AsObject();
        result["protocolVersion"]!.GetValue<string>().Should().Be(McpProtocolConstants.ProtocolVersion, "MCP protocol version must be locked for production");
    }

    [Fact]
    public async Task Compatibility_Request_WithoutMethod_ReturnsInvalidRequest()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 101 });
        response.Should().HaveProperty("error");
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32600);
        response["error"]!.AsObject()["message"]!.GetValue<string>().Should().Contain("method");
    }

    [Fact]
    public async Task Compatibility_ToolsList_EachToolHasRequiredFields()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 102, method = "tools/list" });
        response.Should().HaveProperty("result");
        var tools = response["result"]!.AsObject()["tools"]!.AsArray();
        tools.Should().NotBeEmpty();
        foreach (var toolNode in tools)
        {
            var tool = toolNode!.AsObject();
            tool.Should().HaveProperty("name");
            tool.Should().HaveProperty("description");
            tool.Should().HaveProperty("inputSchema");
            tool["name"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task Compatibility_ErrorResponse_HasCodeAndMessage()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 103, method = "unknown/method" });
        response.Should().HaveProperty("error");
        var error = response["error"]!.AsObject();
        error["code"]!.GetValue<int>().Should().Be(-32601);
        error["message"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ToolsList_ReturnsTaggedToolsOnly()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        });

        response.Should().HaveProperty("result");
        var tools = response["result"]!.AsObject()["tools"]!.AsArray();
        var toolNames = tools.Select(t => t!.AsObject()["name"]!.GetValue<string>()).ToList();

        toolNames.Should().Contain("get_order");
        toolNames.Should().Contain("list_orders");
        toolNames.Should().Contain("create_order");
        toolNames.Should().Contain("update_order_status");
        toolNames.Should().Contain("get_secure_order");
        toolNames.Should().Contain("health_check");
        toolNames.Should().NotContain("delete_order");
        // admin_health has RequiredRoles = ["Admin"]; without auth it is hidden
        toolNames.Should().NotContain("admin_health");
    }

    // --- Governance & Tool Control (Phase 1) ---

    [Fact]
    public async Task Governance_ToolsList_WithoutAuth_ExcludesRoleRequiredTool()
    {
        var response = await PostMcpAsync(new { jsonrpc = "2.0", id = 200, method = "tools/list" });
        response.Should().HaveProperty("result");
        var toolNames = response["result"]!.AsObject()["tools"]!.AsArray()
            .Select(t => t!.AsObject()["name"]!.GetValue<string>())
            .ToList();
        toolNames.Should().NotContain("admin_health", "admin_health has Roles = [Admin] and request has no auth");
    }

    [Fact]
    public async Task Governance_ToolsList_WithAdminKey_IncludesRoleRequiredTool()
    {
        var response = await PostMcpAsync(
            new { jsonrpc = "2.0", id = 201, method = "tools/list" },
            new Dictionary<string, string> { ["X-Api-Key"] = "admin-key" });
        response.Should().HaveProperty("result");
        var toolNames = response["result"]!.AsObject()["tools"]!.AsArray()
            .Select(t => t!.AsObject()["name"]!.GetValue<string>())
            .ToList();
        toolNames.Should().Contain("admin_health", "admin-key adds Admin role so admin_health is visible");
    }

    // --- Observability (Phase 1) ---

    [Fact]
    public async Task Observability_CorrelationId_EchoedInResponse()
    {
        const string correlationId = "test-correlation-123";
        var body = new { jsonrpc = "2.0", id = 300, method = "tools/list" };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        var httpResponse = await _client.SendAsync(request);
        await httpResponse.Content.ReadAsStringAsync(); // ensure response is fully written so OnStarting has run
        httpResponse.Headers.TryGetValues("X-Correlation-ID", out var values).Should().BeTrue();
        values!.Single().Should().Be(correlationId);
    }


    [Fact]
    public async Task ToolsList_ReturnsExpectedInputSchemaShapes()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 20,
            method = "tools/list"
        });

        var toolsByName = response["result"]!.AsObject()["tools"]!.AsArray()
            .Select(tool => tool!.AsObject())
            .ToDictionary(
                tool => tool["name"]!.GetValue<string>(),
                tool => tool["inputSchema"]!.AsObject(),
                StringComparer.OrdinalIgnoreCase);

        var createOrderSchema = toolsByName["create_order"];
        createOrderSchema["type"]!.GetValue<string>().Should().Be("object");
        var createOrderProperties = createOrderSchema["properties"]!.AsObject();
        createOrderProperties["customerName"]!.AsObject()["type"]!.GetValue<string>().Should().Be("string");
        createOrderProperties["product"]!.AsObject()["type"]!.GetValue<string>().Should().Be("string");
        createOrderProperties["quantity"]!.AsObject()["type"]!.GetValue<string>().Should().Be("integer");
        var createOrderRequired = createOrderSchema["required"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToList();
        createOrderRequired.Should().Contain("customerName");
        createOrderRequired.Should().Contain("product");

        var updateStatusSchema = toolsByName["update_order_status"];
        var updateStatusProperties = updateStatusSchema["properties"]!.AsObject();
        updateStatusProperties["id"]!.AsObject()["type"]!.GetValue<string>().Should().Be("integer");
        var statusSchema = updateStatusProperties["status"]!.AsObject();
        statusSchema["type"]!.GetValue<string>().Should().Be("string");
        statusSchema.Should().HaveProperty("pattern");
        var updateStatusRequired = updateStatusSchema["required"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToList();
        updateStatusRequired.Should().Contain("id");
        updateStatusRequired.Should().Contain("status");
    }

    [Fact]
    public async Task ToolCall_GetOrder_ReturnsOrder()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = 1 } }
        });

        response.Should().HaveProperty("result");
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = result["content"]!.AsArray()[0]!.AsObject()["text"]!.GetValue<string>();
        var order = JsonSerializer.Deserialize<JsonElement>(content);
        order.GetProperty("id").GetInt32().Should().Be(1);
        order.GetProperty("customerName").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task ToolCall_ListOrders_FiltersByStatus()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 4,
            method = "tools/call",
            @params = new { name = "list_orders", arguments = new { status = "pending" } }
        });

        var content = ExtractTextContent(response);
        var orders = JsonSerializer.Deserialize<JsonElement[]>(content);
        orders.Should().AllSatisfy(o => o.GetProperty("status").GetString().Should().Be("pending"));
    }

    [Fact]
    public async Task ToolCall_CreateOrder_CreatesAndReturnsOrder()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 5,
            method = "tools/call",
            @params = new
            {
                name = "create_order",
                arguments = new { customerName = "Charlie", product = "Thingamajig", quantity = 5 }
            }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        var content = ExtractTextContent(response);
        var order = JsonSerializer.Deserialize<JsonElement>(content);
        order.GetProperty("customerName").GetString().Should().Be("Charlie");
        order.GetProperty("status").GetString().Should().Be("pending");
    }

    [Fact]
    public async Task ToolCall_CreateOrder_MissingRequiredFields_ReturnsMcpError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 21,
            method = "tools/call",
            @params = new
            {
                name = "create_order",
                arguments = new { quantity = 2 }
            }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        var errorText = ExtractTextContent(response);
        errorText.Should().Contain("HTTP 400");
        errorText.Should().ContainEquivalentOf("customerName");
    }

    [Fact]
    public async Task ToolCall_GetOrder_WithWrongArgumentType_ReturnsMcpError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 22,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = "not-an-int" } }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        ExtractTextContent(response).Should().Contain("HTTP 400");
    }

    [Fact]
    public async Task ToolCall_GetOrder_WithEmptyArguments_ReturnsMcpError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 23,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { } }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        ExtractTextContent(response).Should().Contain("Tool 'get_order' failed with HTTP");
    }

    [Fact]
    public async Task ToolCall_UpdateOrderStatus_InvalidStatus_ReturnsMcpError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 24,
            method = "tools/call",
            @params = new
            {
                name = "update_order_status",
                arguments = new { id = 1, status = "invalid-status-value" }
            }
        });

        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        var errorText = ExtractTextContent(response);
        errorText.Should().Contain("HTTP 400");
        errorText.Should().ContainEquivalentOf("status");
    }

    [Fact]
    public async Task ToolCall_ProtectedEndpoint_Unauthorized_ReturnsMcpErrorResult()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 25,
            method = "tools/call",
            @params = new { name = "get_secure_order", arguments = new { id = 1 } }
        });

        var result = response["result"]!.AsObject();
        TestContext.Current?.TestOutputHelper?.WriteLine(response.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        result["isError"]!.GetValue<bool>().Should().BeTrue();
        ExtractTextContent(response).Should().Contain("HTTP 401");
    }

    [Fact]
    public async Task ToolCall_UnknownTool_ReturnsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 6,
            method = "tools/call",
            @params = new { name = "nonexistent_tool", arguments = new { } }
        });
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ToolCall_GetOrder_NotFound_ReturnsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "2.0",
            id = 7,
            method = "tools/call",
            @params = new { name = "get_order", arguments = new { id = 9999 } }
        });
        var result = response["result"]!.AsObject();
        result["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task InvalidJsonRpc_ReturnsError()
    {
        var response = await PostMcpAsync(new
        {
            jsonrpc = "1.0",
            id = 8,
            method = "tools/list"
        });
        response.Should().HaveProperty("error");
        response["error"]!.AsObject()["code"]!.GetValue<int>().Should().Be(-32600);
    }

    [Fact]
    public async Task MalformedJsonBody_ReturnsParseError()
    {
        const string malformedRequest = """{"jsonrpc":"2.0","id":9,"method":"tools/list","params":{"x":""";
        var response = await PostRawMcpAsync(malformedRequest);

        response.Should().HaveProperty("error");
        var error = response["error"]!.AsObject();
        error["code"]!.GetValue<int>().Should().Be(-32700);
        error["message"]!.GetValue<string>().Should().Be("Parse error");
    }

    private async Task<JsonObject> PostMcpAsync(object body, IReadOnlyDictionary<string, string>? headers = null)
    {
        var json = JsonSerializer.Serialize(body);
        return await PostRawMcpAsync(json, headers);
    }

    private async Task<JsonObject> PostRawMcpAsync(string rawBody, IReadOnlyDictionary<string, string>? headers = null)
    {
        var content = new StringContent(rawBody, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = content };
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                request.Headers.TryAddWithoutValidation(key, value);
        }
        var httpResponse = await _client.SendAsync(request);
        var responseJson = await httpResponse.Content.ReadAsStringAsync();
        return JsonNode.Parse(responseJson)!.AsObject();
    }

    private static string ExtractTextContent(JsonObject response)
    {
        return response["result"]!.AsObject()["content"]!.AsArray()[0]!
            .AsObject()["text"]!.GetValue<string>();
    }
}
