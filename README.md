# SwaggerMcp

Expose your existing ASP.NET Core API as an MCP (Model Context Protocol) server with a single attribute and two lines of setup. No separate process. No code duplication.

## How It Works

Tag controller actions with `[McpTool]` or minimal APIs with `.WithMcpTool(...)`. SwaggerMcp will:

1. **Discover** tools at startup from controller API descriptions (same source as Swagger) and from minimal API endpoints that use `WithMcpTool`
2. **Generate** a JSON Schema for each tool's inputs (route, query, and body merged)
3. **Expose** a single endpoint (GET and POST `/mcp`) that speaks the MCP Streamable HTTP transport
4. **Dispatch** tool calls in-process through your real action or endpoint pipeline — filters, validation, and authorization run normally

```
MCP Client (Claude Desktop, Claude.ai, etc.)
    │
    │  GET /mcp (info)  or  POST /mcp (JSON-RPC 2.0)
    ▼
SwaggerMcp Endpoint
    │
    │  in-process dispatch (controller or minimal endpoint)
    ▼
Your Action / Endpoint  ← [McpTool] or .WithMcpTool(...)
    │
    │  real response
    ▼
MCP Client gets structured result
```

---

## Quick Start

### 1. Install

```xml
<PackageReference Include="SwaggerMcp" Version="1.0.2" />
```

### 2. Register services

```csharp
// Program.cs
builder.Services.AddSwaggerMcp(options =>
{
    options.ServerName = "My Orders API";
    options.ServerVersion = "1.0.0";
});
```

### 3. Map the endpoint

```csharp
app.MapSwaggerMcp(); // registers GET and POST /mcp
```

### 4. Tag your actions

```csharp
[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    [HttpGet("{id}")]
    [McpTool("get_order", Description = "Retrieves a single order by ID.")]
    public ActionResult<Order> GetOrder(int id) { ... }

    [HttpPost]
    [McpTool("create_order", Description = "Creates a new order. Returns the created order.")]
    public ActionResult<Order> CreateOrder([FromBody] CreateOrderRequest request) { ... }

    [HttpDelete("{id}")]
    // No [McpTool] — invisible to MCP clients
    public IActionResult Delete(int id) { ... }
}
```

Point any MCP client at your app's `/mcp` URL; it will see your tagged controller actions and minimal endpoints as tools.

---

## Configuration

```csharp
builder.Services.AddSwaggerMcp(options =>
{
    options.ServerName = "My API";         // shown during MCP handshake
    options.ServerVersion = "2.0.0";       // shown during MCP handshake
    options.RoutePrefix = "/mcp";          // where the endpoint is mounted
    options.IncludeInputSchemas = true;    // attach JSON Schema to tools (helps LLM)
    options.ForwardHeaders = ["Authorization"];  // copy these from MCP request to tool dispatch

    // Optional: filter which tagged tools are exposed at runtime
    options.ToolFilter = name => !name.StartsWith("admin_");
});
```

### Custom route

```csharp
app.MapSwaggerMcp("/api/mcp");  // overrides options.RoutePrefix
```

---

## The `[McpTool]` Attribute

```csharp
[McpTool(
    name: "create_order",               // Required. Snake_case tool name for the LLM.
    Description = "Creates an order.",  // Shown to the LLM. Be descriptive.
    Tags = ["write", "orders"]          // Optional. For grouping/filtering.
)]
```

### Placement rules

- **Per-action only** — `[McpTool]` goes on individual action methods, not controllers
- **One name per application** — duplicate names are logged as warnings and skipped
- **Any HTTP method** — GET, POST, PATCH, DELETE all work
- **Description** — If you omit `Description`, SwaggerMcp uses the method's XML doc `<summary>` when available.

---

## How Parameters Are Mapped

SwaggerMcp merges all parameter sources into a single flat JSON Schema object that the LLM fills in:

| Parameter source | MCP mapping |
|---|---|
| Route params (`{id}`) | Always required properties |
| Query params (`?status=`) | Optional (or required if `[Required]`) |
| `[FromBody]` object | Properties expanded inline from JSON Schema |

**Example:**

```csharp
[HttpPatch("{id}/status")]
[McpTool("update_order_status", Description = "Updates an order's status.")]
public IActionResult UpdateStatus(int id, [FromBody] UpdateStatusRequest req) { ... }

public class UpdateStatusRequest
{
    [Required] public string Status { get; set; }
    public string? Reason { get; set; }
}
```

Produces this MCP input schema:

```json
{
  "type": "object",
  "properties": {
    "id":     { "type": "integer" },
    "status": { "type": "string" },
    "reason": { "type": "string" }
  },
  "required": ["id", "status"]
}
```

---

## In-Process Dispatch

When the MCP client calls a tool, SwaggerMcp:

1. Creates a fresh **DI scope** (same as a real request)
2. Builds a **synthetic `HttpContext`** with route values (including ambient `controller`/`action` for link generation), query string, and body from the JSON arguments
3. Sets the matched **endpoint** on the context so `CreatedAtAction` and `LinkGenerator` work
4. Invokes the controller action via `IActionInvokerFactory` or the minimal endpoint's `RequestDelegate`
5. Captures the response body and forwards it as the MCP result

This means:
- `[Authorize]` works — set up auth on the MCP endpoint and your action filters enforce it
- **Auth forwarding** — Headers in `ForwardHeaders` (e.g. `Authorization`) are copied from the MCP request to the synthetic request
- **CreatedAtAction** works — synthetic request has endpoint and controller/action route values so link generation succeeds
- `[ValidateModel]` / `ModelState` works — validation errors return as MCP error results
- Exception filters work — unhandled exceptions are caught and returned gracefully
- Your existing DI services, repositories, and business logic are called as-is

---

## Minimal API endpoints

You can expose minimal API endpoints as MCP tools by calling `.WithMcpTool(...)` when mapping:

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithMcpTool("health_check", "Returns API health status.", tags: new[] { "system" });
```

- **Name** (required) — snake_case tool name for the LLM
- **Description** (optional) — shown to the LLM
- **Tags** (optional) — for grouping/filtering

Discovery includes both controller actions (from API descriptions) and minimal endpoints (from `EndpointDataSource`). Route parameters on minimal APIs are supported; query/body binding is limited to what the route pattern exposes.

---

## Connecting MCP Clients

### Claude Desktop

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "my-api": {
      "type": "http",
      "url": "http://localhost:5000/mcp"
    }
  }
}
```

### Claude.ai (remote MCP)

Point at your deployed API's `/mcp` endpoint. For production, add authentication — SwaggerMcp doesn't impose any auth on the `/mcp` route itself, so you can apply standard ASP.NET Core auth middleware or `.RequireAuthorization()` as needed:

```csharp
app.MapSwaggerMcp().RequireAuthorization("McpPolicy");
```

---

## Project Structure

```
mcpAPI/
├── MCPSwagger/                    ← Library (NuGet package SwaggerMcp)
│   ├── Attributes/                ← [McpTool]
│   ├── Discovery/                 ← Controller + minimal API tool discovery
│   ├── Schema/                    ← JSON Schema for tool inputs (NJsonSchema)
│   ├── Dispatch/                  ← Synthetic HttpContext, controller/minimal invoke
│   ├── Metadata/                  ← McpToolEndpointMetadata for minimal APIs
│   ├── Extensions/                ← AddSwaggerMcp, MapSwaggerMcp, WithMcpTool
│   ├── Options/                   ← SwaggerMcpOptions
│   └── MCPSwagger.csproj         (PackageId: SwaggerMcp, Version: 1.0.2)
├── MCPSwagger.Sample/             ← Sample (Orders API, health minimal endpoint, optional auth)
├── nupkgs/                        ← dotnet pack -o nupkgs
├── progress.md
└── README.md
```

---

## Known Limitations

- **Streamable HTTP only** — stdio and SSE transports are not supported
- **Minimal APIs** — supported via `WithMcpTool`; route params are bound; query/body binding is limited
- **[FromForm] and file uploads** — not supported; JSON-only body binding
- **Streaming responses** — `IAsyncEnumerable<T>` and SSE action results are not captured correctly
- If **CreatedAtAction** or link generation ever fails in your environment, use `return Created(Url.Action(nameof(OtherAction), new { id = entity.Id })!, entity);` as a fallback

---

## Build

- **Targets:** .NET 9.0 and .NET 10.0 (library); sample and tests may target a single framework.
- **Library:** `dotnet build MCPSwagger\MCPSwagger.csproj`
- **Sample:** `dotnet build MCPSwagger.Sample\MCPSwagger.Sample.csproj`
- **Tests:** `dotnet build MCPSwagger.Tests\MCPSwagger.Tests.csproj` then `dotnet test MCPSwagger.Tests\MCPSwagger.Tests.csproj`
- **TestService:** `dotnet build TestService\TestService.csproj`

### Test coverage

Integration and schema tests cover JSON-RPC validation and errors, model binding failures, wrong/empty arguments, unauthorized `[Authorize]` tool calls, `tools/list` schema shape, and schema edge cases (nested objects, arrays, enums, route+body merging).

---



## Contributing

PRs welcome. The most impactful next additions would be:

1. SSE transport support
2. Richer minimal API parameter binding (query/body from route delegate)
