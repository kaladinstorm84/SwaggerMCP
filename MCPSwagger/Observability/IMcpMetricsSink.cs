namespace ZeroMCP.Observability;

/// <summary>
/// Optional sink for MCP tool invocation metrics (timing, success/failure).
/// Register your own implementation in DI to push metrics to Prometheus, Application Insights, etc.
/// When not registered, a no-op is used.
/// </summary>
public interface IMcpMetricsSink
{
    /// <summary>
    /// Records a single tool invocation for metrics/tracing.
    /// </summary>
    /// <param name="toolName">Name of the tool invoked.</param>
    /// <param name="statusCode">HTTP status code from the dispatched action.</param>
    /// <param name="isError">True if the tool returned an error (4xx/5xx or MCP error).</param>
    /// <param name="durationMs">Elapsed time in milliseconds.</param>
    /// <param name="correlationId">Optional correlation ID for the request.</param>
    void RecordToolInvocation(string toolName, int statusCode, bool isError, double durationMs, string? correlationId = null);
}
