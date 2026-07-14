// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter, Property 4: filterModel → FilterNode mapping fidelity (D134).
/// <para>
/// <see cref="AgGridFilterModelParser.Parse"/> is a pure translation of a polymorphic AG Grid per-column
/// filter descriptor into the neutral <see cref="FilterNode"/> vocabulary. The D134 table pins that
/// translation exactly, so this property generates the full descriptor matrix — text/number/date scalar
/// <c>type</c>s, <c>set</c>, and combined <c>AND</c>/<c>OR</c> of 2+ conditions — and asserts the produced
/// node equals the table-prescribed node <b>row-for-row</b> (R4.1, R4.2, R4.3).
/// </para>
/// <para>
/// Each generated case builds a single-column <c>filterModel</c> whose descriptor JSON is emitted alongside
/// its expected <see cref="FilterNode"/>, so the two can never drift. With one column the parser returns the
/// column node directly (no AND wrapper), so the returned node is compared to the expected node with a
/// structural deep-equality that accounts for the <see cref="FilterLeaf.Value"/> payload being either a
/// scalar or a <c>List&lt;object?&gt;</c> (record equality alone is reference-based over the child/value
/// lists, so a hand-rolled comparator is required).
/// </para>
/// </summary>
public sealed class AgGridFilterModelFidelityPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    /// <summary>The parser's <c>fields</c> map is field-type-neutral for the D134 table; an empty map suffices.</summary>
    private static readonly IReadOnlyDictionary<string, FieldMetadata> NoFields =
        new Dictionary<string, FieldMetadata>(StringComparer.Ordinal);

    /// <summary>A generated descriptor paired with the D134-prescribed node it must map to.</summary>
    private sealed record FilterCase(string Json, FilterNode Expected);

    // Feature: ag-grid-adapter, Property 4: filterModel → FilterNode mapping fidelity (D134).
    //
    // Validates: Requirements 4.1, 4.2, 4.3
    [Test]
    public void FilterModel_Column_Descriptor_Maps_To_The_D134_FilterNode()
    {
        // Feature: ag-grid-adapter, Property 4: filterModel → FilterNode mapping fidelity (D134).
        var genCase =
            from colId in Pick(Columns)
            from spec in GenCase(colId)
            select (colId, spec);

        genCase.Sample(
            tuple =>
            {
                var descriptor = ParseElement(tuple.spec.Json);
                var model = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    [tuple.colId] = descriptor,
                };

                var actual = AgGridFilterModelParser.Parse(model, NoFields);

                // A single-column filterModel yields the column node directly (no AND-of-columns wrapper),
                // so the parser output must equal the D134-prescribed node for this descriptor, row-for-row.
                if (!AreEqual(tuple.spec.Expected, actual))
                {
                    throw new Exception(
                        "AG Grid filterModel descriptor did not map to the D134 FilterNode.\n" +
                        $"  descriptor: {tuple.spec.Json}\n" +
                        $"  expected:   {Describe(tuple.spec.Expected)}\n" +
                        $"  actual:     {Describe(actual)}");
                }
            },
            iter: Iterations);
    }

    // -- Case generators (descriptor JSON + expected node, built together) ------------------------------

    private static Gen<FilterCase> GenCase(string colId) =>
        Gen.OneOf(
            GenTextScalar(colId),
            GenTextBlank(colId),
            GenNumberScalar(colId),
            GenNumberInRange(colId),
            GenNumberBlank(colId),
            GenDateScalar(colId),
            GenDateInRange(colId),
            GenDateBlank(colId),
            GenSet(colId),
            GenCombined(colId));

    // Text scalar rows (D134): value-carrying types over the `filter` property.
    private static readonly (string Type, FilterOperator Op, bool Negate)[] TextScalarTypes =
    {
        ("contains", FilterOperator.Contains, false),
        ("notContains", FilterOperator.Contains, true),   // FilterNot(Contains)
        ("startsWith", FilterOperator.StartsWith, false),
        ("endsWith", FilterOperator.EndsWith, false),
        ("equals", FilterOperator.Equals, false),
        ("notEqual", FilterOperator.NotEquals, false),
    };

    private static Gen<FilterCase> GenTextScalar(string colId) =>
        from ti in Gen.Int[0, TextScalarTypes.Length - 1]
        from v in Pick(TextValues)
        select MakeScalarCase(
            colId, "text", TextScalarTypes[ti].Type, "filter", Q(v),
            TextScalarTypes[ti].Op, TextScalarTypes[ti].Negate, v);

    private static Gen<FilterCase> GenTextBlank(string colId) =>
        from notBlank in Gen.Bool
        select MakeBlankCase(colId, "text", notBlank);

    // Number/date scalar rows (D134): the shared comparison-operator set.
    private static readonly (string Type, FilterOperator Op)[] ScalarComparisonTypes =
    {
        ("equals", FilterOperator.Equals),
        ("notEqual", FilterOperator.NotEquals),
        ("greaterThan", FilterOperator.GreaterThan),
        ("greaterThanOrEqual", FilterOperator.GreaterThanOrEqual),
        ("lessThan", FilterOperator.LessThan),
        ("lessThanOrEqual", FilterOperator.LessThanOrEqual),
    };

    private static Gen<FilterCase> GenNumberScalar(string colId) =>
        from ti in Gen.Int[0, ScalarComparisonTypes.Length - 1]
        from n in Gen.Int[0, 100_000]
        // A JSON number round-trips through the parser as its raw text (QueryBuilderParser.Value parity),
        // so the expected leaf value is the same invariant string embedded in the descriptor.
        select MakeScalarCase(
            colId, "number", ScalarComparisonTypes[ti].Type, "filter",
            n.ToString(CultureInfo.InvariantCulture),
            ScalarComparisonTypes[ti].Op, negate: false, n.ToString(CultureInfo.InvariantCulture));

    private static Gen<FilterCase> GenNumberInRange(string colId) =>
        from a in Gen.Int[0, 100_000]
        from b in Gen.Int[0, 100_000]
        select MakeRangeCase(
            colId, "number", "filter", "filterTo",
            a.ToString(CultureInfo.InvariantCulture), b.ToString(CultureInfo.InvariantCulture),
            a.ToString(CultureInfo.InvariantCulture), b.ToString(CultureInfo.InvariantCulture));

    private static Gen<FilterCase> GenNumberBlank(string colId) =>
        from notBlank in Gen.Bool
        select MakeBlankCase(colId, "number", notBlank);

    private static Gen<FilterCase> GenDateScalar(string colId) =>
        from ti in Gen.Int[0, ScalarComparisonTypes.Length - 1]
        from d in Pick(DateValues)
        select MakeScalarCase(
            colId, "date", ScalarComparisonTypes[ti].Type, "dateFrom", Q(d),
            ScalarComparisonTypes[ti].Op, negate: false, d);

    private static Gen<FilterCase> GenDateInRange(string colId) =>
        from a in Pick(DateValues)
        from b in Pick(DateValues)
        select MakeRangeCase(colId, "date", "dateFrom", "dateTo", Q(a), Q(b), a, b);

    private static Gen<FilterCase> GenDateBlank(string colId) =>
        from notBlank in Gen.Bool
        select MakeBlankCase(colId, "date", notBlank);

    private static Gen<FilterCase> GenSet(string colId) =>
        from values in Pick(TextValues).List[0, 5]
        select MakeSetCase(colId, values);

    private static Gen<FilterCase> GenCombined(string colId) =>
        from op in Pick(CombineOperators)
        from conditions in GenConditionCase(colId).List[2, 4]
        select MakeCombinedCase(colId, op, conditions);

    // Conditions of a combined filter are themselves scalar descriptors (no nested set/combined) — AG Grid's
    // combined filter joins same-column scalar conditions.
    private static Gen<FilterCase> GenConditionCase(string colId) =>
        Gen.OneOf(
            GenTextScalar(colId),
            GenTextBlank(colId),
            GenNumberScalar(colId),
            GenNumberInRange(colId),
            GenNumberBlank(colId),
            GenDateScalar(colId),
            GenDateInRange(colId),
            GenDateBlank(colId));

    // -- Case builders ----------------------------------------------------------------------------------

    private static FilterCase MakeScalarCase(
        string colId, string filterType, string type, string valueProp, string valueJson,
        FilterOperator op, bool negate, object? expectedValue)
    {
        var json = $"{{\"filterType\":\"{filterType}\",\"type\":\"{type}\",\"{valueProp}\":{valueJson}}}";
        FilterNode leaf = new FilterLeaf(colId, op, expectedValue);
        if (negate)
        {
            leaf = new FilterNot(leaf);
        }

        return new FilterCase(json, leaf);
    }

    private static FilterCase MakeRangeCase(
        string colId, string filterType, string fromProp, string toProp,
        string fromJson, string toJson, object? expectedFrom, object? expectedTo)
    {
        var json =
            $"{{\"filterType\":\"{filterType}\",\"type\":\"inRange\"," +
            $"\"{fromProp}\":{fromJson},\"{toProp}\":{toJson}}}";
        var leaf = new FilterLeaf(colId, FilterOperator.Between, new List<object?> { expectedFrom, expectedTo });
        return new FilterCase(json, leaf);
    }

    private static FilterCase MakeBlankCase(string colId, string filterType, bool notBlank)
    {
        var type = notBlank ? "notBlank" : "blank";
        var json = $"{{\"filterType\":\"{filterType}\",\"type\":\"{type}\"}}";
        FilterNode leaf = new FilterLeaf(colId, FilterOperator.IsNull, null);
        if (notBlank)
        {
            leaf = new FilterNot(leaf);
        }

        return new FilterCase(json, leaf);
    }

    private static FilterCase MakeSetCase(string colId, IReadOnlyList<string> values)
    {
        var items = new List<string>(values.Count);
        var expected = new List<object?>(values.Count);
        foreach (var v in values)
        {
            items.Add(Q(v));
            expected.Add(v);
        }

        var json = $"{{\"filterType\":\"set\",\"values\":[{string.Join(",", items)}]}}";
        return new FilterCase(json, new FilterLeaf(colId, FilterOperator.In, expected));
    }

    private static FilterCase MakeCombinedCase(string colId, string op, IReadOnlyList<FilterCase> conditions)
    {
        var condJson = new List<string>(conditions.Count);
        var expected = new List<FilterNode>(conditions.Count);
        foreach (var c in conditions)
        {
            condJson.Add(c.Json);
            expected.Add(c.Expected);
        }

        var json =
            $"{{\"filterType\":\"text\",\"operator\":\"{op}\"," +
            $"\"conditions\":[{string.Join(",", condJson)}]}}";
        FilterNode node = string.Equals(op, "OR", StringComparison.Ordinal)
            ? new FilterOr(expected)
            : new FilterAnd(expected);
        return new FilterCase(json, node);
    }

    // -- Structural deep-equality (record equality is reference-based over child/value lists) -----------

    private static bool AreEqual(FilterNode? a, FilterNode? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return (a, b) switch
        {
            (FilterLeaf la, FilterLeaf lb) =>
                string.Equals(la.Field, lb.Field, StringComparison.Ordinal)
                && la.Op == lb.Op
                && ValueEqual(la.Value, lb.Value),
            (FilterNot na, FilterNot nb) => AreEqual(na.Child, nb.Child),
            (FilterAnd aa, FilterAnd ab) => ChildrenEqual(aa.Children, ab.Children),
            (FilterOr oa, FilterOr ob) => ChildrenEqual(oa.Children, ob.Children),
            _ => false,
        };
    }

    private static bool ChildrenEqual(IReadOnlyList<FilterNode> a, IReadOnlyList<FilterNode> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!AreEqual(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValueEqual(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a is List<object?> la && b is List<object?> lb)
        {
            if (la.Count != lb.Count)
            {
                return false;
            }

            for (var i = 0; i < la.Count; i++)
            {
                if (!ValueEqual(la[i], lb[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return a.Equals(b);
    }

    // -- Diagnostics ------------------------------------------------------------------------------------

    private static string Describe(FilterNode? node) => node switch
    {
        null => "<null>",
        FilterLeaf l => $"Leaf({l.Field},{l.Op},{DescribeValue(l.Value)})",
        FilterNot n => $"Not({Describe(n.Child)})",
        FilterAnd a => $"And[{string.Join(",", a.Children.Select(Describe))}]",
        FilterOr o => $"Or[{string.Join(",", o.Children.Select(Describe))}]",
        _ => node.ToString() ?? "<?>",
    };

    private static string DescribeValue(object? v) => v switch
    {
        null => "null",
        List<object?> list => $"[{string.Join(",", list.Select(DescribeValue))}]",
        _ => v.ToString() ?? string.Empty,
    };

    // -- Helpers & data ---------------------------------------------------------------------------------

    /// <summary>JSON-encodes a string value (proper quoting/escaping) for embedding in a descriptor.</summary>
    private static string Q(string value) => JsonSerializer.Serialize(value);

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static Gen<T> Pick<T>(IReadOnlyList<T> values) =>
        Gen.Int[0, values.Count - 1].Select(i => values[i]);

    private static readonly string[] Columns = { "Id", "Name", "Price", "Category", "CreatedOn" };

    private static readonly string[] TextValues = { "abc", "Widget 1", "naïve café", "a\"b\"c", "", "x" };

    private static readonly string[] DateValues = { "2020-01-01", "2021-12-31T23:59:59", "1999-06-15" };

    private static readonly string[] CombineOperators = { "AND", "OR" };
}
