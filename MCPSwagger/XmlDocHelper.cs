using System.Reflection;
using System.Xml;

namespace ZeroMCP.Discovery;

/// <summary>
/// Reads XML documentation comments for method summary (fallback when [McpTool] Description is not set).
/// </summary>
internal static class XmlDocHelper
{
    /// <summary>
    /// Gets the &lt;summary&gt; text for the given method from the assembly's XML doc file, if present.
    /// Returns null if the file is missing, the member is not found, or the summary is empty.
    /// </summary>
    public static string? GetMethodSummary(MethodInfo method)
    {
        if (method.DeclaringType is null) return null;

        var assembly = method.DeclaringType.Assembly;
        var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
        if (string.IsNullOrEmpty(xmlPath) || !File.Exists(xmlPath)) return null;

        try
        {
            var doc = new XmlDocument();
            doc.Load(xmlPath);
            var memberId = GetMemberId(method);
            var escaped = memberId.Replace("'", "&apos;");
            var member = doc.SelectSingleNode($"/doc/members/member[@name='{escaped}']");
            var summary = member?.SelectSingleNode("summary")?.InnerText?.Trim();
            return string.IsNullOrWhiteSpace(summary) ? null : summary;
        }
        catch
        {
            return null;
        }
    }

    private static string GetMemberId(MethodInfo method)
    {
        // XML doc member ID for methods: M:Namespace.Type.MethodName(ParamType1,ParamType2)
        var type = method.DeclaringType!;
        var typeName = type.FullName ?? type.Name;
        typeName = typeName.Replace('+', '.'); // Nested types use . in XML doc
        var methodName = method.Name;
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
            return $"M:{typeName}.{methodName}";
        var paramTypes = string.Join(",", parameters.Select(p => GetParamTypeName(p.ParameterType)));
        return $"M:{typeName}.{methodName}({paramTypes})";
    }

    private static string GetParamTypeName(Type type)
    {
        if (type.IsByRef)
            type = type.GetElementType()!;
        var name = type.FullName ?? type.Name;
        name = name.Replace('+', '.');
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            var backtick = name.IndexOf('`');
            if (backtick >= 0) name = name[..backtick];
            name += "{" + string.Join(",", type.GetGenericArguments().Select(GetParamTypeName)) + "}";
        }
        return name;
    }
}
