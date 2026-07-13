using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.OpenApi.Schema;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Example coverage for the RUC <see cref="DtoSchemaGenerator"/> (spec openapi-emitter, task 3.1;
/// Requirements 4.1–4.6, 13.3): property naming under the seam policy, enum-as-string, nullable value/
/// reference members, BCL scalar type/format mapping, collections, nested-POCO <c>$ref</c> with component
/// collection, and unresolvable members degrading to a permissive schema plus a non-fatal notice. The
/// instance-against-schema parity property test arrives later with task 8.4.
/// </summary>
public sealed class DtoSchemaGeneratorTests
{
    private enum Color
    {
        Red,
        Green,
        Blue,
    }

    private sealed class Nested
    {
        public string Label { get; init; } = string.Empty;
    }

    private sealed class Row
    {
        public int Id { get; init; }

        public long Ticks { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Nickname { get; init; }

        public int? Age { get; init; }

        public Color Color { get; init; }

        public Color? MaybeColor { get; init; }

        public Guid Key { get; init; }

        public DateTime CreatedAt { get; init; }

        public byte[] Blob { get; init; } = Array.Empty<byte>();

        public List<string> Tags { get; init; } = new();

        public Nested Detail { get; init; } = new();
    }

    private sealed class Bespoke
    {
        public int Ordinary { get; init; }

        // IntPtr is a primitive with no conventional OpenAPI mapping -> permissive schema + notice.
        public IntPtr Handle { get; init; }
    }

    // Mirrors the serialization seam: web defaults (camelCase) + JsonStringEnumConverter.
    private static JsonSerializerOptions SeamOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    private static (OpenApiSchema Root, DtoSchemaGenerator Generator) GenerateRow()
    {
        var generator = new DtoSchemaGenerator(SeamOptions());
        var root = generator.GenerateSchema(typeof(Row));
        return (root, generator);
    }

    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    private static OpenApiSchema RowComponent(DtoSchemaGenerator generator) =>
        generator.Components[nameof(Row)];

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    public async Task Root_Poco_Is_Emitted_As_Component_And_Referenced()
    {
        var (root, generator) = GenerateRow();

        await Assert.That(root.Ref).IsEqualTo("#/components/schemas/Row");
        await Assert.That(generator.Components.ContainsKey(nameof(Row))).IsTrue();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    public async Task Property_Names_Use_The_Seam_CamelCase_Policy()
    {
        var (_, generator) = GenerateRow();
        var props = RowComponent(generator).Properties!;

        await Assert.That(props.ContainsKey("id")).IsTrue();
        await Assert.That(props.ContainsKey("createdAt")).IsTrue();
        await Assert.That(props.ContainsKey("maybeColor")).IsTrue();
        // The PascalCase CLR names must not survive.
        await Assert.That(props.ContainsKey("Id")).IsFalse();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    public async Task Enum_Member_Is_String_With_Member_Names()
    {
        var (_, generator) = GenerateRow();
        var color = RowComponent(generator).Properties!["color"];

        await Assert.That(color.Type).IsEqualTo("string");
        await Assert.That(color.Enum).IsNotNull();
        await Assert.That(color.Enum!).Contains("Red");
        await Assert.That(color.Enum!).Contains("Green");
        await Assert.That(color.Enum!).Contains("Blue");
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    public async Task Nullable_Value_And_Reference_Members_Are_Marked_Nullable()
    {
        var (_, generator) = GenerateRow();
        var props = RowComponent(generator).Properties!;

        // Nullable<int> and a nullable enum -> nullable true.
        await Assert.That(props["age"].Nullable).IsEqualTo(true);
        await Assert.That(props["maybeColor"].Nullable).IsEqualTo(true);
        // Nullable reference member -> nullable true.
        await Assert.That(props["nickname"].Nullable).IsEqualTo(true);
        // Non-nullable members are not decorated.
        await Assert.That(props["id"].Nullable).IsNull();
        await Assert.That(props["name"].Nullable).IsNull();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    public async Task Bcl_Scalars_Map_To_Conventional_Type_And_Format()
    {
        var (_, generator) = GenerateRow();
        var props = RowComponent(generator).Properties!;

        await Assert.That(props["id"].Type).IsEqualTo("integer");
        await Assert.That(props["id"].Format).IsEqualTo("int32");

        await Assert.That(props["ticks"].Type).IsEqualTo("integer");
        await Assert.That(props["ticks"].Format).IsEqualTo("int64");

        await Assert.That(props["key"].Type).IsEqualTo("string");
        await Assert.That(props["key"].Format).IsEqualTo("uuid");

        await Assert.That(props["createdAt"].Type).IsEqualTo("string");
        await Assert.That(props["createdAt"].Format).IsEqualTo("date-time");

        // byte[] serializes as a base64 string, not an array.
        await Assert.That(props["blob"].Type).IsEqualTo("string");
        await Assert.That(props["blob"].Format).IsEqualTo("byte");
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    public async Task Collections_Map_To_Array_With_Items()
    {
        var (_, generator) = GenerateRow();
        var tags = RowComponent(generator).Properties!["tags"];

        await Assert.That(tags.Type).IsEqualTo("array");
        await Assert.That(tags.Items).IsNotNull();
        await Assert.That(tags.Items!.Type).IsEqualTo("string");
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    public async Task Nested_Poco_Emits_Its_Own_Component_And_A_Ref()
    {
        var (_, generator) = GenerateRow();
        var detail = RowComponent(generator).Properties!["detail"];

        await Assert.That(detail.Ref).IsEqualTo("#/components/schemas/Nested");
        await Assert.That(generator.Components.ContainsKey(nameof(Nested))).IsTrue();

        var nested = generator.Components[nameof(Nested)];
        await Assert.That(nested.Type).IsEqualTo("object");
        await Assert.That(nested.Properties!.ContainsKey("label")).IsTrue();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC DtoSchemaGenerator.")]
    public async Task Unresolvable_Member_Yields_Permissive_Schema_And_Notice_Without_Throwing()
    {
        var generator = new DtoSchemaGenerator(SeamOptions());

        var root = generator.GenerateSchema(typeof(Bespoke));
        var component = generator.Components[nameof(Bespoke)];
        var handle = component.Properties!["handle"];

        // The bespoke member is present (never omitted) and is the permissive empty schema.
        await Assert.That(component.Properties!.ContainsKey("handle")).IsTrue();
        await Assert.That(handle.Type).IsNull();
        await Assert.That(handle.Ref).IsNull();
        await Assert.That(handle.Properties).IsNull();
        await Assert.That(handle.Enum).IsNull();

        // The ordinary member is still described precisely.
        await Assert.That(component.Properties!["ordinary"].Type).IsEqualTo("integer");

        // A non-fatal notice was recorded and the build did not throw.
        await Assert.That(root.Ref).IsEqualTo("#/components/schemas/Bespoke");
        await Assert.That(generator.Notices.Count).IsGreaterThan(0);
    }
}
