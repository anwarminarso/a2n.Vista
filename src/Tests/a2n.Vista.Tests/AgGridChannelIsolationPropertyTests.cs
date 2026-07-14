// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using a2n.Vista.Adapters.AgGrid;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: ag-grid-adapter, Property 5: Channel isolation (filterModel → Filter, quick filter → Search).
/// <para>
/// <see cref="AgGridAdapter.ToQuery"/> routes two independent inputs into two disjoint neutral channels of
/// the <see cref="ViewQueryRequest"/> (D111): the AG Grid <c>filterModel</c> feeds the <b>Filter</b> slot
/// (AND-ed across columns), and the quick filter feeds the <b>Search</b> slot (a <see cref="FilterOr"/> of
/// <c>Contains</c> leaves over exactly the <c>IsSearchable &amp;&amp; string</c> view fields). This property
/// pins that the two channels never leak into each other and that the adapter is permissive by design —
/// it never enforces the tri-whitelist and never silently drops a mapped leaf (R4.4, R4.5, R4.6, R4.8,
/// R4.9).
/// </para>
/// <para>
/// The generator drives a randomized <see cref="ViewMetadata"/> (fields of varied CLR type,
/// <c>IsSearchable</c>, and <c>IsFilterable</c> flags), a <c>filterModel</c> whose columns deliberately
/// include <b>non-filterable</b> view fields and even <b>non-field</b> colIds, and a quick filter that is
/// sometimes empty/whitespace. Every generated <c>filterModel</c> descriptor is a text <c>equals</c> leaf
/// carrying a marker value distinct from any quick-filter text, so the two channels' leaves are trivially
/// distinguishable: the Filter tree must contain only <c>Equals</c> leaves (one per <c>filterModel</c>
/// column, in map order) and the Search tree must contain only <c>Contains</c> leaves (one per searchable
/// string field, in field order). Cross-channel leakage — a <c>Contains</c>/quick leaf inside Filter, or a
/// <c>filterModel</c> field inside Search — would fail structural equality and the explicit isolation
/// checks.
/// </para>
/// </summary>
public sealed class AgGridChannelIsolationPropertyTests
{
    /// <summary>Minimum generated cases required for the property (tasks.md Notes: minimum 100).</summary>
    private const int Iterations = 100;

    private static readonly AgGridAdapter Adapter = new();

    // Feature: ag-grid-adapter, Property 5: Channel isolation (filterModel → Filter, quick filter → Search).
    //
    // Validates: Requirements 4.4, 4.5, 4.6, 4.8, 4.9
    [Test]
    public void ToQuery_Routes_FilterModel_To_Filter_And_QuickFilter_To_Search_Without_Leakage()
    {
        // Feature: ag-grid-adapter, Property 5: Channel isolation (filterModel → Filter, quick filter → Search).
        GenCase.Sample(AssertChannelIsolation, iter: Iterations);
    }

    private static void AssertChannelIsolation(ChannelCase testCase)
    {
        var request = new AgGridRowsRequest
        {
            StartRow = 0,
            EndRow = 100,
            SortModel = new List<AgGridSortModel>(),
            FilterModel = testCase.FilterModel,
            QuickFilter = testCase.QuickFilter,
        };

        var query = Adapter.ToQuery(request, testCase.View);

        // --- Filter channel: exactly the AND-of-columns from filterModel (R4.4, R4.8) ------------------
        var expectedFilter = ExpectedFilter(testCase.FilterModel);
        if (!AreEqual(expectedFilter, query.Filter))
        {
            throw new Exception(
                "filterModel did not map to the Filter slot as an AND-of-columns tree.\n" +
                $"  filterModel keys: [{string.Join(",", testCase.FilterModel.Keys)}]\n" +
                $"  expected Filter:  {Describe(expectedFilter)}\n" +
                $"  actual Filter:    {Describe(query.Filter)}");
        }

        // --- Search channel: exactly the FilterOr of Contains over IsSearchable && string (R4.5, R4.9) -
        var expectedSearch = ExpectedSearch(testCase.View, testCase.QuickFilter);
        if (!AreEqual(expectedSearch, query.Search))
        {
            throw new Exception(
                "quick filter did not map to the Search slot as a FilterOr of Contains over searchable strings.\n" +
                $"  quickFilter: '{testCase.QuickFilter}'\n" +
                $"  expected Search: {Describe(expectedSearch)}\n" +
                $"  actual Search:   {Describe(query.Search)}");
        }

        // --- No leakage: Filter carries only Equals leaves; Search carries only Contains leaves ---------
        // (the two channels use disjoint operators + values by construction, so any cross-contamination
        //  would surface here even if the structural checks above were loosened).
        foreach (var leaf in CollectLeaves(query.Filter))
        {
            if (leaf.Op != FilterOperator.Equals)
            {
                throw new Exception(
                    $"Filter slot leaked a non-filterModel leaf: {Describe(leaf)} (expected only Equals leaves).");
            }
        }

        foreach (var leaf in CollectLeaves(query.Search))
        {
            if (leaf.Op != FilterOperator.Contains
                || !string.Equals(leaf.Value as string, testCase.QuickFilter, StringComparison.Ordinal))
            {
                throw new Exception(
                    $"Search slot leaked a non-quick-filter leaf: {Describe(leaf)} (expected only Contains/quick).");
            }
        }

        // --- Scope is never populated by this adapter (v1) ---------------------------------------------
        if (query.Scope is not null)
        {
            throw new Exception($"Scope slot must be null; got {Describe(query.Scope)}.");
        }

        // --- No silent drop / no whitelist enforcement: every filterModel column produced a leaf, and
        //     every searchable string field produced a search leaf, regardless of IsFilterable (R4.6) ---
        var filterFields = CollectLeaves(query.Filter).Select(l => l.Field).ToHashSet(StringComparer.Ordinal);
        foreach (var colId in testCase.FilterModel.Keys)
        {
            if (!filterFields.Contains(colId))
            {
                throw new Exception(
                    $"filterModel column '{colId}' was silently dropped from the Filter slot (whitelist must not be enforced by the adapter).");
            }
        }
    }

    // -- Expected-channel builders (mirror the adapter contract, not its implementation) ----------------

    /// <summary>The Filter slot is the AND-of-columns over the filterModel (each a text <c>equals</c> leaf).</summary>
    private static FilterNode? ExpectedFilter(Dictionary<string, JsonElement> filterModel)
    {
        if (filterModel.Count == 0)
        {
            return null;
        }

        var children = new List<FilterNode>(filterModel.Count);
        foreach (var colId in filterModel.Keys)
        {
            children.Add(new FilterLeaf(colId, FilterOperator.Equals, FilterValue(colId)));
        }

        return children.Count == 1 ? children[0] : new FilterAnd(children);
    }

    /// <summary>
    /// The Search slot is a <see cref="FilterOr"/> of <c>Contains</c> leaves over exactly the
    /// <c>IsSearchable &amp;&amp; string</c> fields (in view field order), or <see langword="null"/> when the
    /// quick filter is blank or no such field exists.
    /// </summary>
    private static FilterNode? ExpectedSearch(ViewMetadata view, string quickFilter)
    {
        if (string.IsNullOrWhiteSpace(quickFilter))
        {
            return null;
        }

        var leaves = new List<FilterNode>();
        foreach (var field in view.Fields)
        {
            if (field.IsSearchable && field.ClrType == typeof(string))
            {
                leaves.Add(new FilterLeaf(field.Name, FilterOperator.Contains, quickFilter));
            }
        }

        return leaves.Count switch
        {
            0 => null,
            1 => leaves[0],
            _ => new FilterOr(leaves),
        };
    }

    // -- Generators -------------------------------------------------------------------------------------

    /// <summary>Candidate view field names; a generated view includes an arbitrary non-empty subset.</summary>
    private static readonly string[] FieldNames = { "Id", "Name", "Price", "Category", "CreatedOn", "Sku", "Notes" };

    /// <summary>CLR types a generated field can take (only <see cref="string"/> is search-eligible).</summary>
    private static readonly Type[] Types = { typeof(string), typeof(int), typeof(decimal), typeof(DateTime) };

    /// <summary>filterModel colIds — deliberately a superset of the field names (Ghost/Phantom are non-fields).</summary>
    private static readonly string[] ColIdPool =
        { "Id", "Name", "Price", "Category", "CreatedOn", "Sku", "Notes", "Ghost", "Phantom" };

    /// <summary>Quick-filter options: blank/whitespace models "no search"; the rest are distinct search terms.</summary>
    private static readonly string[] QuickOptions = { "", "   ", "search", "term xyz", "naïve café", "a\"b\"c" };

    private sealed record ChannelCase(ViewMetadata View, Dictionary<string, JsonElement> FilterModel, string QuickFilter);

    private static readonly Gen<ChannelCase> GenCase =
        from includeMask in Gen.Int[0, (1 << FieldNames.Length) - 1]
        from typeCodes in Gen.Int[0, Types.Length - 1].Array[FieldNames.Length]
        from searchMask in Gen.Int[0, (1 << FieldNames.Length) - 1]
        from filterMask in Gen.Int[0, (1 << FieldNames.Length) - 1]
        from colIds in Gen.Int[0, ColIdPool.Length - 1].List[0, 4]
        from quickIdx in Gen.Int[0, QuickOptions.Length - 1]
        select new ChannelCase(
            BuildView(includeMask, typeCodes, searchMask, filterMask),
            BuildFilterModel(colIds),
            QuickOptions[quickIdx]);

    private static ViewMetadata BuildView(int includeMask, int[] typeCodes, int searchMask, int filterMask)
    {
        // Force at least one field so the view is well-formed (KeyFields must reference a real field).
        var mask = includeMask == 0 ? 1 : includeMask;

        var fields = new List<FieldMetadata>();
        for (var i = 0; i < FieldNames.Length; i++)
        {
            if ((mask & (1 << i)) == 0)
            {
                continue;
            }

            fields.Add(FieldMetadata.Create(
                name: FieldNames[i],
                clrType: Types[typeCodes[i]],
                isFilterable: (filterMask & (1 << i)) != 0,
                isSortable: true,
                // IsSearchable may be set on non-string fields on purpose: the adapter must still exclude
                // them from the Search channel (only IsSearchable && string participates, R4.5).
                isSearchable: (searchMask & (1 << i)) != 0,
                allowedOperators: FilterOperator.Text | FilterOperator.Equals | FilterOperator.In));
        }

        return new ViewMetadata(
            Name: "channel-isolation",
            Route: "/test/channel-isolation",
            QueryType: typeof(object),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: new HardLimits(HardLimits.DefaultMaxPageSize, HardLimits.DefaultMaxExportRows),
            IsReadOnly: true)
        {
            KeyFields = new[] { fields[0].Name },
        };
    }

    private static Dictionary<string, JsonElement> BuildFilterModel(IReadOnlyList<int> colIds)
    {
        var model = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var idx in colIds)
        {
            var colId = ColIdPool[idx];
            // A plain text `equals` descriptor carrying a per-column marker value distinct from any quick
            // filter, so a Filter/Search cross-leak is structurally detectable.
            var json = $"{{\"filterType\":\"text\",\"type\":\"equals\",\"filter\":{JsonSerializer.Serialize(FilterValue(colId))}}}";
            model[colId] = ParseElement(json);
        }

        return model;
    }

    /// <summary>The marker filter value for a column — never collides with a quick-filter term.</summary>
    private static string FilterValue(string colId) => "F:" + colId;

    // -- Structural helpers (record equality is reference-based over child/value lists) -----------------

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

        return a.Equals(b);
    }

    /// <summary>Flattens all <see cref="FilterLeaf"/> nodes reachable from a channel root.</summary>
    private static IEnumerable<FilterLeaf> CollectLeaves(FilterNode? node)
    {
        switch (node)
        {
            case null:
                yield break;
            case FilterLeaf leaf:
                yield return leaf;
                break;
            case FilterNot not:
                foreach (var l in CollectLeaves(not.Child))
                {
                    yield return l;
                }

                break;
            case FilterAnd and:
                foreach (var child in and.Children)
                {
                    foreach (var l in CollectLeaves(child))
                    {
                        yield return l;
                    }
                }

                break;
            case FilterOr or:
                foreach (var child in or.Children)
                {
                    foreach (var l in CollectLeaves(child))
                    {
                        yield return l;
                    }
                }

                break;
        }
    }

    private static string Describe(FilterNode? node) => node switch
    {
        null => "<null>",
        FilterLeaf l => $"Leaf({l.Field},{l.Op},{l.Value ?? "null"})",
        FilterNot n => $"Not({Describe(n.Child)})",
        FilterAnd a => $"And[{string.Join(",", a.Children.Select(Describe))}]",
        FilterOr o => $"Or[{string.Join(",", o.Children.Select(Describe))}]",
        _ => node.ToString() ?? "<?>",
    };

    private static JsonElement ParseElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
