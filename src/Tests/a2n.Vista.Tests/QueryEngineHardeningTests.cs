using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for the query-engine-hardening spec (Decision Log D104–D109): the view key model
/// (<see cref="FieldMetadata.IsPrimaryKey"/> / <see cref="ViewMetadata.KeyFields"/>), deterministic
/// paging, DoS guards, the <see cref="IQueryDialect"/> default dialect, and composite key resolution.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Tests exercise the runtime reflection path of authoring/execution by design.")]
public sealed class QueryEngineHardeningTests
{
    // ---- R1/R2: key model ------------------------------------------------------------------

    [Test]
    public async Task PrimaryKey_Marks_Field_And_Derives_KeyFields()
    {
        var view = BuildView(KeyTemplate.SingleKeyView);

        await Assert.That(view.Fields.Single(f => f.Name == nameof(KeyRow.Id)).IsPrimaryKey).IsTrue();
        await Assert.That(view.KeyFields).IsEquivalentTo(new[] { nameof(KeyRow.Id) });
    }

    [Test]
    public async Task Composite_Key_Preserves_Declared_Order()
    {
        var view = BuildView(KeyTemplate.CompositeKeyView);

        await Assert.That(view.KeyFields).IsEquivalentTo(
            new[] { nameof(KeyRow.Id), nameof(KeyRow.Name) });
    }

    [Test]
    public async Task Explicit_Key_Overrides_PrimaryKey_Default()
    {
        var view = BuildView(KeyTemplate.ExplicitOverrideView);

        // PrimaryKey() marked Id, but Key("Name") overrides the derived default.
        await Assert.That(view.KeyFields).IsEquivalentTo(new[] { nameof(KeyRow.Name) });
    }

    [SuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "Gaya A authoring reflection path is exercised on purpose.")]
    private static ViewMetadata BuildView(string viewName)
    {
        var definitions = new KeyTemplate().BuildViews();
        return definitions.Single(d => d.Metadata.Name == viewName).Metadata;
    }
}

// ---- R5: DoS guards --------------------------------------------------------------------

public sealed class QueryEngineHardeningGuardTests
{
    private static ViewMetadata LimitedView() => new(
        Name: "limited",
        Route: "/test/limited",
        QueryType: typeof(KeyRow),
        CrudType: null,
        CrudEntityType: null,
        Fields: new[]
        {
            FieldMetadata.Create(nameof(KeyRow.Id), typeof(int), allowedOperators: FilterOperator.Equals | FilterOperator.In),
            FieldMetadata.Create(nameof(KeyRow.Name), typeof(string), allowedOperators: FilterOperator.Text | FilterOperator.In),
        },
        Authorization: null,
        Limits: new HardLimits(50, 1000) { MaxFilterDepth = 2, MaxFilterLeaves = 3, MaxFilterStringLength = 5, MaxInValues = 2 },
        IsReadOnly: true)
    {
        KeyFields = new[] { nameof(KeyRow.Id) },
    };

    [Test]
    public async Task Filter_Depth_Over_Limit_Is_Rejected()
    {
        var leaf = new FilterLeaf(nameof(KeyRow.Name), FilterOperator.Contains, "ab");
        FilterNode tree = new FilterAnd(new FilterNode[] { new FilterAnd(new FilterNode[] { leaf }) });

        await AssertTooComplex(tree);
    }

    [Test]
    public async Task Filter_Leaf_Count_Over_Limit_Is_Rejected()
    {
        var leaves = Enumerable.Range(0, 4)
            .Select(_ => (FilterNode)new FilterLeaf(nameof(KeyRow.Name), FilterOperator.Contains, "a"))
            .ToArray();
        await AssertTooComplex(new FilterAnd(leaves));
    }

    [Test]
    public async Task Filter_String_Length_Over_Limit_Is_Rejected() =>
        await AssertTooComplex(new FilterLeaf(nameof(KeyRow.Name), FilterOperator.Contains, "abcdef"));

    [Test]
    public async Task In_Values_Over_Limit_Is_Rejected() =>
        await AssertTooComplex(new FilterLeaf(nameof(KeyRow.Id), FilterOperator.In, new[] { 1, 2, 3 }));

    [Test]
    public async Task Tree_Within_Limits_Compiles()
    {
        var compiler = new FilterCompiler();
        FilterNode tree = new FilterAnd(new FilterNode[]
        {
            new FilterLeaf(nameof(KeyRow.Name), FilterOperator.Contains, "ab"),
            new FilterLeaf(nameof(KeyRow.Id), FilterOperator.Equals, 1),
        });

        var predicate = compiler.Compile<KeyRow>(tree, FilterOrigin.Filter, LimitedView());
        await Assert.That(predicate).IsNotNull();
    }

    private static async Task AssertTooComplex(FilterNode tree)
    {
        var compiler = new FilterCompiler();
        FilterValidationException? ex = null;
        try
        {
            _ = compiler.Compile<KeyRow>(tree, FilterOrigin.Filter, LimitedView());
        }
        catch (FilterValidationException e)
        {
            ex = e;
        }

        await Assert.That(ex).IsNotNull();
        await Assert.That(ex!.Code).IsEqualTo(FilterErrorCode.RequestTooComplex);
    }
}

// ---- R4: dialect, R6: composite key, R3: deterministic paging --------------------------

[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Executor tests exercise the runtime reflection path by design.")]
public sealed class QueryEngineHardeningExecutorTests
{
    [Test]
    public async Task DefaultDialect_Emits_Like_Call()
    {
        var dialect = new DefaultQueryDialect();
        var parameter = Expression.Parameter(typeof(KeyRow), "x");
        var member = Expression.Property(parameter, nameof(KeyRow.Name));

        var expression = dialect.BuildStringMatch(member, "a%b", StringMatchKind.Contains);

        await Assert.That(expression is MethodCallExpression).IsTrue();
        await Assert.That(((MethodCallExpression)expression).Method.Name).IsEqualTo("Like");
        await Assert.That(dialect.ProviderName).IsEqualTo(DefaultQueryDialect.AnyRelationalProvider);
    }

    [Test]
    public async Task Detail_Single_Key_Resolves_By_Scalar_And_Map()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();

        var byScalar = await harness.Executor.DetailAsync<WidgetRow>(view, 5, new ViewScope(), CancellationToken.None);
        var byMap = await harness.Executor.DetailAsync<WidgetRow>(
            view, new Dictionary<string, object?> { ["Id"] = 5 }, new ViewScope(), CancellationToken.None);

        await Assert.That(byScalar!.Id).IsEqualTo(5);
        await Assert.That(byMap!.Id).IsEqualTo(5);
    }

    [Test]
    public async Task Detail_Composite_Key_Resolves_By_Name()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView() with { KeyFields = new[] { "Id", "Name" } };

        var key = new Dictionary<string, object?> { ["Name"] = "Widget 5", ["Id"] = 5 };
        var row = await harness.Executor.DetailAsync<WidgetRow>(view, key, new ViewScope(), CancellationToken.None);

        await Assert.That(row!.Id).IsEqualTo(5);
    }

    [Test]
    public async Task Detail_Composite_With_Scalar_Is_Rejected()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView() with { KeyFields = new[] { "Id", "Name" } };

        await Assert.That(await CaptureAsync(() =>
            harness.Executor.DetailAsync<WidgetRow>(view, 5, new ViewScope(), CancellationToken.None))).IsNotNull();
    }

    [Test]
    public async Task Detail_Composite_Missing_Member_Is_Rejected()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView() with { KeyFields = new[] { "Id", "Name" } };
        var key = new Dictionary<string, object?> { ["Id"] = 5 };

        await Assert.That(await CaptureAsync(() =>
            harness.Executor.DetailAsync<WidgetRow>(view, key, new ViewScope(), CancellationToken.None))).IsNotNull();
    }

    [Test]
    public async Task EmptySort_Pages_Cover_All_Rows_Once()
    {
        using var harness = WidgetTestHarness.Create();
        var view = WidgetTestHarness.BuildView();
        var seen = new HashSet<int>();

        for (var page = 0; page < 3; page++)
        {
            var request = new ViewQueryRequest(Filter: null, Sort: Array.Empty<SortSpec>(), Page: page, PageSize: 10);
            var result = await harness.Executor.ListAsync<WidgetRow>(view, request, new ViewScope(), CancellationToken.None);
            foreach (var row in result.Page.Items)
            {
                seen.Add(row.Id);
            }
        }

        await Assert.That(seen.Count).IsEqualTo(WidgetTestHarness.SeededRowCount);
    }

    private static async Task<FilterValidationException?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (FilterValidationException ex)
        {
            return ex;
        }

        return null;
    }
}

/// <summary>Named projection row for the key-model tests.</summary>
internal sealed class KeyRow
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// Gaya A template exercising the view key model (Decision Log D104): a single-key view (PrimaryKey),
/// a composite-key view (explicit Key in order), and an override view (explicit Key wins over PrimaryKey).
/// </summary>
internal sealed class KeyTemplate : ViewTemplate<DummyContext>
{
    public const string SingleKeyView = "singleKey";
    public const string CompositeKeyView = "compositeKey";
    public const string ExplicitOverrideView = "explicitOverride";

    protected override void Configure(IViewTemplateBuilder<DummyContext> views)
    {
        views.AddView(SingleKeyView, static (db, sp) => Enumerable.Empty<KeyRow>().AsQueryable())
             .Field(r => r.Id, f => f.PrimaryKey());

        views.AddView(CompositeKeyView, static (db, sp) => Enumerable.Empty<KeyRow>().AsQueryable())
             .Key(r => r.Id, r => r.Name);

        views.AddView(ExplicitOverrideView, static (db, sp) => Enumerable.Empty<KeyRow>().AsQueryable())
             .Field(r => r.Id, f => f.PrimaryKey())
             .Key(r => r.Name);
    }
}
