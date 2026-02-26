# ZeroMCP

Expose your ASP.NET Core API as an **MCP (Model Context Protocol)** server. Tag controller actions with `[McpTool]` or minimal APIs with `.WithMcpTool(...)`; ZeroMCP discovers them, builds JSON Schema for inputs, and exposes a single **POST /mcp** endpoint that speaks the MCP Streamable HTTP transport. Tool calls are dispatched in-process through your real pipeline (filters, validation, authorization run as normal).

**Full documentation** (configuration, governance, observability, minimal APIs, limitations): [repository README](https://github.com/kaladinstorm84/ZeroMCP) or your GitLab repo root `README.md`.

---

## Install

```xml
<PackageReference Include="ZeroMCP" Version="1.*" />
```

---

## Quick Start

**1. Register and map**

```csharp
// Program.cs
builder.Services.AddZeroMCP(options =>
{
    options.ServerName = "My API";
    options.ServerVersion = "1.0.0";
});

// After UseRouting(), UseAuthorization()
app.MapZeroMCP();  // GET and POST /mcp
```

**2. Tag controller actions**

```csharp
[HttpGet("{id}")]
[McpTool("get_order", Description = "Retrieves a single order by ID.")]
public ActionResult<Order> GetOrder(int id) { ... }

[HttpPost]
[McpTool("create_order", Description = "Creates a new order.")]
public ActionResult<Order> CreateOrder([FromBody] CreateOrderRequest request) { ... }
```

**3. Optional: minimal APIs**

```csharp
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
   .WithMcpTool("health_check", "Returns API health status.");
```

If you use **both** controllers and minimal APIs, add `builder.Services.AddEndpointsApiExplorer();` and `app.MapControllers();` so controller tools are discovered.

Point any MCP client (e.g. Claude Desktop) at your app’s `/mcp` URL.

---

## Configuration (summary)

| Option | Default | Description |
|--------|---------|-------------|
| `RoutePrefix` | `"/mcp"` | Endpoint path |
| `ServerName` / `ServerVersion` | — | Shown in MCP handshake |
| `ForwardHeaders` | `["Authorization"]` | Headers copied to tool dispatch |
| `ToolFilter` | `null` | Discovery-time filter by tool name |
| `ToolVisibilityFilter` | `null` | Per-request filter `(name, ctx) => bool` |
| `CorrelationIdHeader` | `"X-Correlation-ID"` | Request/response correlation ID |
| `EnableOpenTelemetryEnrichment` | `false` | Tag `Activity.Current` with MCP tool details |

**Governance:** Use `[McpTool(..., Roles = new[] { "Admin" }, Policy = "RequireEditor")]` or `.WithMcpTool(..., roles: ..., policy: ...)` to restrict which tools appear in `tools/list` per user.

**Metrics:** Implement `IMcpMetricsSink` and register it after `AddZeroMCP()` to record tool invocations (name, status code, duration, success/failure).

---

## Versioning

We follow [Semantic Versioning](https://semver.org/). Breaking changes are documented in the repository (e.g. `VERSIONING.md`).
