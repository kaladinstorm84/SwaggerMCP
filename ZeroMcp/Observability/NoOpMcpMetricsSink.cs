namespace ZeroMCP.Observability;

/// <summary>
/// No-op implementation of <see cref="IMcpMetricsSink"/>. Used when no custom sink is registered.
/// </summary>
internal sealed class NoOpMcpMetricsSink : IMcpMetricsSink
{
    public void RecordToolInvocation(string toolName, int statusCode, bool isError, double durationMs, string? correlationId = null)
    {
    }
}
