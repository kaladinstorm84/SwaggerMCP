# SwaggerMcp

Expose your existing ASP.NET Core API as an MCP (Model Context Protocol) server with a single attribute and two lines of setup. No separate process. No code duplication.

## How It Works

Tag any controller action with `[McpTool]` and SwaggerMcp will:

1. **Discover** it at startup via ASP.NET Core's `IApiDescriptionGroupCollectionProvider` (same source as Swagger)
2. **Generate** a JSON Schema for its inputs by merging route params, query params, and body properties
3. **Expose** it via a `POST /mcp` endpoint that speaks the MCP Streamable HTTP transport
4. **Dispatch** calls in-process through your real action pipeline — filters, validation, and authorization all run normally

```
MCP Client (Claude Desktop, Claude.ai, etc.)
    │
    │  POST /mcp  (JSON-RPC 2.0, MCP protocol)
    ▼
SwaggerMcp Endpoint
    │
    │  in-process dispatch
    ▼
Your Controller Action  ← [McpTool] tagged
    │
    │  real response
    ▼
MCP Client gets structured result
```

---

## Quick Start

### 1. Install

```xml
<PackageReference Include="SwaggerMcp" Version="1.0.0" />
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
app.MapSwaggerMcp(); // registers POST /mcp
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

That's it. Point any MCP client at `POST /mcp` and it will see your tagged endpoints as tools.

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
- **Description** — If you omit `Description`, SwaggerMcp will use the method's XML doc `<summary>` when available (Phase 1).

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
2. Builds a **synthetic `HttpContext`** with route values, query string, and body stream populated from the JSON arguments
3. Invokes the action via `IActionInvokerFactory` — **your full filter pipeline runs**
4. Captures the response body and forwards it as the MCP result

This means:
- `[Authorize]` works — set up auth on the MCP endpoint and your action filters enforce it
- **Auth forwarding** — Headers listed in `ForwardHeaders` (e.g. `Authorization`) are copied from the MCP request to the synthetic request, so the dispatched action sees the same auth (Phase 1).
- `[ValidateModel]` / `ModelState` works — validation errors return as MCP error results
- Exception filters work — unhandled exceptions are caught and returned gracefully
- Your existing DI services, repositories, and business logic are called as-is

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
├── MCPSwagger/                    ← Library (packs as NuGet)
│   ├── Attributes/McpToolAttribute.cs
│   ├── Discovery/
│   ├── Schema/McpSchemaBuilder.cs (NJsonSchema)
│   ├── Dispatch/
│   ├── Transport/
│   ├── Extensions/
│   ├── SwaggerMcpOptions.cs
│   └── MCPSwagger.csproj          (PackageId: SwaggerMcp, Version: 1.0.0)
├── MCPSwagger.Sample/             ← Sample app (Program + Orders API + Swagger UI)
│   ├── Program.cs
│   └── Controllers/OrdersController.cs
├── MCPSwagger.Tests/              ← Unit + integration tests
├── TestService/                   ← Example API consuming MCPSwagger (project ref)
├── nupkgs/                        ← dotnet pack -o nupkgs
├── progress.md
└── README.md
```

## Minimal API endpoints (Phase 2)

You can expose minimal API endpoints as MCP tools by calling `.WithMcpTool(...)` when mapping the endpoint:

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithMcpTool("health_check", "Returns API health status.", tags: new[] { "system" });
```

- **Name** (required) — snake_case tool name for the LLM.
- **Description** (optional) — shown to the LLM.
- **Tags** (optional) — for grouping/filtering.

Discovery reads from `EndpointDataSource` in addition to controller API descriptions; dispatch invokes the endpoint's `RequestDelegate` directly. Route parameters are supported; query/body binding for minimal APIs may be limited depending on the route pattern.

---

## Known Limitations

- **Streamable HTTP only** — stdio and SSE transports are not currently supported
- **Minimal APIs** — supported via `WithMcpTool`; route params are bound; query/body handling is minimal-API specific
- **[FromForm] and file uploads** — not supported; JSON-only body binding
- **CreatedAtAction** — controller actions that return `CreatedAtAction` are dispatched with the matched endpoint set so link generation can succeed. If you still see 500s, use `return Created(Url.Action(nameof(OtherAction), new { id = entity.Id })!, entity);` instead.
- **Streaming responses** — `IAsyncEnumerable<T>` and SSE action results are not captured correctly

---

## Build

- Target: .NET 10.0.
- **Library:** `dotnet build MCPSwagger\MCPSwagger.csproj`
- **Sample app:** `dotnet build MCPSwagger.Sample\MCPSwagger.Sample.csproj`
- **Tests:** `dotnet build MCPSwagger.Tests\MCPSwagger.Tests.csproj` then `dotnet test MCPSwagger.Tests\MCPSwagger.Tests.csproj`
- **TestService (consumer):** `dotnet build TestService\TestService.csproj`

## NuGet package

To produce a `.nupkg`:

```bash
dotnet pack MCPSwagger\MCPSwagger.csproj -c Release -o .\nupkgs
```

This creates `nupkgs\SwaggerMcp.1.0.0.nupkg`. The library has no dependency on Swashbuckle or test packages; only **NJsonSchema** is included. Package metadata (id, version, description, tags, license) is in **MCPSwagger.csproj**. To publish to NuGet.org, run `dotnet nuget push nupkgs\SwaggerMcp.1.0.0.nupkg --source https://api.nuget.org/v3/index.json --api-key YOUR_KEY`.

---

## Contributing

PRs welcome. The most impactful next additions would be:

1. SSE transport support
2. Richer minimal API parameter binding (query/body from route delegate)
