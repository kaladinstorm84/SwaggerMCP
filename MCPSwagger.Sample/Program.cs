using SwaggerMcp.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSwaggerMcp(options =>
{
    options.ServerName = "Orders API";
    options.ServerVersion = "1.0.0";
    options.RoutePrefix = "/mcp";
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

// Minimal API example: exposed as MCP tool via WithMcpTool
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
   .WithMcpTool("health_check", "Returns API health status.", tags: new[] { "system" });

app.MapSwaggerMcp();

app.Run();

// Expose for WebApplicationFactory<Program> in integration tests
public partial class Program { }
