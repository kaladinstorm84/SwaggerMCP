using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SampleApi.Auth;

internal sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    private const string HeaderName = "X-Api-Key";
    private const string ValidKey = "dev-key";

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    private const string AdminKey = "admin-key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var apiKey))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (apiKey.Count != 1)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var key = apiKey[0];
        if (string.IsNullOrEmpty(key) || (!string.Equals(key, ValidKey, StringComparison.Ordinal) && !string.Equals(key, AdminKey, StringComparison.Ordinal)))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, string.Equals(key, AdminKey, StringComparison.Ordinal) ? "admin" : "mcp-test"),
            new Claim(ClaimTypes.Name, string.Equals(key, AdminKey, StringComparison.Ordinal) ? "Admin Client" : "MCP Test Client")
        };
        if (string.Equals(key, AdminKey, StringComparison.Ordinal))
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
