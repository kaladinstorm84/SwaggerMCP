namespace ZeroMCP;

/// <summary>
/// Locked MCP (Model Context Protocol) transport constants.
/// Used for production hardening: protocol version is fixed until a major release.
/// </summary>
public static class McpProtocolConstants
{
    /// <summary>
    /// MCP protocol version supported by this implementation (streamable HTTP / 2024-11-05).
    /// Do not change without a major version bump and compatibility tests.
    /// </summary>
    public const string ProtocolVersion = "2024-11-05";
}
