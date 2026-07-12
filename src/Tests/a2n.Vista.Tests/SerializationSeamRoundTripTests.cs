using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Execution;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Contracts;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Unit examples for the AOT-clean serialization seam (spec source-generator-http-surface, task 7.6;
/// Decision Log D124; Requirements 5.1 and 5.5). These assert three things:
/// <list type="number">
///   <item><description>each type covered by the shipped <see cref="VistaStaticJsonContext"/>
///   (the <c>Static_Envelope_Context</c>) round-trips through <see cref="VistaJson.Options"/> and resolves
///   its <see cref="JsonTypeInfo"/> from the source-generated context, not the reflection fallback
///   (R5.1);</description></item>
///   <item><description>a type covered by no chained context still (de)serializes correctly through the
///   reflection fallback resolver (R5.5);</description></item>
///   <item><description>opting the reflection fallback out removes the reflection branch, so an uncovered
///   type no longer resolves (R5.5).</description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared-static isolation.</b> <see cref="VistaJson.Options"/> is a process-wide static whose
/// resolver chain freezes on first use, and <see cref="VistaJson.DisableReflectionFallback"/> mutates it
/// irreversibly (there is no re-enable API). Calling it here would strip the reflection resolver every
/// other test in the process relies on. The opt-out assertion therefore runs against a <b>fresh</b>
/// <see cref="JsonSerializerOptions"/> that mirrors the seam chain <i>without</i> the reflection fallback,
/// exactly the shape <see cref="VistaJson.DisableReflectionFallback"/> produces — proving the behavior
/// without corrupting the shared static.
/// </para>
/// </remarks>
public sealed class SerializationSeamRoundTripTests
{
    // -- R5.1: every Static_Envelope_Context type round-trips and resolves from the source-gen context ---

    [Test]
    public async Task StaticContext_Covers_ListRequestBody_And_RoundTrips()
    {
        var body = new VistaListRequestBody
        {
            Filter = new FilterLeaf("Name", FilterOperator.Contains, "abc"),
            Search = "widget",
            Scope = new FilterLeaf("City", FilterOperator.Equals, "Seattle"),
            Sort = new List<VistaSortBody> { new() { Field = "Name", Desc = true } },
            Page = 2,
            PageSize = 25,
            Format = "csv",
        };

        await AssertResolvedFromStaticContext(typeof(VistaListRequestBody));
        await AssertJsonRoundTrips(body);
    }

    [Test]
    public async Task StaticContext_Covers_DetailRequestBody_And_RoundTrips()
    {
        using var keyDoc = JsonDocument.Parse("""{"OrderId":10248,"ProductId":11}""");
        var body = new VistaDetailRequestBody { Key = keyDoc.RootElement.Clone() };

        await AssertResolvedFromStaticContext(typeof(VistaDetailRequestBody));
        await AssertJsonRoundTrips(body);
    }

    [Test]
    public async Task StaticContext_Covers_WriteRequestBody_And_RoundTrips()
    {
        using var modelDoc = JsonDocument.Parse("""{"Name":"Acme","Amount":42}""");
        using var keyDoc = JsonDocument.Parse("7");
        var body = new VistaWriteRequestBody
        {
            Model = modelDoc.RootElement.Clone(),
            Key = keyDoc.RootElement.Clone(),
        };

        await AssertResolvedFromStaticContext(typeof(VistaWriteRequestBody));
        await AssertJsonRoundTrips(body);
    }

    [Test]
    public async Task StaticContext_Covers_WriteResponse_And_RoundTrips()
    {
        var response = new VistaWriteResponse(4242L);

        await AssertResolvedFromStaticContext(typeof(VistaWriteResponse));
        await AssertJsonRoundTrips(response);
    }

    [Test]
    public async Task StaticContext_Covers_MetadataResponse_And_RoundTrips()
    {
        var response = new VistaMetadataResponse(
            Name: "Widgets",
            Route: "/api/views/widgets",
            IsReadOnly: false,
            KeyFields: new[] { "Id" },
            MaxPageSize: 100,
            MaxExportRows: 10_000,
            Fields: new[]
            {
                new VistaFieldMetadataResponse(
                    Name: "Id",
                    Label: "Id",
                    ClrType: "Int32",
                    IsFilterable: true,
                    IsSortable: true,
                    IsSearchable: false,
                    IsScopable: false,
                    IsHidden: false,
                    IsPrimaryKey: true,
                    AllowedOperators: "Equals"),
                new VistaFieldMetadataResponse(
                    Name: "Name",
                    Label: "Name",
                    ClrType: "String",
                    IsFilterable: true,
                    IsSortable: true,
                    IsSearchable: true,
                    IsScopable: false,
                    IsHidden: false,
                    IsPrimaryKey: false,
                    AllowedOperators: "Contains"),
            });

        await AssertResolvedFromStaticContext(typeof(VistaMetadataResponse));
        await AssertJsonRoundTrips(response);
    }

    [Test]
    public async Task StaticContext_Covers_FieldMetadataResponse_And_RoundTrips()
    {
        var field = new VistaFieldMetadataResponse(
            Name: "Balance",
            Label: "Account Balance",
            ClrType: "Decimal",
            IsFilterable: true,
            IsSortable: true,
            IsSearchable: false,
            IsScopable: false,
            IsHidden: false,
            IsPrimaryKey: false,
            AllowedOperators: "Equals, GreaterThan, LessThan");

        await AssertResolvedFromStaticContext(typeof(VistaFieldMetadataResponse));
        await AssertJsonRoundTrips(field);
    }

    [Test]
    public async Task StaticContext_Covers_FilterNode_Tree_And_RoundTrips()
    {
        FilterNode tree = new FilterAnd(new FilterNode[]
        {
            new FilterLeaf("Name", FilterOperator.Contains, "abc"),
            new FilterOr(new FilterNode[]
            {
                new FilterLeaf("Id", FilterOperator.In, new object[] { 1L, 2L, 3L }),
                new FilterNot(new FilterLeaf("Price", FilterOperator.GreaterThan, 10L)),
            }),
        });

        await AssertResolvedFromStaticContext(typeof(FilterNode));
        await AssertJsonRoundTrips(tree);
    }

    // -- R5.5: an uncovered type still (de)serializes through the reflection fallback -------------------

    [Test]
    public async Task UncoveredType_Falls_Back_To_Reflection_Resolver_And_RoundTrips()
    {
        // The static context covers none of the app's own DTOs, so this rides the reflection fallback.
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(UncoveredDto))).IsNull();

        var value = new UncoveredDto(7, "reflection", true);
        var json = JsonSerializer.Serialize(value, VistaJson.Options);
        var back = JsonSerializer.Deserialize<UncoveredDto>(json, VistaJson.Options);

        // A pure-value record: structural equality confirms a faithful reflection round-trip.
        await Assert.That(back).IsEqualTo(value);

        // The static source-gen context does not cover it, so the only chained resolver that could have
        // served it is the reflection fallback — yet the seam still resolves it (R5.5).
        await Assert.That(VistaJson.Options.GetTypeInfo(typeof(UncoveredDto))).IsNotNull();
    }

    // -- R5.5: opting the reflection fallback out removes the reflection branch -------------------------

    [Test]
    public async Task DisablingReflectionFallback_Removes_The_Reflection_Branch()
    {
        // Mirror the seam chain WITHOUT the reflection fallback (the shape DisableReflectionFallback
        // produces), so we do not mutate the shared VistaJson.Options static other tests depend on.
        var noFallback = BuildSeamOptionsWithoutReflectionFallback();

        // A fully source-gen-covered fixed-envelope type (all scalar members) still resolves from the
        // static context and serializes with no reflection branch present, byte-for-byte as the seam.
        await Assert.That(noFallback.GetTypeInfo(typeof(VistaFieldMetadataResponse))).IsNotNull();
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(VistaFieldMetadataResponse))).IsNotNull();

        var field = new VistaFieldMetadataResponse(
            Name: "Id",
            Label: "Id",
            ClrType: "Int32",
            IsFilterable: true,
            IsSortable: true,
            IsSearchable: false,
            IsScopable: false,
            IsHidden: false,
            IsPrimaryKey: true,
            AllowedOperators: "Equals");
        var json = JsonSerializer.Serialize(field, noFallback);
        await Assert.That(json).IsEqualTo(JsonSerializer.Serialize(field, VistaJson.Options));

        // An uncovered type no longer resolves: the reflection branch is gone.
        var resolver = noFallback.TypeInfoResolver;
        await Assert.That(resolver).IsNotNull();
        await Assert.That(resolver!.GetTypeInfo(typeof(UncoveredDto), noFallback)).IsNull();

        // Serialization of an uncovered type therefore throws instead of silently reflecting.
        await Assert.That(() => JsonSerializer.Serialize(new UncoveredDto(1, "x", false), noFallback))
            .Throws<NotSupportedException>();
    }

    // -- Helpers ----------------------------------------------------------------------------------------

    /// <summary>
    /// Round-trips a value through the seam and asserts stability: serialize → deserialize → serialize
    /// yields byte-for-byte identical JSON. This works uniformly for records and mutable envelope classes
    /// (which have no value equality) while still proving the (de)serialization is lossless.
    /// </summary>
    private static async Task AssertJsonRoundTrips<T>(T value)
    {
        var json1 = JsonSerializer.Serialize(value, VistaJson.Options);
        var back = JsonSerializer.Deserialize<T>(json1, VistaJson.Options);
        var json2 = JsonSerializer.Serialize(back, VistaJson.Options);

        await Assert.That(json2).IsEqualTo(json1);
    }

    /// <summary>
    /// Asserts the shipped <see cref="VistaStaticJsonContext"/> provides <see cref="JsonTypeInfo"/> for
    /// <paramref name="type"/> (i.e. the seam resolves it from the source-gen context, ahead of and
    /// independent of the reflection fallback).
    /// </summary>
    private static async Task AssertResolvedFromStaticContext(Type type)
    {
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(type)).IsNotNull();
    }

    /// <summary>
    /// Builds a fresh <see cref="JsonSerializerOptions"/> mirroring the Vista seam configuration but with
    /// only the <see cref="VistaStaticJsonContext"/> in the resolver chain — no reflection fallback —
    /// matching what <see cref="VistaJson.DisableReflectionFallback"/> leaves behind.
    /// </summary>
    private static JsonSerializerOptions BuildSeamOptionsWithoutReflectionFallback()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new FilterNodeJsonConverter());
        options.TypeInfoResolverChain.Add(VistaStaticJsonContext.Default);
        return options;
    }

    /// <summary>
    /// A pure-value DTO covered by no chained context — it exercises the reflection fallback. Members are
    /// deliberately scalar so record structural equality is a faithful round-trip check (a collection
    /// member would deserialize as <c>List&lt;T&gt;</c> and differ from an original array by type only).
    /// </summary>
    public sealed record UncoveredDto(int Number, string Text, bool Flag);
}
