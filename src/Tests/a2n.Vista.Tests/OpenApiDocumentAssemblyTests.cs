using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using a2n.Vista.AspNetCore.Configuration;
using a2n.Vista.Metadata;
using a2n.Vista.OpenApi;
using a2n.Vista.OpenApi.Model;
using a2n.Vista.Ports;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Example coverage for the document-assembly finalization (spec openapi-emitter, task 5.4;
/// Requirements 8.1, 8.3, 8.4, 9.1, 9.2, 3.6, 4.5). Asserts <c>openapi</c>/<c>info</c> population and
/// <c>info.version</c> defaulting to the emitting assembly's informational version, the deterministic and
/// registration-order-independent JSON output (the key R9.2 guarantee), and the shared-schema-once
/// invariant for a component referenced by multiple views.
/// </summary>
public sealed class OpenApiDocumentAssemblyTests
{
    // ---- Representative DTOs sharing a nested POCO across two views --------------------------------

    private enum Kind
    {
        Alpha,
        Beta,
    }

    // A nested POCO shared by both views' rows, so it must be emitted exactly once.
    private sealed class SharedAddress
    {
        public string City { get; init; } = string.Empty;

        public string? Region { get; init; }
    }

    private sealed class WidgetRow
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public Kind Kind { get; init; }

        public SharedAddress Address { get; init; } = new();
    }

    private sealed class GadgetRow
    {
        public Guid Id { get; init; }

        public SharedAddress Location { get; init; } = new();
    }

    private sealed record GadgetCrud(Guid Id, string Label);

    // Mirrors the serialization seam: web defaults (camelCase) + JsonStringEnumConverter.
    private static JsonSerializerOptions SeamOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static ViewMetadata WidgetView() => new(
        Name: "widgets",
        Route: "/api/views/widgets",
        QueryType: typeof(WidgetRow),
        CrudType: null,
        CrudEntityType: null,
        Fields: Array.Empty<FieldMetadata>(),
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: true);

    private static ViewMetadata GadgetView() => new(
        Name: "gadgets",
        Route: "/api/views/gadgets",
        QueryType: typeof(GadgetRow),
        CrudType: typeof(GadgetCrud),
        CrudEntityType: typeof(GadgetCrud),
        Fields: Array.Empty<FieldMetadata>(),
        Authorization: null,
        Limits: HardLimits.Default,
        IsReadOnly: false);

    private static IViewRegistry RegistryOf(params ViewMetadata[] views)
    {
        var registry = new ViewRegistry();
        foreach (var view in views)
        {
            registry.Add(view);
        }

        return registry;
    }

    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    private static VistaOpenApiDocumentBuilder Builder(IViewRegistry registry, VistaOpenApiOptions? options = null) =>
        new(registry, SeamOptions(), new VistaEndpointOptions(), options ?? new VistaOpenApiOptions());

    // ---- openapi + info population (R8.1) ---------------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Openapi_And_Info_Title_Come_From_Options()
    {
        var options = new VistaOpenApiOptions { DocumentTitle = "My API", OpenApiVersion = "3.0.4" };
        var document = Builder(RegistryOf(WidgetView()), options).Build();

        await Assert.That(document.Openapi).IsEqualTo("3.0.4");
        await Assert.That(document.Info.Title).IsEqualTo("My API");
        await Assert.That(string.IsNullOrWhiteSpace(document.Info.Version)).IsFalse();
    }

    // ---- info.version defaulting (R8.4) -----------------------------------------------------------

    // The expected default: the emitting assembly's informational version with any +build-metadata stripped.
    private static string ExpectedDefaultVersion()
    {
        var assembly = typeof(VistaOpenApiDocumentBuilder).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = assembly.GetName().Version?.ToString();
        return string.IsNullOrWhiteSpace(version) ? "1.0.0" : version;
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Info_Version_Defaults_To_The_Emitting_Assembly_Informational_Version()
    {
        // DocumentVersion is null -> the builder derives info.version from the emitting assembly.
        var document = Builder(RegistryOf(WidgetView())).Build();

        var expected = ExpectedDefaultVersion();
        await Assert.That(string.IsNullOrWhiteSpace(document.Info.Version)).IsFalse();
        await Assert.That(document.Info.Version).IsEqualTo(expected);

        // The default has no SemVer build-metadata suffix (a SourceLink +<sha> would have been stripped).
        await Assert.That(document.Info.Version.Contains('+', StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Info_Version_Uses_The_Explicit_Option_When_Supplied()
    {
        var options = new VistaOpenApiOptions { DocumentVersion = "2.5.0" };
        var document = Builder(RegistryOf(WidgetView()), options).Build();

        await Assert.That(document.Info.Version).IsEqualTo("2.5.0");
    }

    // ---- Determinism / order-independence (R9.1, R9.2) --------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Same_Registry_Builds_Byte_Identical_Json()
    {
        var registry = RegistryOf(WidgetView(), GadgetView());

        var first = Builder(registry).BuildJson();
        var second = Builder(registry).BuildJson();

        await Assert.That(second).IsEqualTo(first);
    }

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Different_Registration_Order_Builds_Byte_Identical_Json()
    {
        // Same views, opposite registration order -> the emitted JSON must be byte-for-byte identical,
        // because paths/components/properties are all ordinal-ordered (Requirement 9.2).
        var forward = Builder(RegistryOf(WidgetView(), GadgetView())).BuildJson();
        var reverse = Builder(RegistryOf(GadgetView(), WidgetView())).BuildJson();

        await Assert.That(reverse).IsEqualTo(forward);
    }

    // ---- Shared-schema-once (R3.6, R4.5, R8.3) ----------------------------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task A_Shared_Nested_Poco_Referenced_By_Two_Views_Appears_Once()
    {
        // Both WidgetRow and GadgetRow embed SharedAddress; it must be a single component schema.
        var document = Builder(RegistryOf(WidgetView(), GadgetView())).Build();
        var components = document.Components!.Schemas!;

        await Assert.That(components.ContainsKey("SharedAddress")).IsTrue();

        // Count every $ref that targets SharedAddress across the whole document; the component itself is
        // still stored exactly once regardless of how many times it is referenced.
        var componentKeyCount = 0;
        foreach (var key in components.Keys)
        {
            if (string.Equals(key, "SharedAddress", StringComparison.Ordinal))
            {
                componentKeyCount++;
            }
        }

        await Assert.That(componentKeyCount).IsEqualTo(1);

        // And both rows reference the same shared component (no per-view duplication).
        var widgetAddressRef = components["WidgetRow"].Properties!["address"].Ref;
        var gadgetAddressRef = components["GadgetRow"].Properties!["location"].Ref;
        await Assert.That(widgetAddressRef).IsEqualTo("#/components/schemas/SharedAddress");
        await Assert.That(gadgetAddressRef).IsEqualTo("#/components/schemas/SharedAddress");
    }

    // ---- Referential integrity of the assembled document (R8.3) -----------------------------------

    [Test]
    [RequiresUnreferencedCode("Exercises the RUC document builder.")]
    public async Task Every_Ref_In_The_Assembled_Document_Resolves()
    {
        var document = Builder(RegistryOf(WidgetView(), GadgetView())).Build();
        var components = document.Components!.Schemas!;

        var refs = new List<string>();
        foreach (var item in document.Paths!.Values)
        {
            CollectOperationRefs(item.Get, refs);
            CollectOperationRefs(item.Post, refs);
        }

        foreach (var schema in components.Values)
        {
            CollectRefs(schema, refs);
        }

        await Assert.That(refs.Count).IsGreaterThan(0);
        foreach (var reference in refs)
        {
            var name = reference["#/components/schemas/".Length..];
            await Assert.That(components.ContainsKey(name)).IsTrue();
        }
    }

    private static void CollectOperationRefs(OpenApiOperation? operation, List<string> sink)
    {
        if (operation is null)
        {
            return;
        }

        if (operation.RequestBody?.Content is not null)
        {
            foreach (var media in operation.RequestBody.Content.Values)
            {
                CollectRefs(media.Schema, sink);
            }
        }

        if (operation.Responses is not null)
        {
            foreach (var response in operation.Responses.Values)
            {
                if (response.Content is null)
                {
                    continue;
                }

                foreach (var media in response.Content.Values)
                {
                    CollectRefs(media.Schema, sink);
                }
            }
        }
    }

    private static void CollectRefs(OpenApiSchema? schema, List<string> sink)
    {
        if (schema is null)
        {
            return;
        }

        if (schema.Ref is not null)
        {
            sink.Add(schema.Ref);
        }

        CollectRefs(schema.Items, sink);

        if (schema.Properties is not null)
        {
            foreach (var property in schema.Properties.Values)
            {
                CollectRefs(property, sink);
            }
        }

        if (schema.OneOf is not null)
        {
            foreach (var alternative in schema.OneOf)
            {
                CollectRefs(alternative, sink);
            }
        }
    }
}
