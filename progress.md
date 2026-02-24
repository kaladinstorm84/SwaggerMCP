# Progress

## 2026-02-24 – Build errors resolved

### Changes made

1. **MCPSwagger.csproj**
   - Removed invalid `Microsoft.AspNetCore` (Version 2.3.9) package reference; it was unnecessary for net10.0 and caused NU1510.
   - Added **NJsonSchema** (11.0.0) for `McpSchemaBuilder` (JSON Schema generation, `SystemTextJsonSchemaGeneratorSettings`, `JsonObjectType`).
   - Added **Swashbuckle.AspNetCore** (7.2.0) for `Program.cs` (AddSwaggerGen, UseSwagger, UseSwaggerUI).

2. **McpSwaggerToolHandler.cs**
   - Removed unused `using ModelContextProtocol.Server;` (types `McpToolDefinition` and `McpToolResult` are defined in the same file).

3. **SyntheticHttpContextFactory.cs**
   - Made `SyntheticHttpContextFactory` **public** to fix CS0051 (parameter type less accessible than `McpToolDispatcher` constructor).
   - Replaced `RequestServicesFeature(scope.ServiceProvider)` with custom **SyntheticRequestServicesFeature** (ASP.NET Core 10 constructor expects `(HttpContext, IServiceScopeFactory?)`).
   - Removed **HttpActivityFeature** usage (type is inaccessible in ASP.NET Core 10).

4. **McpSchemaBuilderTests.cs**
   - Replaced `typeof(string?)` with `typeof(string)` for the optional "filter" query parameter to fix CS8639 (typeof on nullable reference type).

5. **TestService**
   - **CreateOrderRequest.cs** and **Order.cs**: Initialized `ProductName` with `= string.Empty` to clear CS8618 (non-nullable property when exiting constructor).

### Build status

- `dotnet build TestService\TestService.csproj` — **succeeds** (0 errors, 0 warnings).
- Application (TestService) builds and is ready to run; ensure `Program.cs` registers controllers and SwaggerMcp if you use the Orders API and MCP endpoint.

### Later fix: FluentAssertions JSON API

- **McpEndpointIntegrationTests.cs:** Replaced `ContainKey("result")` / `ContainKey("error")` with **`HaveProperty("result")`** / **`HaveProperty("error")`**. `JsonNodeAssertions<JsonObject>` uses `HaveProperty` / `NotHaveProperty`, not `ContainKey`.

### GET /mcp returning something

- **McpHttpEndpointHandler:** For **GET** requests to `/mcp`, now return a JSON description (protocol, server name/version, example initialize payload). **POST** unchanged (JSON-RPC 2.0).
- **EndpointRouteBuilderExtensions:** Route registered with **MapMethods(route, ["GET", "POST"], ...)** so both GET and POST are handled at `/mcp`.

### NuGet package layout

- **MCPSwagger** is now a library-only project that packs as a NuGet package.
  - **MCPSwagger.csproj:** OutputType=Library, only NJsonSchema dependency; PackageId=SwaggerMcp, Version=1.0.0; Compile Remove for Program.cs, OrdersController.cs, *Tests.cs (moved to other projects).
  - **MCPSwagger.Sample:** Standalone sample app (Program.cs, Controllers/OrdersController.cs), references MCPSwagger + Swashbuckle.
  - **MCPSwagger.Tests:** Unit and integration tests; references MCPSwagger and MCPSwagger.Sample (WebApplicationFactory&lt;Program&gt;).
- **Pack:** `dotnet pack MCPSwagger\MCPSwagger.csproj -c Release -o .\nupkgs` produces **SwaggerMcp.1.0.0.nupkg**.
- **TestService** still references MCPSwagger via ProjectReference (unchanged).

## 2026-02-24 – Expanded MCP validation/schema/auth test coverage

### Changes made

1. **MCPSwagger.Sample/Program.cs**
   - Added authentication/authorization services:
     - `AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)`
     - `AddAuthorization()`
   - Added `app.UseAuthentication()` before `app.UseAuthorization()`.

2. **MCPSwagger.Sample/ApiKeyAuthenticationHandler.cs** (new)
   - Added a lightweight API-key auth handler (`X-Api-Key: dev-key`) for sample authorization scenarios.
   - Missing/invalid key yields unauthenticated requests, enabling deterministic unauthorized behavior in tests.

3. **MCPSwagger.Sample/Controllers/OrdersController.cs**
   - Added protected MCP tool:
     - `get_secure_order` (`[Authorize]`) for auth failure-path verification.
   - Added status value validation on `UpdateStatusRequest.Status`:
     - `[RegularExpression("^(pending|shipped|cancelled)$", ...)]`
   - This enables a concrete invalid-status model-validation test case.

4. **MCPSwagger.Tests/McpEndpointIntegrationTests.cs**
   - Added tool-list schema shape assertions (not just tool-name presence).
   - Added MCP transport and validation/error tests for:
     - malformed JSON body parse errors (`-32700`)
     - create-order model-state failure (missing required fields)
     - wrong argument type (`id` as string instead of int)
     - empty `{}` arguments for a required-params tool
     - valid route + invalid body value (`update_order_status`)
     - unauthorized protected endpoint call returning MCP error content (HTTP 401 wrapped in MCP result)
   - Updated tool list assertions to include `get_secure_order`.

5. **MCPSwagger.Tests/McpSchemaBuilderTests.cs**
   - Expanded schema-builder coverage for:
     - `[Required]` body properties flowing into `required[]`
     - nullable primitive query parameter generating `["type","null"]`
     - body + route merged schema + required propagation
     - nested complex body property shape (`object`)
     - collection properties (`List<string>`, arrays) mapping to `array`
     - enum property containing an `enum` array
     - empty body type yielding object schema with no properties

6. **README.md**
   - Added a test-coverage highlights section documenting the expanded validation/schema/auth scenarios.

### Verification status in this environment

- Attempted:
  - `dotnet build MCPSwagger.Tests/MCPSwagger.Tests.csproj -v detailed`
  - `dotnet` location checks (`command -v dotnet`, `whereis dotnet`, `/usr/share/dotnet`)
- Result:
  - Build/test execution is currently blocked on this runner because the .NET SDK is not installed (`dotnet: command not found`).
