using Microsoft.AspNetCore.Authentication;
using SampleApi.Auth;
using SwaggerMcp.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapSwaggerMcp();

app.Run();

// Expose for WebApplicationFactory<Program> in integration tests
public partial class Program { }
