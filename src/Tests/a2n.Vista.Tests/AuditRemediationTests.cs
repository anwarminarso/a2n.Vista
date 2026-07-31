// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Adapters;
using a2n.Vista.Adapters.DataTablesNet;
using a2n.Vista.AspNetCore.Serialization;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Export;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using a2n.Vista.OpenApi.Schema;
using a2n.Vista.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;
using PurchasingFixtures = a2n.Vista.Tests.AuditFixtures.Purchasing;
using SalesFixtures = a2n.Vista.Tests.AuditFixtures.Sales;

namespace a2n.Vista.Tests;

/// <summary>
/// Regression tests for the defects found by the 2026-07-31 full code audit
/// (<c>docs/audit/2026-07-31-full-code-audit.md</c>). Each case pins the fixed behavior of one finding, cited
/// by its stable audit id, so a regression is caught by name rather than by symptom.
/// </summary>
public sealed class AuditRemediationTests
{
    private const string Il2026 = "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming";
    private const string Why = "Test exercises the runtime reflection authoring/filter/schema paths by design; trimming is not used for tests.";

    // ---- SEC-01: a type-erased Style A plan must fail closed on a populated scope -------------------

    /// <summary>
    /// SEC-01: <see cref="ProjectedViewExecutionPlan"/> cannot AND a scope predicate in pre-projection, so a
    /// request whose authorizer pushed a row filter must be refused rather than served unscoped. Before the
    /// fix the scope argument was validated and then ignored, silently dropping row-level security.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task SEC01_Style_A_Plan_Fails_Closed_When_The_Request_Scope_Carries_A_Row_Filter()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        var options = new DbContextOptionsBuilder<AuditWidgetContext>().UseSqlite(connection).Options;
        using var context = new AuditWidgetContext(options);
        using var services = new ServiceCollection().BuildServiceProvider();

        var plan = new ProjectedViewExecutionPlan(
            "audit-style-a",
            typeof(AuditWidgetSource),
            (db, _) => db.Set<AuditWidgetSource>());

        // No scope: the plan serves normally (unchanged behavior for the common Style A view).
        await Assert.That(plan.CreateScopedQueryable(context, services, new ViewScope())).IsNotNull();

        // A scope row filter: the plan must refuse instead of returning rows outside the authorized scope.
        var scope = new ViewScope();
        scope.AddRowFilter<AuditWidgetSource>(s => s.Id > 0);

        await Assert.That(() => plan.CreateScopedQueryable(context, services, scope))
            .Throws<NotSupportedException>();
    }

    // ---- BUG-01: a typed filter value mismatch is a 400, not a 500 ----------------------------------

    /// <summary>
    /// BUG-01: a non-string value on a <c>Guid</c>/<c>DateTimeOffset</c>/<c>DateOnly</c>/<c>TimeOnly</c> field
    /// used to escape the coercion guard and surface later as an unmapped <see cref="ArgumentException"/>
    /// (HTTP 500). It must be a <see cref="FilterValidationException"/> with
    /// <see cref="FilterErrorCode.InvalidValue"/>, which the problem-details mapper renders as 400.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task BUG01_Typed_Filter_Value_Mismatch_Is_A_Validation_Error()
    {
        var view = TypedFieldsMetadata();

        foreach (var field in new[]
        {
            nameof(AuditTypedRow.Key),
            nameof(AuditTypedRow.Stamp),
            nameof(AuditTypedRow.Day),
            nameof(AuditTypedRow.Moment),
        })
        {
            var thrown = CaptureFilter(new FilterLeaf(field, FilterOperator.Equals, 42), view);

            await Assert.That(thrown.Code).IsEqualTo(FilterErrorCode.InvalidValue);
            await Assert.That(thrown.Field).IsEqualTo(field);
        }
    }

    // ---- BUG-09: Key(...) satisfies the write facet's primary-key requirement -----------------------

    /// <summary>
    /// BUG-09: a writable view that declares its key with the view-level <c>Key(...)</c> override (the
    /// documented path for join/union views, D104/D105) used to fail at startup because the guard read only
    /// the per-field <c>.PrimaryKey()</c> mark. It must build, with the declared key resolved.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task BUG09_Writable_View_Keyed_With_The_Key_Override_Builds()
    {
        var services = new ServiceCollection();
        services.AddVista(v => v.Register<AuditKeyedWritableView>());
        using var provider = services.BuildServiceProvider();

        var view = provider.GetRequiredService<IViewRegistry>().Get("audit-keyed-writable");

        await Assert.That(view).IsNotNull();
        await Assert.That(view!.IsReadOnly).IsFalse();
        await Assert.That(view.KeyFields.Count).IsEqualTo(1);
        await Assert.That(view.KeyFields[0]).IsEqualTo(nameof(AuditKeyedRow.Id));
    }

    // ---- BUG-11: CSV export neutralizes formula-injection payloads ----------------------------------

    /// <summary>
    /// BUG-11: a cell whose value starts with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab or CR is evaluated as
    /// a formula when the exported file is opened. Such a value must be prefixed with an apostrophe so it
    /// stays literal text; an ordinary value must be untouched.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task BUG11_Csv_Export_Neutralizes_Formula_Values()
    {
        var view = TypedFieldsMetadata();
        var rows = new object?[]
        {
            new AuditTextRow { Text = "=1+1" },
            new AuditTextRow { Text = "+CMD" },
            new AuditTextRow { Text = "-2" },
            new AuditTextRow { Text = "@SUM(A1)" },
            new AuditTextRow { Text = "harmless" },
        };

        var csv = await WriteCsvAsync(TextMetadata(), rows);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        // Line 0 is the header; the payload rows follow in order.
        await Assert.That(lines[1]).IsEqualTo("'=1+1");
        await Assert.That(lines[2]).IsEqualTo("'+CMD");
        await Assert.That(lines[3]).IsEqualTo("'-2");
        await Assert.That(lines[4]).IsEqualTo("'@SUM(A1)");
        await Assert.That(lines[5]).IsEqualTo("harmless");

        // The view fixture is shared with BUG-01; touching it here keeps the metadata build exercised once.
        await Assert.That(view.Fields.Count).IsGreaterThan(0);
    }

    // ---- BUG-12: a negated empty QueryBuilder group must not become "no filter" ---------------------

    /// <summary>
    /// BUG-12: <c>{"not":true,"condition":"AND","rules":[]}</c> used to return <see langword="null"/>, dropping
    /// the filter entirely and returning every row — the exact inverse of the requested "no rows". The parser
    /// must keep the negation so the compiler decides the semantics.
    /// </summary>
    [Test]
    public async Task BUG12_Negated_Empty_QueryBuilder_Group_Keeps_The_Negation()
    {
        var fields = new Dictionary<string, FieldMetadata>(StringComparer.Ordinal);

        var negated = QueryBuilderParser.Parse("{\"not\":true,\"condition\":\"AND\",\"rules\":[]}", fields);
        await Assert.That(negated).IsTypeOf<FilterNot>();

        // An un-negated empty group stays a genuine no-op.
        var plain = QueryBuilderParser.Parse("{\"condition\":\"AND\",\"rules\":[]}", fields);
        await Assert.That(plain).IsNull();
    }

    // ---- SEC-03 / BUG-08: the OpenAPI schema honors field flags and type identity -------------------

    /// <summary>
    /// SEC-03: a field marked <c>Hidden()</c> is dropped from <c>GET {route}/metadata</c>, so it must not be
    /// published in <c>components.schemas</c> either; a maskable field stays described but annotated.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task SEC03_Hidden_Field_Is_Not_Published_In_The_Generated_Schema()
    {
        var view = HiddenFieldMetadata();
        var policy = DtoSchemaPolicy.ForView(view);
        await Assert.That(policy).IsNotNull();

        var generator = new DtoSchemaGenerator(VistaJson.Options);
        var schema = generator.GenerateSchema(view.QueryType, policy);

        var componentName = schema.Ref!["#/components/schemas/".Length..];
        var component = generator.Components[componentName];

        await Assert.That(component.Properties!.ContainsKey("id")).IsTrue();
        await Assert.That(component.Properties!.ContainsKey("secret")).IsFalse();

        // The maskable field is still described (clients need its type) but carries the masking notice.
        await Assert.That(component.Properties!["token"].Description).IsNotNull();
    }

    /// <summary>
    /// BUG-08: two row types with the same simple name in different namespaces used to collapse onto one
    /// component, so the second view's operations documented the first view's shape. Each must get its own
    /// component.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task BUG08_Same_Named_Types_In_Different_Namespaces_Get_Distinct_Components()
    {
        var generator = new DtoSchemaGenerator(VistaJson.Options);

        var sales = generator.GenerateSchema(typeof(SalesFixtures.OrderRow));
        var purchasing = generator.GenerateSchema(typeof(PurchasingFixtures.OrderRow));

        await Assert.That(sales.Ref).IsNotEqualTo(purchasing.Ref);
        await Assert.That(generator.Components.Count).IsEqualTo(2);

        // Each component describes its own shape rather than the first one registered.
        var salesComponent = generator.Components[sales.Ref!["#/components/schemas/".Length..]];
        var purchasingComponent = generator.Components[purchasing.Ref!["#/components/schemas/".Length..]];

        await Assert.That(salesComponent.Properties!.ContainsKey("customerId")).IsTrue();
        await Assert.That(purchasingComponent.Properties!.ContainsKey("supplierId")).IsTrue();
    }

    // ---- SEC-04: a masked field is non-sortable by default ------------------------------------------

    /// <summary>
    /// SEC-04 (Decision Log D143): masking defaults a field non-filterable and non-searchable (D95) but left
    /// it <b>sortable</b>, so a client could `ORDER BY` the masked column and page through the result to infer
    /// the relative ordering of the hidden values — for a numeric or date column, close to a binary search.
    /// The sort channel now follows the same default, with the explicit opt-in still winning.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task SEC04_Masked_Field_Is_Not_Sortable_Without_An_Explicit_OptIn()
    {
        var view = SortableMaskMetadata();

        var masked = view.Fields.Single(f => f.Name == nameof(AuditSortMaskRow.Salary));
        await Assert.That(masked.IsMaskable).IsTrue();
        await Assert.That(masked.IsSortable).IsFalse();
        await Assert.That(masked.IsFilterable).IsFalse();

        // The author's reviewed Sortable() opt-in still wins.
        var optedIn = view.Fields.Single(f => f.Name == nameof(AuditSortMaskRow.Grade));
        await Assert.That(optedIn.IsMaskable).IsTrue();
        await Assert.That(optedIn.IsSortable).IsTrue();

        // An unmasked field keeps the default-allow sort behavior.
        var plain = view.Fields.Single(f => f.Name == nameof(AuditSortMaskRow.Name));
        await Assert.That(plain.IsSortable).IsTrue();
    }

    // ---- BUG-02: the row window is offset-based, so clamping cannot move it -------------------------

    /// <summary>
    /// BUG-02 (Decision Log D144): DataTables is offset-based. Deriving a page index by dividing
    /// <c>start</c> by the <em>client's</em> <c>length</c> lost rows twice — integer division snapped an
    /// unaligned offset, and the engine's later page-size clamp shifted the window. The adapter now carries
    /// <c>start</c> verbatim as <see cref="ViewQueryRequest.Offset"/>.
    /// </summary>
    [Test]
    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    public async Task BUG02_DataTables_Carries_The_Absolute_Row_Offset()
    {
        var view = TextMetadata();
        var adapter = new DataTablesAdapter();

        // The historical failure case: start=200 with length=200 against a MaxPageSize of 100 produced
        // page=1 → skip 100, returning rows 100..199 for a request for rows 200..399.
        var unaligned = adapter.ToQuery(
            new DataTablesQuery { Start = 250, Length = 100 },
            view);

        await Assert.That(unaligned.Offset).IsEqualTo(250);
        await Assert.That(unaligned.PageSize).IsEqualTo(100);

        var oversized = adapter.ToQuery(
            new DataTablesQuery { Start = 200, Length = 200 },
            view);

        await Assert.That(oversized.Offset).IsEqualTo(200);
    }

    /// <summary>
    /// BUG-02: the engine honors the absolute offset and keeps the clamp a pure size concern — the window
    /// start never moves, a clamped request just returns fewer rows from the right position.
    /// </summary>
    [Test]
    public async Task BUG02_Window_Resolution_Honors_The_Offset_Independently_Of_The_Clamp()
    {
        // Offset set: the skip is the offset verbatim, whatever the (clamped) page size is.
        var offsetRequest = new ViewQueryRequest(null, Array.Empty<SortSpec>(), Page: 0, PageSize: 200, Offset: 200);
        var (skip, pageIndex) = AuditWindowProbe.Resolve(offsetRequest, pageSize: 100);
        await Assert.That(skip).IsEqualTo(200);
        await Assert.That(pageIndex).IsEqualTo(2);

        // An unaligned offset is preserved exactly (it no longer snaps to a page boundary).
        var unaligned = new ViewQueryRequest(null, Array.Empty<SortSpec>(), Page: 0, PageSize: 100, Offset: 250);
        await Assert.That(AuditWindowProbe.Resolve(unaligned, pageSize: 100).Skip).IsEqualTo(250);

        // No offset: the page model is unchanged.
        var paged = new ViewQueryRequest(null, Array.Empty<SortSpec>(), Page: 3, PageSize: 10);
        var pagedWindow = AuditWindowProbe.Resolve(paged, pageSize: 10);
        await Assert.That(pagedWindow.Skip).IsEqualTo(30);
        await Assert.That(pagedWindow.PageIndex).IsEqualTo(3);
    }

    /// <summary>
    /// BUG-02 (related) and DEAD-05: the DataTables adapter now rejects a negative <c>start</c> — matching
    /// the AG Grid range check instead of letting the engine silently rewrite <c>page = -1</c> to 0 — and
    /// rejects a regex search rather than executing it as a literal <c>Contains</c>.
    /// </summary>
    [Test]
    public async Task BUG02_DataTables_Rejects_A_Negative_Offset_And_Regex_Search()
    {
        var adapter = new DataTablesAdapter();

        await Assert.That(() => adapter.BindRequest(RawRequest(("start", "-10"), ("length", "10"))))
            .Throws<AdapterBindException>();

        await Assert.That(() => adapter.BindRequest(RawRequest(("search[regex]", "true"))))
            .Throws<AdapterBindException>();

        // A well-formed request still binds.
        var bound = adapter.BindRequest(RawRequest(("start", "10"), ("length", "25")));
        await Assert.That(bound.Start).IsEqualTo(10);
        await Assert.That(bound.Length).IsEqualTo(25);
    }

    // ---- DEAD-04: the absolute export cap is enforced -----------------------------------------------

    /// <summary>
    /// DEAD-04: <see cref="HardLimits.AbsoluteMaxExportRows"/> was documented as unbypassable but never
    /// enforced, so a view could set an unbounded export size. Every construction path must clamp.
    /// </summary>
    [Test]
    public async Task DEAD04_Export_Row_Cap_Is_Clamped_To_The_Absolute_Maximum()
    {
        var direct = new HardLimits(HardLimits.DefaultMaxPageSize, int.MaxValue);
        await Assert.That(direct.MaxExportRows).IsEqualTo(HardLimits.AbsoluteMaxExportRows);

        var mutated = HardLimits.Default with { MaxExportRows = int.MaxValue };
        await Assert.That(mutated.MaxExportRows).IsEqualTo(HardLimits.AbsoluteMaxExportRows);

        // A value under the cap is passed through untouched.
        var normal = new HardLimits(HardLimits.DefaultMaxPageSize, 250);
        await Assert.That(normal.MaxExportRows).IsEqualTo(250);
    }

    // ---- Helpers -----------------------------------------------------------------------------------

    private static AdapterRequest RawRequest(params (string Key, string Value)[] pairs)
    {
        var values = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
        {
            values[key] = new[] { value };
        }

        return new AdapterRequest("audit-text", values, JsonBody: null);
    }

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static ViewMetadata SortableMaskMetadata() => Metadata<AuditSortMaskView>("audit-sort-mask");

    private static async Task<string> WriteCsvAsync(ViewMetadata view, IReadOnlyList<object?> rows)
    {
        using var buffer = new MemoryStream();
        await new CsvViewExportWriter().WriteAsync(buffer, view, rows, CancellationToken.None);
        return new UTF8Encoding(false).GetString(buffer.ToArray().AsSpan(3)); // skip the UTF-8 BOM
    }

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static ViewMetadata TypedFieldsMetadata() => Metadata<AuditTypedView>("audit-typed");

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static ViewMetadata TextMetadata() => Metadata<AuditTextView>("audit-text");

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static ViewMetadata HiddenFieldMetadata() => Metadata<AuditHiddenView>("audit-hidden");

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static ViewMetadata Metadata<TView>(string viewName)
        where TView : class, new()
    {
        var services = new ServiceCollection();
        services.AddVista(v => v.Register<TView>());
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IViewRegistry>().Get(viewName)
            ?? throw new InvalidOperationException($"View '{viewName}' was not registered.");
    }

    [UnconditionalSuppressMessage("Trimming", Il2026, Justification = Why)]
    private static FilterValidationException CaptureFilter(FilterNode node, ViewMetadata view)
    {
        try
        {
            _ = new FilterCompiler().Compile<AuditTypedRow>(node, FilterOrigin.Filter, view);
        }
        catch (FilterValidationException ex)
        {
            return ex;
        }

        throw new InvalidOperationException("Expected a FilterValidationException, but none was thrown.");
    }
}

// ---- Fixtures ---------------------------------------------------------------------------------------

internal sealed class AuditWidgetSource
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed class AuditWidgetContext : DbContext
{
    public AuditWidgetContext(DbContextOptions<AuditWidgetContext> options)
        : base(options)
    {
    }

    public DbSet<AuditWidgetSource> Widgets => Set<AuditWidgetSource>();
}

internal sealed class AuditTypedSource
{
    public Guid Key { get; set; }

    public DateTimeOffset Stamp { get; set; }

    public DateOnly Day { get; set; }

    public TimeOnly Moment { get; set; }
}

internal sealed class AuditTypedRow
{
    public Guid Key { get; set; }

    public DateTimeOffset Stamp { get; set; }

    public DateOnly Day { get; set; }

    public TimeOnly Moment { get; set; }
}

/// <summary>A read-only view whose fields cover the four types whose coercion fell through (BUG-01).</summary>
internal sealed class AuditTypedView : View<AuditTypedRow>
{
    protected override void Configure(IViewBuilder<AuditTypedRow> b) =>
        b.Named("audit-typed")
         .From<AuditTypedSource>(s => new AuditTypedRow
         {
             Key = s.Key,
             Stamp = s.Stamp,
             Day = s.Day,
             Moment = s.Moment,
         })
         .Field(x => x.Key, f => f.PrimaryKey());
}

internal sealed class AuditTextSource
{
    public int Id { get; set; }

    public string Text { get; set; } = string.Empty;
}

internal sealed class AuditTextRow
{
    public string Text { get; set; } = string.Empty;
}

/// <summary>A single-column view used to inspect exported CSV cell text (BUG-11).</summary>
internal sealed class AuditTextView : View<AuditTextRow>
{
    protected override void Configure(IViewBuilder<AuditTextRow> b) =>
        b.Named("audit-text")
         .From<AuditTextSource>(s => new AuditTextRow { Text = s.Text })
         .Key(x => x.Text);
}

internal sealed class AuditHiddenSource
{
    public int Id { get; set; }

    public string Secret { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;
}

internal sealed class AuditHiddenRow
{
    public int Id { get; set; }

    public string Secret { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;
}

/// <summary>A view with one hidden and one masked field, for the emitted-schema guard (SEC-03).</summary>
internal sealed class AuditHiddenView : View<AuditHiddenRow>
{
    protected override void Configure(IViewBuilder<AuditHiddenRow> b) =>
        b.Named("audit-hidden")
         .From<AuditHiddenSource>(s => new AuditHiddenRow { Id = s.Id, Secret = s.Secret, Token = s.Token })
         .Field(x => x.Id, f => f.PrimaryKey())
         .Field(x => x.Secret, f => f.Hidden())
         .MaskField(x => x.Token, _ => true, _ => "***");
}

internal sealed class AuditSortMaskSource
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Salary { get; set; }

    public int Grade { get; set; }
}

internal sealed class AuditSortMaskRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal Salary { get; set; }

    public int Grade { get; set; }
}

/// <summary>
/// A view exercising the D143 sort default: <c>Salary</c> is masked with no opt-in (→ non-sortable),
/// <c>Grade</c> is masked but explicitly <c>Sortable()</c> (→ sortable), <c>Name</c> is unmasked.
/// </summary>
internal sealed class AuditSortMaskView : View<AuditSortMaskRow>
{
    protected override void Configure(IViewBuilder<AuditSortMaskRow> b) =>
        b.Named("audit-sort-mask")
         .From<AuditSortMaskSource>(s => new AuditSortMaskRow
         {
             Id = s.Id,
             Name = s.Name,
             Salary = s.Salary,
             Grade = s.Grade,
         })
         .Field(x => x.Id, f => f.PrimaryKey())
         .MaskField(x => x.Salary, _ => true, _ => 0m)
         .MaskField(x => x.Grade, _ => true, _ => 0)
         .Field(x => x.Grade, f => f.Sortable());
}

/// <summary>
/// Reaches <c>EfViewExecutor.ResolveWindow</c> (a protected static seam) so the D144 window arithmetic can be
/// asserted directly, without standing up a database.
/// </summary>
internal sealed class AuditWindowProbe : a2n.Vista.EntityFrameworkCore.Execution.EfViewExecutor
{
    private AuditWindowProbe()
        : base(new a2n.Vista.Filter.FilterCompiler())
    {
    }

    public static (int Skip, int PageIndex) Resolve(ViewQueryRequest request, int pageSize) =>
        ResolveWindow(request, pageSize);
}

internal sealed class AuditKeyedSource
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed class AuditKeyedRow
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed class AuditKeyedCrud
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A writable view whose key is declared with the view-level <c>Key(...)</c> override rather than a per-field
/// <c>.PrimaryKey()</c> mark — the shape that failed at startup before BUG-09 was fixed.
/// </summary>
internal sealed class AuditKeyedWritableView : View<AuditKeyedRow, AuditKeyedCrud>
{
    protected override void Configure(IViewBuilder<AuditKeyedRow, AuditKeyedCrud> builder)
    {
        // The view key is declared with Key(...) only — deliberately no per-field .PrimaryKey() mark.
        builder
            .Named("audit-keyed-writable")
            .From<AuditKeyedSource>(s => new AuditKeyedRow { Id = s.Id, Name = s.Name })
            .Key(x => x.Id);

        builder
            .CrudOn<AuditKeyedSource>()
            .MapWritable(c => c.Name, e => e.Name);
    }
}


