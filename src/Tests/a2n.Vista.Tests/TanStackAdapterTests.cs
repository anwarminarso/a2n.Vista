using System.Collections.Generic;
using System.Text.Json;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.TanStackTable;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;
using CsCheck;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Feature: tanstack-table-adapter — the three pure steps of <see cref="TanStackTableAdapter"/>
/// (D155–D157). Bind parses TanStack table state, ToQuery places each slice in its Vista channel,
/// and ToResponse emits <c>{ rows, rowCount, pageCount }</c>.
/// </summary>
public sealed class TanStackAdapterTests
{
    private static readonly TanStackTableAdapter Adapter = new();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoValues =
        new Dictionary<string, IReadOnlyList<string>>();

    private static AdapterRequest Raw(string? json) => new("vTanStack", NoValues, json);

    private static ViewMetadata BuildView()
    {
        var fields = new[]
        {
            FieldMetadata.Create("Id", typeof(int), isSearchable: false, allowedOperators: FilterOperator.Equals | FilterOperator.In),
            FieldMetadata.Create("Name", typeof(string), isSearchable: true, allowedOperators: FilterOperator.Text | FilterOperator.In),
            FieldMetadata.Create("Description", typeof(string), isSearchable: true, allowedOperators: FilterOperator.Text),
            FieldMetadata.Create("Price", typeof(decimal), isSearchable: false, allowedOperators: FilterOperator.Range | FilterOperator.Equals),
        };

        return new ViewMetadata(
            Name: "vTanStack",
            Route: "/test/vTanStack",
            QueryType: typeof(object),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true)
        {
            KeyFields = ["Id"],
        };
    }

    private static ViewMetadata BuildSingleSearchableView()
    {
        var fields = new[]
        {
            FieldMetadata.Create("Id", typeof(int), isSearchable: false, allowedOperators: FilterOperator.Equals),
            FieldMetadata.Create("Name", typeof(string), isSearchable: true, allowedOperators: FilterOperator.Text),
        };

        return new ViewMetadata(
            Name: "vSingle",
            Route: "/test/vSingle",
            QueryType: typeof(object),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true)
        {
            KeyFields = ["Id"],
        };
    }

    // -- identity -------------------------------------------------------------------------------------

    [Test]
    public async Task Identity_Is_TanStack_On_Its_Own_Route()
    {
        await Assert.That(Adapter.Id).IsEqualTo("tanstack");
        await Assert.That(Adapter.RouteSuffix).IsEqualTo("tanstack");
    }

    // -- bind -----------------------------------------------------------------------------------------

    [Test]
    public async Task Bind_Absent_Body_Throws()
    {
        await Assert.That(() => Adapter.BindRequest(Raw(null))).Throws<AdapterBindException>();
    }

    [Test]
    public async Task Bind_Blank_Body_Throws()
    {
        await Assert.That(() => Adapter.BindRequest(Raw("   "))).Throws<AdapterBindException>();
    }

    [Test]
    public async Task Bind_Malformed_Json_Throws()
    {
        await Assert.That(() => Adapter.BindRequest(Raw("{"))).Throws<AdapterBindException>();
    }

    [Test]
    public async Task Bind_Non_String_Global_Filter_Throws()
    {
        await Assert.That(() => Adapter.BindRequest(Raw("{\"globalFilter\":12}")))
            .Throws<AdapterBindException>();
    }

    [Test]
    public async Task Bind_Negative_PageIndex_Throws()
    {
        await Assert.That(() => Adapter.BindRequest(Raw("{\"pagination\":{\"pageIndex\":-1,\"pageSize\":10}}")))
            .Throws<AdapterBindException>();
    }

    [Test]
    public async Task Bind_Global_Filter_Over_1024_Throws_And_1024_Is_Accepted()
    {
        var ok = "{\"globalFilter\":\"" + new string('a', 1024) + "\"}";
        var bound = Adapter.BindRequest(Raw(ok));
        await Assert.That(bound.GlobalFilter.Length).IsEqualTo(1024);

        var tooLong = "{\"globalFilter\":\"" + new string('a', 1025) + "\"}";
        await Assert.That(() => Adapter.BindRequest(Raw(tooLong))).Throws<AdapterBindException>();
    }

    [Test]
    public async Task Bind_Empty_Object_Uses_TanStack_Initial_Pagination()
    {
        var bound = Adapter.BindRequest(Raw("{}"));

        await Assert.That(bound.Pagination.PageIndex).IsEqualTo(0);
        await Assert.That(bound.Pagination.PageSize).IsEqualTo(10);
        await Assert.That(bound.Sorting.Count).IsEqualTo(0);
        await Assert.That(bound.ColumnFilters.Count).IsEqualTo(0);
        await Assert.That(bound.GlobalFilter).IsEqualTo(string.Empty);
        await Assert.That(bound.ColumnVisibility).IsNull();
    }

    [Test]
    public async Task Bind_Missing_Filter_Value_Throws()
    {
        await Assert.That(() => Adapter.BindRequest(Raw("{\"columnFilters\":[{\"id\":\"Name\"}]}")))
            .Throws<AdapterBindException>();
    }

    [Test]
    public async Task Bind_CamelCase_Table_State_Round_Trips()
    {
        const string json = """
            {
              "pagination": { "pageIndex": 2, "pageSize": 25 },
              "sorting": [ { "id": "Price", "desc": true }, { "id": "nope", "desc": false } ],
              "columnFilters": [
                { "id": "Name", "value": "chai" },
                { "id": "Price", "value": { "min": 10, "max": 50 } }
              ],
              "globalFilter": "tea",
              "columnVisibility": { "Description": false, "actions": false }
            }
            """;

        var request = Adapter.BindRequest(Raw(json));
        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Page).IsEqualTo(0);
        await Assert.That(query.PageSize).IsEqualTo(25);
        await Assert.That(query.Offset).IsEqualTo(50);

        await Assert.That(query.Sort.Count).IsEqualTo(2);
        await Assert.That(query.Sort[0].Field).IsEqualTo("Price");
        await Assert.That(query.Sort[0].Descending).IsTrue();
        await Assert.That(query.Sort[1].Field).IsEqualTo("nope");
        await Assert.That(query.Sort[1].Descending).IsFalse();

        var and = (FilterAnd)query.Filter!;
        await Assert.That(and.Children.Count).IsEqualTo(2);
        var name = (FilterLeaf)and.Children[0];
        await Assert.That(name.Field).IsEqualTo("Name");
        await Assert.That(name.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(name.Value).IsEqualTo("chai");
        var price = (FilterLeaf)and.Children[1];
        await Assert.That(price.Op).IsEqualTo(FilterOperator.Between);
        var bounds = (List<object?>)price.Value!;
        await Assert.That(bounds[0]).IsEqualTo("10");
        await Assert.That(bounds[1]).IsEqualTo("50");

        var search = (FilterOr)query.Search!;
        await Assert.That(search.Children.Count).IsEqualTo(2);
        await Assert.That(((FilterLeaf)search.Children[0]).Field).IsEqualTo("Name");
        await Assert.That(((FilterLeaf)search.Children[1]).Field).IsEqualTo("Description");
        await Assert.That(query.Scope).IsNull();

        await Assert.That(query.SelectFields).IsEquivalentTo(new[] { "Id", "Name", "Price" });
    }

    // -- paging (D144 / D157) -------------------------------------------------------------------------

    [Test]
    public async Task Paging_Carries_Absolute_Offset()
    {
        var request = new TanStackTableRequest
        {
            Pagination = new TanStackPaginationState { PageIndex = 3, PageSize = 20 },
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Offset).IsEqualTo(60);
        await Assert.That(query.PageSize).IsEqualTo(20);
        await Assert.That(query.Page).IsEqualTo(0);
    }

    [Test]
    public async Task Paging_NonPositive_PageSize_Passes_Through()
    {
        var zero = Adapter.ToQuery(
            new TanStackTableRequest { Pagination = new TanStackPaginationState { PageIndex = 4, PageSize = 0 } },
            BuildView());
        var all = Adapter.ToQuery(
            new TanStackTableRequest { Pagination = new TanStackPaginationState { PageIndex = 0, PageSize = -1 } },
            BuildView());

        await Assert.That(zero.PageSize).IsEqualTo(0);
        await Assert.That(zero.Offset).IsEqualTo(0);
        await Assert.That(all.PageSize).IsEqualTo(-1);
        await Assert.That(all.Offset).IsEqualTo(0);
    }

    [Test]
    public async Task Paging_Offset_Overflow_Throws()
    {
        var request = new TanStackTableRequest
        {
            Pagination = new TanStackPaginationState { PageIndex = int.MaxValue, PageSize = 3 },
        };

        await Assert.That(() => Adapter.ToQuery(request, BuildView())).Throws<AdapterBindException>();
    }

    /// <summary>
    /// Property: for any non-negative page index and positive page size whose product fits in an int,
    /// Offset is that product, Page stays 0, and two adapter instances agree.
    /// </summary>
    [Test]
    public void Paging_And_Sort_Are_Deterministic()
    {
        var view = BuildView();
        (
            from pageIndex in Gen.Int[0, 200]
            from pageSize in Gen.Int[1, 100]
            from desc in Gen.Bool
            select (pageIndex, pageSize, desc)
        ).Sample(sample =>
        {
            var request = new TanStackTableRequest
            {
                Pagination = new TanStackPaginationState { PageIndex = sample.pageIndex, PageSize = sample.pageSize },
                Sorting = [new TanStackColumnSort { Id = "Name", Desc = sample.desc }],
            };

            var first = new TanStackTableAdapter().ToQuery(request, view);
            var second = new TanStackTableAdapter().ToQuery(request, view);

            if (first.Offset != sample.pageIndex * sample.pageSize
                || first.PageSize != sample.pageSize
                || first.Page != 0
                || first.Sort.Count != 1
                || first.Sort[0].Field != "Name"
                || first.Sort[0].Descending != sample.desc
                || second.Offset != first.Offset
                || second.PageSize != first.PageSize
                || second.Sort[0].Descending != first.Sort[0].Descending)
            {
                throw new Exception(
                    $"Paging/sort mapping diverged for pageIndex={sample.pageIndex}, pageSize={sample.pageSize}, desc={sample.desc}.");
            }
        }, iter: 100);
    }

    // -- sort (D150) ----------------------------------------------------------------------------------

    [Test]
    public async Task Sort_Unknown_Id_Is_Not_Dropped()
    {
        var request = new TanStackTableRequest
        {
            Sorting =
            [
                new TanStackColumnSort { Id = "Name", Desc = false },
                new TanStackColumnSort { Id = "name", Desc = true },
                new TanStackColumnSort { Id = "", Desc = false },
            ],
        };

        var query = Adapter.ToQuery(request, BuildView());

        await Assert.That(query.Sort.Count).IsEqualTo(3);
        await Assert.That(query.Sort[1].Field).IsEqualTo("name");
        await Assert.That(query.Sort[1].Descending).IsTrue();
        await Assert.That(query.Sort[2].Field).IsEqualTo(string.Empty);
    }

    // -- column filters (D156) ------------------------------------------------------------------------

    [Test]
    public async Task Filter_String_Is_Contains_And_Blank_Is_Skipped()
    {
        var query = QueryWithFilters(
            """{"columnFilters":[{"id":"Name","value":"chai"},{"id":"Description","value":"   "}]}""");

        var leaf = (FilterLeaf)query.Filter!;
        await Assert.That(leaf.Field).IsEqualTo("Name");
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.Contains);
        await Assert.That(leaf.Value).IsEqualTo("chai");
    }

    [Test]
    public async Task Filter_Number_Bool_And_Null_Map_To_Equals_Or_IsNull()
    {
        var number = (FilterLeaf)QueryWithFilters("""{"columnFilters":[{"id":"Price","value":20}]}""").Filter!;
        await Assert.That(number.Op).IsEqualTo(FilterOperator.Equals);
        await Assert.That(number.Value).IsEqualTo("20");

        var flag = (FilterLeaf)QueryWithFilters("""{"columnFilters":[{"id":"Name","value":false}]}""").Filter!;
        await Assert.That(flag.Op).IsEqualTo(FilterOperator.Equals);
        await Assert.That((bool)flag.Value!).IsFalse();

        var missing = (FilterLeaf)QueryWithFilters("""{"columnFilters":[{"id":"Name","value":null}]}""").Filter!;
        await Assert.That(missing.Op).IsEqualTo(FilterOperator.IsNull);
    }

    [Test]
    public async Task Filter_Array_Is_In_And_Empty_Array_Is_Skipped()
    {
        var query = QueryWithFilters("""{"columnFilters":[{"id":"Id","value":[1,2]},{"id":"Name","value":[]}]}""");

        var leaf = (FilterLeaf)query.Filter!;
        await Assert.That(leaf.Field).IsEqualTo("Id");
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.In);
        var values = (List<object?>)leaf.Value!;
        await Assert.That(values).IsEquivalentTo(new object?[] { "1", "2" });
    }

    [Test]
    public async Task Filter_Range_Object_Maps_Both_Or_One_Bound()
    {
        var both = (FilterLeaf)QueryWithFilters("""{"columnFilters":[{"id":"Price","value":{"min":10,"max":40}}]}""").Filter!;
        await Assert.That(both.Op).IsEqualTo(FilterOperator.Between);

        var minOnly = (FilterLeaf)QueryWithFilters("""{"columnFilters":[{"id":"Price","value":{"min":10}}]}""").Filter!;
        await Assert.That(minOnly.Op).IsEqualTo(FilterOperator.GreaterThanOrEqual);
        await Assert.That(minOnly.Value).IsEqualTo("10");

        var maxOnly = (FilterLeaf)QueryWithFilters("""{"columnFilters":[{"id":"Price","value":{"max":""}}]}""").Filter!;
        await Assert.That(maxOnly).IsNull();

        var openMax = (FilterLeaf)QueryWithFilters("""{"columnFilters":[{"id":"Price","value":{"min":"","max":40}}]}""").Filter!;
        await Assert.That(openMax.Op).IsEqualTo(FilterOperator.LessThanOrEqual);
    }

    [Test]
    public async Task Filter_Explicit_Ops_Map_To_The_Neutral_Operators()
    {
        var notContains = (FilterNot)QueryWithFilters(
            """{"columnFilters":[{"id":"Name","value":{"op":"notContains","value":"x"}}]}""").Filter!;
        await Assert.That(((FilterLeaf)notContains.Child).Op).IsEqualTo(FilterOperator.Contains);

        var gte = (FilterLeaf)QueryWithFilters(
            """{"columnFilters":[{"id":"Price","value":{"op":"greaterThanOrEqualTo","value":20}}]}""").Filter!;
        await Assert.That(gte.Op).IsEqualTo(FilterOperator.GreaterThanOrEqual);

        var between = (FilterLeaf)QueryWithFilters(
            """{"columnFilters":[{"id":"Price","value":{"op":"inNumberRange","value":[1,9]}}]}""").Filter!;
        await Assert.That(between.Op).IsEqualTo(FilterOperator.Between);

        var set = (FilterLeaf)QueryWithFilters(
            """{"columnFilters":[{"id":"Id","value":{"op":"arrIncludesSome","value":["a","b"]}}]}""").Filter!;
        await Assert.That(set.Op).IsEqualTo(FilterOperator.In);

        var blank = (FilterLeaf)QueryWithFilters(
            """{"columnFilters":[{"id":"Name","value":{"op":"empty"}}]}""").Filter!;
        await Assert.That(blank.Op).IsEqualTo(FilterOperator.IsNull);
    }

    [Test]
    public async Task Filter_Unknown_Column_Is_Not_Dropped()
    {
        var query = QueryWithFilters("""{"columnFilters":[{"id":"nope","value":"x"}]}""");
        var leaf = (FilterLeaf)query.Filter!;
        await Assert.That(leaf.Field).IsEqualTo("nope");
    }

    [Test]
    public async Task Filter_Unknown_Op_Throws()
    {
        await Assert.That(() => QueryWithFilters(
            """{"columnFilters":[{"id":"Name","value":{"op":"regex","value":"a"}}]}"""))
            .Throws<AdapterBindException>();
    }

    [Test]
    public async Task Filter_Between_Missing_Bound_Throws()
    {
        await Assert.That(() => QueryWithFilters(
            """{"columnFilters":[{"id":"Price","value":{"op":"between","value":[1,null]}}]}"""))
            .Throws<AdapterBindException>();
    }

    [Test]
    public async Task Filter_Unexpected_Property_Throws()
    {
        await Assert.That(() => QueryWithFilters(
            """{"columnFilters":[{"id":"Price","value":{"min":1,"filterType":"number"}}]}"""))
            .Throws<AdapterBindException>();
    }

    [Test]
    public async Task Filter_Nested_Array_Throws()
    {
        await Assert.That(() => QueryWithFilters(
            """{"columnFilters":[{"id":"Id","value":[1,{"a":2}]}]}"""))
            .Throws<AdapterBindException>();
    }

    // -- channels -------------------------------------------------------------------------------------

    [Test]
    public async Task Global_Filter_Is_Search_Only_And_Empty_Search_Is_Null()
    {
        var query = Adapter.ToQuery(
            Adapter.BindRequest(Raw("""{"globalFilter":"tea","columnFilters":[{"id":"Price","value":1}]}""")),
            BuildView());

        await Assert.That(query.Search).IsNotNull();
        await Assert.That(query.Filter).IsNotNull();
        await Assert.That(query.Scope).IsNull();

        var cleared = Adapter.ToQuery(Adapter.BindRequest(Raw("""{"globalFilter":"   "}""")), BuildView());
        await Assert.That(cleared.Search).IsNull();
        await Assert.That(cleared.Filter).IsNull();
    }

    [Test]
    public async Task Global_Filter_Single_Searchable_Field_Is_A_Leaf()
    {
        var query = Adapter.ToQuery(
            Adapter.BindRequest(Raw("""{"globalFilter":"tea"}""")),
            BuildSingleSearchableView());

        var leaf = (FilterLeaf)query.Search!;
        await Assert.That(leaf.Field).IsEqualTo("Name");
        await Assert.That(leaf.Op).IsEqualTo(FilterOperator.Contains);
    }

    [Test]
    public async Task Global_Filter_With_No_Searchable_String_Is_Null()
    {
        var fields = new[]
        {
            FieldMetadata.Create("Id", typeof(int), isSearchable: false, allowedOperators: FilterOperator.Equals),
        };
        var view = new ViewMetadata(
            "vNone", "/test/vNone", typeof(object), null, null, fields, null, HardLimits.Default, true);

        var query = Adapter.ToQuery(Adapter.BindRequest(Raw("""{"globalFilter":"tea"}""")), view);
        await Assert.That(query.Search).IsNull();
    }

    // -- column visibility → SelectFields -------------------------------------------------------------

    [Test]
    public async Task Visibility_Absent_Or_All_Visible_Leaves_SelectFields_Null()
    {
        var absent = Adapter.ToQuery(Adapter.BindRequest(Raw("{}")), BuildView());
        await Assert.That(absent.SelectFields).IsNull();

        var shown = Adapter.ToQuery(
            Adapter.BindRequest(Raw("""{"columnVisibility":{"Name":true,"actions":false}}""")),
            BuildView());
        await Assert.That(shown.SelectFields).IsNull();
    }

    [Test]
    public async Task Visibility_Hides_A_Known_Field_In_Metadata_Order()
    {
        var query = Adapter.ToQuery(
            Adapter.BindRequest(Raw("""{"columnVisibility":{"Price":false,"notAField":false}}""")),
            BuildView());

        await Assert.That(query.SelectFields).IsEquivalentTo(new[] { "Id", "Name", "Description" });
    }

    [Test]
    public async Task Visibility_MisCased_Key_Does_Not_Hide_The_Field()
    {
        var query = Adapter.ToQuery(
            Adapter.BindRequest(Raw("""{"columnVisibility":{"price":false}}""")),
            BuildView());

        await Assert.That(query.SelectFields).IsNull();
    }

    // -- response -------------------------------------------------------------------------------------

    [Test]
    public async Task Response_Uses_Filtered_Total_And_Ceil_Page_Count()
    {
        var request = new TanStackTableRequest
        {
            Pagination = new TanStackPaginationState { PageIndex = 0, PageSize = 10 },
        };
        var result = new AdapterListResult(new object?[] { "a", "b" }, RecordsFiltered: 25, RecordsTotal: 100);

        var response = Adapter.ToResponse(result, request, BuildView());

        await Assert.That(response.Rows.Count).IsEqualTo(2);
        await Assert.That(response.RowCount).IsEqualTo(25);
        await Assert.That(response.PageCount).IsEqualTo(3);
    }

    [Test]
    public async Task Response_PageCount_Is_Zero_When_PageSize_Is_Not_Positive()
    {
        var request = new TanStackTableRequest
        {
            Pagination = new TanStackPaginationState { PageSize = 0 },
        };
        var result = new AdapterListResult(Array.Empty<object?>(), 10, 10);

        var response = Adapter.ToResponse(result, request, BuildView());

        await Assert.That(response.PageCount).IsEqualTo(0);
        await Assert.That(response.RowCount).IsEqualTo(10);
    }

    [Test]
    public async Task Response_Serializes_CamelCase_Through_The_Source_Gen_Context()
    {
        var response = new TanStackTableResponse
        {
            Rows = Array.Empty<object?>(),
            RowCount = 4,
            PageCount = 1,
        };

        var json = JsonSerializer.Serialize(response, TanStackJsonContext.Default.TanStackTableResponse);

        await Assert.That(json).Contains("\"rowCount\":4");
        await Assert.That(json).Contains("\"pageCount\":1");
        await Assert.That(json).Contains("\"rows\"");
    }

    private ViewQueryRequest QueryWithFilters(string json) =>
        Adapter.ToQuery(Adapter.BindRequest(Raw(json)), BuildView());
}
