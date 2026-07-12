// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Requirement R2 (Decision Log D110) — the request/response envelopes of the action-style surface,
/// verified end to end over an in-process <see cref="TestServer"/> with real SQLite-backed views (a
/// single-key <c>r2-orders</c> view and a composite-key <c>r2-order-details</c> view modelled on the
/// Northwind <c>OrderId</c>+<c>ProductId</c> key). Style A (central-template) authoring is used because
/// it is executable through the reflection bridge without the source generator:
/// <list type="bullet">
/// <item>R2.1 — <c>POST {route}/list</c> with a filter/search/sort/paging body returns the expected
/// page (filtered total + the requested slice, in the requested order).</item>
/// <item>R2.2 — <c>POST {route}/detail</c> resolves a row by a scalar key and by a composite
/// <c>{ field: value }</c> key.</item>
/// <item>R2.4 — <c>GET {route}/metadata</c> returns the metadata DTO (name, key fields, fields).</item>
/// <item>R2.5 — a malformed request body is rejected with <c>400 Bad Request</c>.</item>
/// </list>
/// The <c>FilterNode</c> polymorphic round-trip (part of R2.1) is covered as a focused unit in
/// <see cref="HttpSurfaceTests.FilterNode_Json_Roundtrips_Polymorphic_Tree"/>; here the same converter is
/// exercised implicitly by the filtered-list request travelling through the real pipeline.
/// The Style A reflection authoring/mapping path is RUC, so IL2026 is suppressed at the class level —
/// trimming is not used for tests.
/// </summary>
[SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
    Justification = "Integration tests drive the reflection-based endpoint/executor path by design; trimming is not used for tests.")]
public sealed class HttpSurfaceR2Tests
{
    private const string OrdersRoute = "/api/views/r2-orders";
    private const string OrderDetailsRoute = "/api/views/r2-order-details";

    // ---- R2.1: list with a filter/sort/paging body returns the expected page ------------------------

    /// <summary>
    /// R2.1: a <c>list</c> body carrying a <c>FilterNode</c> tree, an ordering and a page window returns
    /// exactly the filtered slice in the requested order. Orders are keyed 1..10; the filter
    /// <c>Id &gt;= 5</c> keeps orders 5..10 (6 rows), and sorted by <c>Id desc</c> the first page of
    /// size 2 is orders 10 and 9. (Sorting/filtering use the integer key because SQLite cannot translate
    /// an <c>ORDER BY</c> over the <c>decimal</c> <c>Total</c> column.)
    /// </summary>
    [Test]
    public async Task List_With_Filter_Sort_And_Paging_Returns_Expected_Page()
    {
        await using var app = await TestApp.StartAsync();

        const string body = """
        {
            "filter": { "field": "Id", "op": "GreaterThanOrEqual", "value": 5 },
            "sort": [ { "field": "Id", "desc": true } ],
            "page": 0,
            "pageSize": 2
        }
        """;

        var response = await app.Client.PostAsync($"{OrdersRoute}/list", Json(body));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        // Unfiltered scope total is all 10 orders; the filter keeps 6 (orders 5..10).
        await Assert.That(root.GetProperty("totalRowsUnfiltered").GetInt64()).IsEqualTo(10L);
        var page = root.GetProperty("page");
        await Assert.That(page.GetProperty("totalRows").GetInt64()).IsEqualTo(6L);

        var items = page.GetProperty("items");
        await Assert.That(items.GetArrayLength()).IsEqualTo(2);
        // Sorted by Id desc: the top of the filtered set is order 10 then order 9.
        await Assert.That(items[0].GetProperty("id").GetInt32()).IsEqualTo(10);
        await Assert.That(items[1].GetProperty("id").GetInt32()).IsEqualTo(9);
    }

    /// <summary>
    /// R2.1: a <c>list</c> body carrying global <c>search</c> text matches only the searchable string
    /// field (<c>Customer</c>). Orders 1..10 have <c>Customer = "Customer {Id}"</c>; searching for
    /// <c>"Customer 1"</c> matches orders 1 and 10 (the two names that contain the substring).
    /// </summary>
    [Test]
    public async Task List_With_Global_Search_Filters_By_Searchable_String_Field()
    {
        await using var app = await TestApp.StartAsync();

        const string body = """
        { "search": "Customer 1", "sort": [ { "field": "Id", "desc": false } ], "page": 0, "pageSize": 50 }
        """;

        var response = await app.Client.PostAsync($"{OrdersRoute}/list", Json(body));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = doc.RootElement.GetProperty("page");

        await Assert.That(page.GetProperty("totalRows").GetInt64()).IsEqualTo(2L);
        var items = page.GetProperty("items");
        await Assert.That(items.GetArrayLength()).IsEqualTo(2);
        await Assert.That(items[0].GetProperty("id").GetInt32()).IsEqualTo(1);
        await Assert.That(items[1].GetProperty("id").GetInt32()).IsEqualTo(10);
    }

    // ---- R2.2: detail resolves by scalar key and by composite key -----------------------------------

    /// <summary>R2.2: <c>POST {route}/detail</c> with a scalar <c>key</c> returns that one row.</summary>
    [Test]
    public async Task Detail_By_Scalar_Key_Returns_The_Row()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{OrdersRoute}/detail", Json("""{ "key": 3 }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = doc.RootElement;
        await Assert.That(row.GetProperty("id").GetInt32()).IsEqualTo(3);
        await Assert.That(row.GetProperty("customer").GetString()).IsEqualTo("Customer 3");
        await Assert.That(row.GetProperty("total").GetDecimal()).IsEqualTo(300m);
    }

    /// <summary>
    /// R2.2: <c>POST {route}/detail</c> with a composite <c>{ field: value }</c> key (Northwind
    /// <c>OrderId</c>+<c>ProductId</c>) resolves the single matching row.
    /// </summary>
    [Test]
    public async Task Detail_By_Composite_Key_Returns_The_Row()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync(
            $"{OrderDetailsRoute}/detail",
            Json("""{ "key": { "OrderId": 2, "ProductId": 11 } }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = doc.RootElement;
        await Assert.That(row.GetProperty("orderId").GetInt32()).IsEqualTo(2);
        await Assert.That(row.GetProperty("productId").GetInt32()).IsEqualTo(11);
        await Assert.That(row.GetProperty("quantity").GetInt32()).IsEqualTo(2 * 100 + 11);
    }

    /// <summary>
    /// R2.2: the composite key is resolved by field name, not position — swapping the member order in the
    /// JSON object resolves the same row (Decision Log D109).
    /// </summary>
    [Test]
    public async Task Detail_By_Composite_Key_Is_Field_Order_Independent()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync(
            $"{OrderDetailsRoute}/detail",
            Json("""{ "key": { "ProductId": 11, "OrderId": 2 } }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = doc.RootElement;
        await Assert.That(row.GetProperty("orderId").GetInt32()).IsEqualTo(2);
        await Assert.That(row.GetProperty("productId").GetInt32()).IsEqualTo(11);
    }

    /// <summary>R2.2 / DR6: a key that matches no row yields <c>404 Not Found</c>.</summary>
    [Test]
    public async Task Detail_For_Absent_Key_Is_404()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{OrdersRoute}/detail", Json("""{ "key": 9999 }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    // ---- R2.5: malformed body → 400 -----------------------------------------------------------------

    /// <summary>R2.5: an unparseable JSON <c>list</c> body is rejected with <c>400 Bad Request</c>.</summary>
    [Test]
    public async Task List_With_Unparseable_Body_Is_400()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{OrdersRoute}/list", Json("{ this is not json "));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>R2.5: an unparseable JSON <c>detail</c> body is rejected with <c>400 Bad Request</c>.</summary>
    [Test]
    public async Task Detail_With_Unparseable_Body_Is_400()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{OrdersRoute}/detail", Json("""{ "key": """));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// R2.5: a well-formed JSON <c>detail</c> body that omits the required <c>key</c> member (for
    /// example <c>{}</c>) is a malformed envelope and must be rejected with <c>400 Bad Request</c>,
    /// not surface as an unhandled <c>500</c> from the key reader.
    /// </summary>
    [Test]
    public async Task Detail_With_Missing_Key_Is_400()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{OrdersRoute}/detail", Json("{}"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    /// <summary>R2.5: an explicit <c>null</c> key on <c>detail</c> is equally malformed → <c>400 Bad Request</c>.</summary>
    [Test]
    public async Task Detail_With_Null_Key_Is_400()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.PostAsync($"{OrdersRoute}/detail", Json("""{ "key": null }"""));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    // ---- R2.4: metadata DTO -------------------------------------------------------------------------

    /// <summary>
    /// R2.4: <c>GET {route}/metadata</c> returns the metadata DTO; for the composite view the
    /// <c>keyFields</c> projection carries both key fields in declaration order.
    /// </summary>
    [Test]
    public async Task Metadata_Returns_The_Dto_With_Composite_Key_Fields()
    {
        await using var app = await TestApp.StartAsync();

        var response = await app.Client.GetAsync($"{OrderDetailsRoute}/metadata");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        await Assert.That(root.GetProperty("name").GetString()).IsEqualTo("r2-order-details");

        var keyFields = root.GetProperty("keyFields").EnumerateArray().Select(e => e.GetString()!).ToArray();
        await Assert.That(keyFields).IsEquivalentTo(new[] { "OrderId", "ProductId" });

        var fieldNames = root.GetProperty("fields").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToArray();
        await Assert.That(fieldNames).Contains("Quantity");
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    // ---- Fixtures -----------------------------------------------------------------------------------

    private sealed class R2Order
    {
        public int Id { get; set; }

        public string Customer { get; set; } = string.Empty;

        public decimal Total { get; set; }
    }

    private sealed class R2OrderDetail
    {
        public int OrderId { get; set; }

        public int ProductId { get; set; }

        public int Quantity { get; set; }
    }

    private sealed class R2Context : DbContext
    {
        public R2Context(DbContextOptions<R2Context> options)
            : base(options)
        {
        }

        public DbSet<R2Order> Orders => Set<R2Order>();

        public DbSet<R2OrderDetail> OrderDetails => Set<R2OrderDetail>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<R2OrderDetail>().HasKey(e => new { e.OrderId, e.ProductId });
    }

    /// <summary>
    /// A Style A central template exposing a single-key <c>r2-orders</c> view and a composite-key
    /// <c>r2-order-details</c> view (keyed by <c>OrderId</c>+<c>ProductId</c> in declaration order).
    /// </summary>
    private sealed class R2Views : ViewTemplate<R2Context>
    {
        protected override void Configure(IViewTemplateBuilder<R2Context> views)
        {
            views.AddView("r2-orders", (db, sp) =>
                    from o in db.Orders
                    select new { o.Id, o.Customer, o.Total })
                .Field(x => x.Id, f => f.PrimaryKey());

            views.AddView("r2-order-details", (db, sp) =>
                    from d in db.OrderDetails
                    select new { d.OrderId, d.ProductId, d.Quantity })
                .Field(x => x.OrderId, f => f.PrimaryKey())
                .Field(x => x.ProductId, f => f.PrimaryKey());
        }
    }

    /// <summary>A started in-process host + its test client, owning the in-memory SQLite connection.</summary>
    private sealed class TestApp : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly SqliteConnection _connection;

        private TestApp(IHost host, SqliteConnection connection, HttpClient client)
        {
            _host = host;
            _connection = connection;
            Client = client;
        }

        public HttpClient Client { get; }

        [RequiresUnreferencedCode("Vista endpoint mapping uses the reflection bridge by design.")]
        public static async Task<TestApp> StartAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            var host = await new HostBuilder()
                .ConfigureWebHost(web => web
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddDbContext<R2Context>(o => o.UseSqlite(connection));
                        services.AddVista(v => v.RegisterTemplate<R2Views, R2Context>());
                        services.AddVistaEndpoints(e => e.AllowAnonymousAccess());
                    })
                    .Configure(app =>
                    {
                        app.UseVistaExceptionHandling();
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.MapVistaViews());
                    }))
                .StartAsync();

            using (var scope = host.Services.CreateScope())
            {
                var ctx = scope.ServiceProvider.GetRequiredService<R2Context>();
                ctx.Database.EnsureCreated();

                // Orders 1..10: Customer "Customer {Id}", Total = Id * 100.
                ctx.Orders.AddRange(Enumerable.Range(1, 10)
                    .Select(i => new R2Order { Id = i, Customer = $"Customer {i}", Total = i * 100m }));

                // Order-details: two product lines per order, Quantity = OrderId * 100 + ProductId.
                ctx.OrderDetails.AddRange(Enumerable.Range(1, 10)
                    .SelectMany(orderId => new[] { 11, 42 }
                        .Select(productId => new R2OrderDetail
                        {
                            OrderId = orderId,
                            ProductId = productId,
                            Quantity = orderId * 100 + productId,
                        })));

                ctx.SaveChanges();
            }

            return new TestApp(host, connection, host.GetTestClient());
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _host.StopAsync();
            _host.Dispose();
            _connection.Dispose();
        }
    }
}
