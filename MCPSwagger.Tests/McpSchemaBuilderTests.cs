using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FluentAssertions;
using SwaggerMcp.Discovery;
using SwaggerMcp.Schema;
using Xunit;

namespace SwaggerMcp.Tests;

public sealed class McpSchemaBuilderTests
{
    private readonly McpSchemaBuilder _builder = new();

    [Fact]
    public void BuildSchema_RouteAndQueryParams_ProducesCorrectSchema()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "get_item",
            HttpMethod = "GET",
            RelativeUrl = "items/{id}",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters =
            [
                new McpParameterDescriptor { Name = "id", ParameterType = typeof(int), IsRequired = true }
            ],
            QueryParameters =
            [
                new McpParameterDescriptor { Name = "include_deleted", ParameterType = typeof(bool), IsRequired = false }
            ]
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;

        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").TryGetProperty("id", out var idProp).Should().BeTrue();
        idProp.GetProperty("type").GetString().Should().Be("integer");

        schema.GetProperty("properties").TryGetProperty("include_deleted", out _).Should().BeTrue();

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.Should().Contain("id");
        required.Should().NotContain("include_deleted");
    }

    [Fact]
    public void BuildSchema_BodyType_ExpandsPropertiesInline()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "create_item",
            HttpMethod = "POST",
            RelativeUrl = "items",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters = [],
            QueryParameters = [],
            Body = new McpBodyDescriptor
            {
                BodyType = typeof(CreateItemRequest),
                ParameterName = "request"
            }
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;
        var props = schema.GetProperty("properties");

        props.TryGetProperty("name", out _).Should().BeTrue();
        props.TryGetProperty("count", out var countProp).Should().BeTrue();
        countProp.GetProperty("type").GetString().Should().Be("integer");

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.Should().Contain("name");
    }

    [Fact]
    public void BuildSchema_NullableType_IncludesNullInTypeArray()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "search",
            HttpMethod = "GET",
            RelativeUrl = "search",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters = [],
            QueryParameters =
            [
                new McpParameterDescriptor { Name = "count", ParameterType = typeof(int?), IsRequired = false }
            ]
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;
        var countType = schema.GetProperty("properties").GetProperty("count").GetProperty("type");
        countType.ValueKind.Should().Be(JsonValueKind.Array);
        countType.EnumerateArray().Select(e => e.GetString()).Should().Contain(["integer", "null"]);
    }

    [Fact]
    public void BuildSchema_BodyAndRouteParams_ProducesCombinedRequiredSchema()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "update_status",
            HttpMethod = "PATCH",
            RelativeUrl = "items/{id}",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters =
            [
                new McpParameterDescriptor { Name = "id", ParameterType = typeof(int), IsRequired = true }
            ],
            QueryParameters = [],
            Body = new McpBodyDescriptor
            {
                BodyType = typeof(UpdateStatusBody),
                ParameterName = "request"
            }
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;
        var props = schema.GetProperty("properties");

        props.TryGetProperty("id", out var idProp).Should().BeTrue();
        idProp.GetProperty("type").GetString().Should().Be("integer");
        props.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetProperty("type").GetString().Should().Be("string");

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        required.Should().Contain("id");
        required.Should().Contain("status");
    }

    [Fact]
    public void BuildSchema_NestedComplexBodyProperty_IsObjectType()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "submit_nested",
            HttpMethod = "POST",
            RelativeUrl = "nested",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters = [],
            QueryParameters = [],
            Body = new McpBodyDescriptor
            {
                BodyType = typeof(NestedBodyRequest),
                ParameterName = "request"
            }
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;
        var addressProp = schema.GetProperty("properties").GetProperty("address");
        addressProp.GetProperty("type").GetString().Should().Be("object");
    }

    [Fact]
    public void BuildSchema_CollectionBodyProperties_AreArrays()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "submit_collections",
            HttpMethod = "POST",
            RelativeUrl = "collections",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters = [],
            QueryParameters = [],
            Body = new McpBodyDescriptor
            {
                BodyType = typeof(CollectionBodyRequest),
                ParameterName = "request"
            }
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;
        var props = schema.GetProperty("properties");

        props.GetProperty("tags").GetProperty("type").GetString().Should().Be("array");
        props.GetProperty("aliases").GetProperty("type").GetString().Should().Be("array");
    }

    [Fact]
    public void BuildSchema_EnumProperty_ContainsEnumArray()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "set_state",
            HttpMethod = "POST",
            RelativeUrl = "state",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters = [],
            QueryParameters = [],
            Body = new McpBodyDescriptor
            {
                BodyType = typeof(EnumBodyRequest),
                ParameterName = "request"
            }
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;
        var stateProp = schema.GetProperty("properties").GetProperty("state");

        stateProp.TryGetProperty("enum", out var enumValues).Should().BeTrue();
        enumValues.ValueKind.Should().Be(JsonValueKind.Array);
        enumValues.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildSchema_EmptyBodyType_ProducesObjectWithNoProperties()
    {
        var descriptor = new McpToolDescriptor
        {
            Name = "empty_body",
            HttpMethod = "POST",
            RelativeUrl = "empty",
            ActionDescriptor = null!,
            ApiDescription = null!,
            RouteParameters = [],
            QueryParameters = [],
            Body = new McpBodyDescriptor
            {
                BodyType = typeof(EmptyBodyRequest),
                ParameterName = "request"
            }
        };

        var json = _builder.BuildSchema(descriptor);
        var schema = JsonDocument.Parse(json).RootElement;
        var props = schema.GetProperty("properties").EnumerateObject().ToList();

        props.Should().BeEmpty();
        schema.TryGetProperty("required", out _).Should().BeFalse();
    }

    private sealed class CreateItemRequest
    {
        [Required] public string Name { get; set; } = default!;
        [Range(1, 100)] public int Count { get; set; }
    }

    private sealed class UpdateStatusBody
    {
        [Required] public string Status { get; set; } = default!;
    }

    private sealed class NestedBodyRequest
    {
        [Required] public Address Address { get; set; } = new();
    }

    private sealed class Address
    {
        [Required] public string Street { get; set; } = default!;
        public string? City { get; set; }
    }

    private sealed class CollectionBodyRequest
    {
        public List<string> Tags { get; set; } = [];
        public string[] Aliases { get; set; } = [];
    }

    private sealed class EnumBodyRequest
    {
        public FulfillmentState State { get; set; }
    }

    private enum FulfillmentState
    {
        Pending,
        Shipped,
        Cancelled
    }

    private sealed class EmptyBodyRequest;
}
