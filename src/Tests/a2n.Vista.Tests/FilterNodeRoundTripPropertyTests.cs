// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Property-based test for the reflection-free polymorphic FilterNode converter
// (spec source-generator-http-surface, task 7.4; Decision Log D124). Task 7.1 replaced the write-side
// reflection call (JsonSerializer.Serialize(writer, leaf.Value, options)) in FilterNodeJsonConverter with
// a manual WriteValue switch over Utf8JsonWriter primitives, so the polymorphic FilterNode converter now
// carries no reflection and is compatible with a source-generated JsonSerializerContext. This test proves
// that the reflection-free converter is also structure-preserving: any FilterNode tree survives a
// serialize/deserialize round-trip through VistaJson.Options unchanged.
//
// Feature: source-generator-http-surface, Property 2: Polymorphic FilterNode round-trip is reflection-free
// and structure-preserving
//
// Validates: Requirements 5.1
//
// Strategy: a CsCheck generator builds diverse FilterNode trees with bounded depth (to avoid exponential
// explosion) — arbitrary nesting of FilterAnd/FilterOr/FilterNot around FilterLeaf nodes whose values are
// drawn from the NEUTRAL value space the converter's ReadValue produces (string / long / double / bool /
// null / List<object?> of those). Numbers are constrained so equivalence is well defined: longs always
// round-trip as long, and doubles are forced non-integer so they never collapse to a long on read
// (a JSON integer is read back as long, not double). Each generated tree is serialized then deserialized
// through VistaJson.Options and asserted structurally equivalent (same node kinds, fields, operators,
// values, and nesting). Minimum 100 iterations. PBT library: CsCheck.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Contracts;
using CsCheck;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property 2 — the polymorphic <see cref="FilterNodeJsonConverter"/> is reflection-free and
/// structure-preserving (task 7.4, Requirement 5.1). See the file header for the full strategy.
/// </summary>
public sealed class FilterNodeRoundTripPropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 200;

    /// <summary>Maximum tree nesting depth — bounds the generator to keep trees finite and cheap.</summary>
    private const int MaxTreeDepth = 4;

    /// <summary>Maximum nesting depth of a list-valued leaf (lists of lists of neutral values).</summary>
    private const int MaxValueDepth = 2;

    /// <summary>
    /// The single-flag, non-<see cref="FilterOperator.None"/> operators the converter accepts. The
    /// converter rejects <c>None</c> and only single flags round-trip through <c>Op.ToString()</c> /
    /// <c>Enum.TryParse</c> unambiguously, so the composite groupings (<c>Range</c>/<c>Text</c>) are
    /// excluded from generation.
    /// </summary>
    private static readonly FilterOperator[] Operators =
    {
        FilterOperator.Equals,
        FilterOperator.NotEquals,
        FilterOperator.GreaterThan,
        FilterOperator.GreaterThanOrEqual,
        FilterOperator.LessThan,
        FilterOperator.LessThanOrEqual,
        FilterOperator.Contains,
        FilterOperator.StartsWith,
        FilterOperator.EndsWith,
        FilterOperator.In,
        FilterOperator.Between,
        FilterOperator.IsNull,
    };

    /// <summary>
    /// A safe alphabet for generated field names and string values: all single BMP code units (no lone
    /// surrogates), including JSON-significant characters (quote, backslash, slash, whitespace) and a few
    /// non-ASCII letters, so escaping is exercised while every string stays valid UTF-16.
    /// </summary>
    private static readonly char[] TextChars =
        "abcXYZ019 .-_\"\\/\n\téÜ中".ToCharArray();

    // -- Value generators (the neutral value space produced by ReadValue) -------------------------------

    private static readonly Gen<object?> GenNull = Gen.Const((object?)null);

    private static readonly Gen<object?> GenBoolValue = Gen.Bool.Select(b => (object?)b);

    // Any long fits int64 and round-trips as long.
    private static readonly Gen<object?> GenLongValue = Gen.Long.Select(l => (object?)l);

    // Non-integer, finite doubles: a JSON integer is read back as long (TryGetInt64), so a double that is
    // integer-valued would not round-trip as a double. Forcing a fractional part keeps equivalence well
    // defined; the magnitude is bounded so the +0.5 nudge is exactly representable.
    private static readonly Gen<object?> GenDoubleValue =
        from n in Gen.Int[-1_000_000, 1_000_000]
        from d in Gen.Int[2, 1_000]
        let value = (double)n / d
        select (object?)(value == Math.Truncate(value) ? value + 0.5 : value);

    private static readonly Gen<string> GenText =
        from len in Gen.Int[0, 10]
        from indices in Gen.Int[0, TextChars.Length - 1].Array[len]
        select new string(Array.ConvertAll(indices, i => TextChars[i]));

    private static readonly Gen<object?> GenStringValue = GenText.Select(s => (object?)s);

    /// <summary>
    /// A neutral leaf value: null / bool / long / double / string at any depth, plus (while depth remains)
    /// a possibly-empty list of neutral values, mirroring the recursive shape <c>ReadValue</c> produces.
    /// </summary>
    private static Gen<object?> GenValue(int depth)
    {
        var scalar =
            from choice in Gen.Int[0, 4]
            from v in choice switch
            {
                0 => GenNull,
                1 => GenBoolValue,
                2 => GenLongValue,
                3 => GenDoubleValue,
                _ => GenStringValue,
            }
            select v;

        if (depth <= 0)
        {
            return scalar;
        }

        return
            from choice in Gen.Int[0, 5]
            from v in choice == 5
                ? GenValue(depth - 1).List[0, 3].Select(items => (object?)items)
                : scalar
            select v;
    }

    // -- Tree generator (bounded depth) -----------------------------------------------------------------

    private static readonly Gen<FilterNode> GenLeaf =
        from field in GenText
        from opIndex in Gen.Int[0, Operators.Length - 1]
        from value in GenValue(MaxValueDepth)
        select (FilterNode)new FilterLeaf(field, Operators[opIndex], value);

    /// <summary>
    /// A FilterNode tree of at most <paramref name="depth"/> nesting levels. Leaves are weighted higher
    /// than composites (choices 0..2 of 0..6) so trees stay bounded while still covering And/Or/Not.
    /// </summary>
    private static Gen<FilterNode> GenNode(int depth)
    {
        if (depth <= 0)
        {
            return GenLeaf;
        }

        return
            from choice in Gen.Int[0, 6]
            from node in choice switch
            {
                0 or 1 or 2 => GenLeaf,
                3 => GenNode(depth - 1).List[1, 3].Select(cs => (FilterNode)new FilterAnd(cs)),
                4 => GenNode(depth - 1).List[1, 3].Select(cs => (FilterNode)new FilterOr(cs)),
                _ => GenNode(depth - 1).Select(c => (FilterNode)new FilterNot(c)),
            }
            select node;
    }

    // -- The property -----------------------------------------------------------------------------------

    [Test]
    public async Task FilterNode_Tree_RoundTrips_Structure_Preservingly()
    {
        // The reflection-free converter is compatible with a source-generated context: the shipped
        // Static_Envelope_Context resolves FilterNode without the reflection fallback (R5.1).
        await Assert.That(VistaStaticJsonContext.Default.GetTypeInfo(typeof(FilterNode))).IsNotNull();

        GenNode(MaxTreeDepth).Sample(
            tree =>
            {
                var json = JsonSerializer.Serialize(tree, VistaJson.Options);
                var back = JsonSerializer.Deserialize<FilterNode>(json, VistaJson.Options);

                if (back is null)
                {
                    throw new Exception($"Round-trip produced a null node. JSON: {json}");
                }

                if (!NodesEqual(tree, back))
                {
                    throw new Exception(
                        "Round-trip changed the tree structure.\n" +
                        $"  original:     {Describe(tree)}\n" +
                        $"  deserialized: {Describe(back)}\n" +
                        $"  json:         {json}");
                }
            },
            iter: Iterations);
    }

    // -- Structural comparison --------------------------------------------------------------------------

    private static bool NodesEqual(FilterNode a, FilterNode b) => (a, b) switch
    {
        (FilterLeaf la, FilterLeaf lb) =>
            string.Equals(la.Field, lb.Field, StringComparison.Ordinal)
            && la.Op == lb.Op
            && ValuesEqual(la.Value, lb.Value),
        (FilterAnd aa, FilterAnd ab) => ChildrenEqual(aa.Children, ab.Children),
        (FilterOr oa, FilterOr ob) => ChildrenEqual(oa.Children, ob.Children),
        (FilterNot na, FilterNot nb) => NodesEqual(na.Child, nb.Child),
        _ => false,
    };

    private static bool ChildrenEqual(IReadOnlyList<FilterNode> a, IReadOnlyList<FilterNode> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!NodesEqual(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Structural equality over the neutral value space (null / bool / long / double / string /
    /// list of those). Lists compare element-wise in order; scalars compare by value and type.
    /// </summary>
    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null || b is null)
        {
            return a is null && b is null;
        }

        if (a is System.Collections.IEnumerable ea and not string
            && b is System.Collections.IEnumerable eb and not string)
        {
            var listA = new List<object?>();
            foreach (var item in ea)
            {
                listA.Add(item);
            }

            var listB = new List<object?>();
            foreach (var item in eb)
            {
                listB.Add(item);
            }

            if (listA.Count != listB.Count)
            {
                return false;
            }

            for (var i = 0; i < listA.Count; i++)
            {
                if (!ValuesEqual(listA[i], listB[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // Same runtime type by construction (long/double/bool/string); compare by value.
        return a.GetType() == b.GetType() && a.Equals(b);
    }

    // -- Diagnostics (counterexample rendering) ---------------------------------------------------------

    private static string Describe(FilterNode node) => node switch
    {
        FilterLeaf leaf => $"Leaf(field='{leaf.Field}', op={leaf.Op}, value={DescribeValue(leaf.Value)})",
        FilterAnd and => $"And[{string.Join(", ", DescribeChildren(and.Children))}]",
        FilterOr or => $"Or[{string.Join(", ", DescribeChildren(or.Children))}]",
        FilterNot not => $"Not({Describe(not.Child)})",
        _ => node.GetType().Name,
    };

    private static IEnumerable<string> DescribeChildren(IReadOnlyList<FilterNode> children)
    {
        foreach (var child in children)
        {
            yield return Describe(child);
        }
    }

    private static string DescribeValue(object? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case string s:
                return $"\"{s}\"";
            case System.Collections.IEnumerable enumerable and not string:
                var parts = new List<string>();
                foreach (var item in enumerable)
                {
                    parts.Add(DescribeValue(item));
                }

                return $"[{string.Join(", ", parts)}]";
            default:
                return $"{value} ({value.GetType().Name})";
        }
    }
}
