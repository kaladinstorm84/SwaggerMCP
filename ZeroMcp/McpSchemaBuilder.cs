using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using NJsonSchema;
using NJsonSchema.Generation;
using ZeroMCP.Discovery;

namespace ZeroMCP.Schema;

/// <summary>
/// Builds a merged JSON Schema for an MCP tool's input, combining route params,
/// query params, and body properties into a single flat schema object.
/// </summary>
public sealed class McpSchemaBuilder
{
    private static readonly SystemTextJsonSchemaGeneratorSettings SchemaSettings = new()
    {
        SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }
    };

    /// <summary>
    /// Builds a JSON Schema string for the given tool descriptor.
    /// Returns a JSON object schema with all parameters as properties.
    /// </summary>
    public string BuildSchema(McpToolDescriptor descriptor)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        // Route parameters
        foreach (var param in descriptor.RouteParameters)
        {
            properties[param.Name] = BuildPrimitiveProperty(param.ParameterType, param.Description);
            // Route params are always required
            required.Add(param.Name);
        }

        // Query parameters
        foreach (var param in descriptor.QueryParameters)
        {
            properties[param.Name] = BuildPrimitiveProperty(param.ParameterType, param.Description);
            if (param.IsRequired)
                required.Add(param.Name);
        }

        // Body: expand its properties inline
        if (descriptor.Body is not null)
        {
            var bodyProperties = ExtractBodyProperties(descriptor.Body.BodyType, required);
            foreach (var (key, value) in bodyProperties)
                properties[key] = value;
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
            schema["required"] = required;

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static Dictionary<string, object> BuildPrimitiveProperty(Type type, string? description)
    {
        var prop = new Dictionary<string, object>
        {
            ["type"] = GetJsonSchemaType(type)
        };

        if (description is not null)
            prop["description"] = description;

        // Handle nullable
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            prop["type"] = new[] { GetJsonSchemaType(underlying), "null" };
        }

        return prop;
    }

    private static Dictionary<string, object> ExtractBodyProperties(Type bodyType, List<string> requiredList)
    {
        var result = new Dictionary<string, object>();

        try
        {
            // Use NJsonSchema to get the full schema for the body type
            var schema = JsonSchema.FromType(bodyType, SchemaSettings);

            foreach (var (propName, propSchema) in schema.Properties)
            {
                var camelName = char.ToLowerInvariant(propName[0]) + propName[1..];
                var propObj = new Dictionary<string, object>();

                // Type
                if (propSchema.Type == JsonObjectType.None && propSchema.HasReference)
                {
                    // Complex nested object — just mark as object
                    propObj["type"] = "object";
                }
                else
                {
                    var typeStr = MapNJsonType(propSchema.Type);
                    propObj["type"] = typeStr;
                }

                // Description
                if (!string.IsNullOrEmpty(propSchema.Description))
                    propObj["description"] = propSchema.Description;

                // Enum values from NJsonSchema
                if (propSchema.Enumeration.Count > 0)
                    propObj["enum"] = propSchema.Enumeration.Select(e => e?.ToString() ?? "").ToArray();

                // Min/max
                if (propSchema.Minimum.HasValue) propObj["minimum"] = propSchema.Minimum.Value;
                if (propSchema.Maximum.HasValue) propObj["maximum"] = propSchema.Maximum.Value;
                if (propSchema.MinLength.HasValue) propObj["minLength"] = propSchema.MinLength.Value;
                if (propSchema.MaxLength.HasValue) propObj["maxLength"] = propSchema.MaxLength.Value;
                if (!string.IsNullOrEmpty(propSchema.Pattern)) propObj["pattern"] = propSchema.Pattern;
                // NJsonSchema does not populate Pattern from [RegularExpression]; add it from reflection
                var propInfo = GetBodyProperty(bodyType, propName);
                if (propInfo is not null)
                {
                    var regexAttr = propInfo.GetCustomAttribute<RegularExpressionAttribute>();
                    if (regexAttr is not null)
                        propObj["pattern"] = regexAttr.Pattern;

                    // Fallback for enums: if NJsonSchema didn't emit enum but the CLR type is an enum, add an enum array
                    if (!propObj.ContainsKey("enum"))
                    {
                        var enumType = Nullable.GetUnderlyingType(propInfo.PropertyType) ?? propInfo.PropertyType;
                        if (enumType.IsEnum)
                            propObj["enum"] = Enum.GetNames(enumType);
                    }
                }

                result[camelName] = propObj;

                if (propSchema.IsRequired)
                    requiredList.Add(camelName);
            }
        }
        catch
        {
            // If NJsonSchema fails for any reason, fall back to marking the body as a generic object
            result["body"] = new Dictionary<string, object> { ["type"] = "object" };
        }

        return result;
    }

    private static string GetJsonSchemaType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        return underlying switch
        {
            _ when underlying == typeof(bool) => "boolean",
            _ when underlying == typeof(int) || underlying == typeof(long) ||
                   underlying == typeof(short) || underlying == typeof(byte) => "integer",
            _ when underlying == typeof(float) || underlying == typeof(double) ||
                   underlying == typeof(decimal) => "number",
            _ when underlying == typeof(Guid) => "string",
            _ when underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset) => "string",
            _ when underlying == typeof(DateOnly) || underlying == typeof(TimeOnly) => "string",
            _ when underlying.IsArray || (underlying.IsGenericType &&
                   underlying.GetGenericTypeDefinition() == typeof(List<>)) => "array",
            _ when underlying.IsEnum => "string",
            _ when underlying == typeof(string) => "string",
            _ => "object"
        };
    }

    private static string MapNJsonType(JsonObjectType njType) => njType switch
    {
        JsonObjectType.Boolean => "boolean",
        JsonObjectType.Integer => "integer",
        JsonObjectType.Number => "number",
        JsonObjectType.Array => "array",
        JsonObjectType.Object => "object",
        _ => "string"
    };

    /// <summary>
    /// Gets the reflected property for a body type, accounting for PascalCase vs camelCase
    /// differences between CLR properties and schema property names.
    /// </summary>
    private static PropertyInfo? GetBodyProperty(Type bodyType, string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return null;

        // Try exact name (e.g. \"Status\")
        var prop = bodyType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null)
            return prop;

        // Try PascalCase if schema used camelCase (e.g. \"status\" => \"Status\")
        if (propertyName.Length > 1)
        {
            var pascal = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
            prop = bodyType.GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance);
            if (prop is not null)
                return prop;
        }
        else
        {
            var upper = propertyName.ToUpperInvariant();
            prop = bodyType.GetProperty(upper, BindingFlags.Public | BindingFlags.Instance);
            if (prop is not null)
                return prop;
        }

        return null;
    }
}
