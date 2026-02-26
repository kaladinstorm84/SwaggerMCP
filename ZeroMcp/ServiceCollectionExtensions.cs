using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZeroMCP.Dispatch;
using ZeroMCP.Discovery;
using ZeroMCP.Observability;
using ZeroMCP.Options;
using ZeroMCP.Schema;
using ZeroMCP.Transport;

namespace ZeroMCP.Extensions;

/// <summary>
/// Extension methods for registering ZeroMCP services with the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds ZeroMCP services. Call this before <c>builder.Build()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddZeroMCP(options =>
    /// {
    ///     options.ServerName = "My API";
    ///     options.RoutePrefix = "/mcp";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddZeroMcp(
        this IServiceCollection services,
        Action<ZeroMCPOptions>? configure = null)
    {
        // Register options
        var optionsBuilder = services.AddOptions<ZeroMCPOptions>();
        if (configure is not null)
            optionsBuilder.Configure(configure);

        // Resolve server name from entry assembly if not set
        services.PostConfigure<ZeroMCPOptions>(options =>
        {
            options.ServerName ??= Assembly.GetEntryAssembly()?.GetName().Name ?? "ZeroMCP Server";
        });

        // Core infrastructure — all singletons since they cache at startup
        services.AddSingleton<McpSchemaBuilder>();
        services.AddSingleton<McpToolDiscoveryService>();

        // Dispatch infrastructure
        services.AddSingleton<SyntheticHttpContextFactory>();
        services.AddSingleton<McpToolDispatcher>();

        // Observability: metrics sink (register your own after AddZeroMCP to replace the no-op)
        services.AddSingleton<IMcpMetricsSink, NoOpMcpMetricsSink>();

        // Transport
        services.AddSingleton<McpSwaggerToolHandler>();

        // IHttpContextFactory is needed for synthetic context creation
        services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();

        return services;
    }
}
