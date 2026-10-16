using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using a2n.Vista.Contracts;
using a2n.Vista.Examples.Northwind.Views;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Northwind.DataAccess;

namespace a2n.Vista.Examples.Northwind;

/// <summary>
/// End-to-end verification harness for the <c>vWritableMemo</c> writable view (Requirement R16.5). It
/// drives the real Core <see cref="IViewExecutor"/> write facet exactly as the HTTP layer does — a
/// Create, then an Update, then a Delete — and asserts each operation's effect against the backing
/// <see cref="NorthwindDbContext"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RunAsync"/> stands up its own isolated, in-memory SQLite database (a single open
/// connection kept alive for the run), calls
/// <see cref="Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade.EnsureCreated"/> to materialize
/// the schema (including <c>VistaMemos</c>), and wires a self-contained DI container:
/// <c>AddDbContext&lt;NorthwindDbContext&gt;</c> over that connection, an <c>AddScoped&lt;DbContext&gt;</c>
/// forwarder (so the executor resolves the concrete context when no template captured one), and
/// <c>AddVista(v =&gt; v.Register&lt;WritableMemoView&gt;())</c> which publishes the view metadata, the
/// write-facet registry, and the reflection write-mapper resolver. That pass never touches the shipped
/// sample file.
/// </para>
/// <para>
/// <see cref="RunAgainstHostAsync"/> repeats Create/Update/Delete through the host's own
/// <see cref="NorthwindDbContext"/>, the database <c>dotnet run</c> serves. Startup creates
/// <c>VistaMemos</c> there when the Microsoft sample does not already contain it
/// (<see cref="NorthwindDemoSchema.EnsureWritableMemoTable"/>). The host pass deletes the memo it
/// inserted, so Northwind's own tables stay untouched.
/// </para>
/// <para>
/// The concurrency token (<see cref="Memo.RowVersion"/>) is a <see cref="Guid"/>; the executor renders it
/// with invariant-culture formatting, which equals <see cref="Guid.ToString()"/>, so the wire
/// <c>If-Match</c> value the self-test supplies for Update/Delete is simply the row's current
/// <c>RowVersion</c> read back from the context.
/// </para>
/// </remarks>
public static class WriteSelfTest
{
    private const string ViewName = WritableMemoView.ViewName;

    /// <summary>
    /// Runs the write self-test against an isolated in-memory database.
    /// </summary>
    /// <returns><see langword="true"/> when every operation passed with zero failures; otherwise <see langword="false"/>.</returns>
    [RequiresUnreferencedCode("Self-test drives the reflection write path (Register<TView> + reflection write mapper).")]
    public static async Task<bool> RunAsync()
    {
        // Isolated in-memory SQLite: a single connection kept open for the whole run so the database
        // survives across DI scopes. The shipped read-only northwind.db is never touched.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<NorthwindDbContext>(options => options.UseSqlite(connection));
        // AddVista's scoped executor resolves the captured context type, or falls back to the DbContext
        // base when no template captured one — this forwarder supplies that base for the Style B view.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<NorthwindDbContext>());
        services.AddVista(v => v.Register<WritableMemoView>());

        using var provider = services.BuildServiceProvider();

        // Materialize the schema (creates the VistaMemos table) in the isolated database.
        using (var initScope = provider.CreateScope())
        {
            var ctx = initScope.ServiceProvider.GetRequiredService<NorthwindDbContext>();
            ctx.Database.EnsureCreated();
        }

        var registry = provider.GetRequiredService<IViewRegistry>();
        var view = registry.Get(ViewName);

        Console.WriteLine();
        Console.WriteLine("=== Vista Northwind write self-test ===");
        if (view is null)
        {
            Console.WriteLine($"FAIL: view '{ViewName}' is not registered.");
            Console.WriteLine("WRITE SELF-TEST RESULT: FAIL");
            return false;
        }

        Console.WriteLine($"View      : {view.Name}");
        Console.WriteLine($"Route     : {view.Route}");
        Console.WriteLine($"IsReadOnly: {view.IsReadOnly}");
        Console.WriteLine($"KeyFields : [{string.Join(", ", view.KeyFields)}]");
        Console.WriteLine($"CrudType  : {view.CrudType?.Name ?? "(none)"}");

        // Confirm the write path is exercising the SOURCE-GENERATED mapper, not the reflection fallback.
        // The WriteMapperGenerator emits a [ModuleInitializer] into this assembly that registers the
        // generated WriteMapper into GeneratedWriteMapperStore keyed by the view's runtime Name at
        // assembly load — before DI — so WriteMapperResolver prefers it (first-wins) for every write to
        // this view (Decision Log D121; Requirements R7.1, R7.4). This does not alter the write behavior;
        // it only reports which mapper origin the Create/Update/Delete below run through.
        var usingGenerated = GeneratedWriteMapperStore.TryGet(view.Name, out _);
        Console.WriteLine($"WriteMapper: {(usingGenerated ? "GENERATED (source generator)" : "reflection fallback")}");

        // Confirm the generated HTTP DISPATCH invoker is in effect for this Style B view (Decision Log
        // D123). The ViewInvokerGenerator emits a [ModuleInitializer] into this assembly that registers a
        // reflection-free IViewInvoker (closing List/Detail/Create/Update over MemoRow/MemoWriteModel at
        // compile time) into the Core-resident ViewInvokerStore keyed by the view's runtime Name, before
        // DI — so ViewRequestExecutor prefers it over MakeGenericMethod dispatch on the HTTP surface. This
        // is a diagnostic report of the invoker origin only; the write operations below still call the
        // Core executor directly and are unaffected (mirrors the WriteMapper line above).
        var usingGeneratedInvoker = ViewInvokerStore.TryGet(view.Name, out _);
        Console.WriteLine($"ViewInvoker: {(usingGeneratedInvoker ? "GENERATED (source generator)" : "reflection fallback")}");
        Console.WriteLine();

        var failures = 0;

        // ---- Create ---------------------------------------------------------------------------------
        var memoId = await CreateAsync(
            provider,
            view,
            subject: "Welcome",
            body: "First memo created by the write self-test.",
            incrementFailures: () => failures++);

        // ---- Update ---------------------------------------------------------------------------------
        if (memoId is int createdId)
        {
            await UpdateAsync(
                provider,
                view,
                createdId,
                subject: "Welcome (edited)",
                body: "Body updated by the write self-test.",
                incrementFailures: () => failures++);

            // ---- Delete -----------------------------------------------------------------------------
            await DeleteAsync(provider, view, createdId, incrementFailures: () => failures++);
        }
        else
        {
            // Create failed; the Update and Delete checks cannot run, count them as failed operations.
            Console.WriteLine("[2] Update — SKIPPED: no created row to update.");
            Console.WriteLine("    -> FAIL");
            Console.WriteLine("[3] Delete — SKIPPED: no created row to delete.");
            Console.WriteLine("    -> FAIL");
            failures += 2;
        }

        var passed = failures == 0;
        Console.WriteLine();
        Console.WriteLine($"Failed operations: {failures}");
        Console.WriteLine($"WRITE SELF-TEST RESULT: {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    /// <summary>
    /// R16.5 — Create/Update/Delete on the host database (the shipped <c>northwind.db</c> after startup
    /// has ensured <c>VistaMemos</c>). Also lists the view first, which is the read that returns 500
    /// when the table is missing. Rows inserted here are deleted before the method returns.
    /// </summary>
    /// <param name="services">The built host's root service provider.</param>
    /// <returns><see langword="true"/> when every operation passed; otherwise <see langword="false"/>.</returns>
    [RequiresUnreferencedCode("Self-test drives the reflection write path (Register<TView> + reflection write mapper).")]
    public static async Task<bool> RunAgainstHostAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        using var scope = services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IViewRegistry>();
        var view = registry.Get(ViewName);

        Console.WriteLine();
        Console.WriteLine("=== Vista Northwind write self-test (host database) ===");
        if (view is null)
        {
            Console.WriteLine($"FAIL: view '{ViewName}' is not registered.");
            Console.WriteLine("HOST WRITE SELF-TEST RESULT: FAIL");
            return false;
        }

        Console.WriteLine($"View      : {view.Name}");
        Console.WriteLine($"Route     : {view.Route}");
        Console.WriteLine();

        var failures = 0;
        if (!await HostListAsync(services, view))
        {
            failures++;
        }

        var subject = "vista-selftest-" + Guid.NewGuid().ToString("N");
        var editedSubject = subject + " (edited)";
        const string body = "Memo created by the host write self-test.";
        const string editedBody = "Body updated by the host write self-test.";

        try
        {
            var memoId = await CreateAsync(
                services,
                view,
                subject,
                body,
                incrementFailures: () => failures++);

            if (memoId is int createdId)
            {
                await UpdateAsync(
                    services,
                    view,
                    createdId,
                    editedSubject,
                    editedBody,
                    incrementFailures: () => failures++);
                await DeleteAsync(services, view, createdId, incrementFailures: () => failures++);
            }
            else
            {
                Console.WriteLine("[2] Update — SKIPPED: no created row to update.");
                Console.WriteLine("    -> FAIL");
                Console.WriteLine("[3] Delete — SKIPPED: no created row to delete.");
                Console.WriteLine("    -> FAIL");
                failures += 2;
            }
        }
        finally
        {
            await DeleteSubjectsAsync(services, subject, editedSubject);
        }

        var passed = failures == 0;
        Console.WriteLine();
        Console.WriteLine($"Failed operations: {failures}");
        Console.WriteLine($"HOST WRITE SELF-TEST RESULT: {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    /// <summary>Lists <c>vWritableMemo</c> on the host database. A missing <c>VistaMemos</c> table fails this check.</summary>
    [RequiresUnreferencedCode("Closes the generic IViewExecutor.ListAsync over MemoRow.")]
    private static async Task<bool> HostListAsync(IServiceProvider provider, ViewMetadata view)
    {
        Console.WriteLine("[0] List (host database)");
        try
        {
            using var scope = provider.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IViewExecutor>();
            var listed = await executor.ListAsync<MemoRow>(
                view,
                new ViewQueryRequest(null, Array.Empty<SortSpec>(), Page: 0, PageSize: 5),
                new ViewScope(),
                CancellationToken.None);
            Console.WriteLine($"    Page items={listed.Page.Items.Count}  TotalRows={listed.Page.TotalRows}");
            Console.WriteLine("    -> PASS");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    -> FAIL: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Removes host-self-test rows so the sample file does not keep them. Best-effort.</summary>
    private static async Task DeleteSubjectsAsync(IServiceProvider provider, string subject, string editedSubject)
    {
        try
        {
            using var scope = provider.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<NorthwindDbContext>();
            var rows = await ctx.Memos
                .Where(m => m.Subject == subject || m.Subject == editedSubject)
                .ToListAsync();
            if (rows.Count == 0)
            {
                return;
            }

            ctx.Memos.RemoveRange(rows);
            await ctx.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    cleanup warning: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>R16.5 — Create: insert a new memo through the write facet and verify it landed.</summary>
    [RequiresUnreferencedCode("Closes the generic IViewExecutor.CreateAsync over the typed write contract.")]
    private static async Task<int?> CreateAsync(
        IServiceProvider provider,
        ViewMetadata view,
        string subject,
        string body,
        Action incrementFailures)
    {
        int newId;
        using (var scope = provider.CreateScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<IViewExecutor>();
            var key = await executor.CreateAsync(
                view,
                new MemoWriteModel { Subject = subject, Body = body },
                new ViewScope(),
                CancellationToken.None);
            newId = Convert.ToInt32(key, CultureInfo.InvariantCulture);
        }

        Console.WriteLine($"[1] Create (Subject='{subject}')");
        Console.WriteLine($"    New MemoId = {newId}");

        // Verify the row exists with the written scalars.
        Memo? persisted;
        using (var scope = provider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<NorthwindDbContext>();
            persisted = await ctx.Memos.AsNoTracking().SingleOrDefaultAsync(m => m.MemoId == newId);
        }

        var ok = persisted is not null && persisted.Subject == subject && persisted.Body == body;
        Console.WriteLine($"    -> {(ok ? "PASS" : "FAIL")}");
        if (!ok)
        {
            incrementFailures();
            return null;
        }

        return newId;
    }

    /// <summary>R16.5 — Update: change the memo's scalars through the write facet under its token.</summary>
    [RequiresUnreferencedCode("Closes the generic IViewExecutor.UpdateAsync over the typed write contract.")]
    private static async Task UpdateAsync(
        IServiceProvider provider,
        ViewMetadata view,
        int memoId,
        string subject,
        string body,
        Action incrementFailures)
    {
        // The If-Match token is the row's current RowVersion; a Guid renders invariantly, matching the
        // executor's FormatToken so the optimistic-concurrency pre-check succeeds.
        var ifMatch = await ReadTokenAsync(provider, memoId);

        bool updated;
        using (var scope = provider.CreateScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<IViewExecutor>();
            updated = await executor.UpdateAsync(
                view,
                memoId,
                new MemoWriteModel { Subject = subject, Body = body },
                new ViewScope(),
                concurrencyToken: ifMatch,
                CancellationToken.None);
        }

        Console.WriteLine($"[2] Update (MemoId={memoId}, If-Match={ifMatch})");

        Memo? persisted;
        using (var scope = provider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<NorthwindDbContext>();
            persisted = await ctx.Memos.AsNoTracking().SingleOrDefaultAsync(m => m.MemoId == memoId);
        }

        var ok = updated && persisted is not null && persisted.Subject == subject && persisted.Body == body;
        Console.WriteLine($"    Updated={updated}  Subject='{persisted?.Subject}'");
        Console.WriteLine($"    -> {(ok ? "PASS" : "FAIL")}");
        if (!ok)
        {
            incrementFailures();
        }
    }

    /// <summary>R16.5 — Delete: remove the memo through the write facet under its token.</summary>
    [RequiresUnreferencedCode("Invokes the write facet Delete.")]
    private static async Task DeleteAsync(IServiceProvider provider, ViewMetadata view, int memoId, Action incrementFailures)
    {
        var ifMatch = await ReadTokenAsync(provider, memoId);

        bool deleted;
        using (var scope = provider.CreateScope())
        {
            var executor = scope.ServiceProvider.GetRequiredService<IViewExecutor>();
            deleted = await executor.DeleteAsync(
                view,
                memoId,
                new ViewScope(),
                concurrencyToken: ifMatch,
                CancellationToken.None);
        }

        Console.WriteLine($"[3] Delete (MemoId={memoId}, If-Match={ifMatch})");

        bool stillExists;
        using (var scope = provider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<NorthwindDbContext>();
            stillExists = await ctx.Memos.AsNoTracking().AnyAsync(m => m.MemoId == memoId);
        }

        var ok = deleted && !stillExists;
        Console.WriteLine($"    Deleted={deleted}  RowStillExists={stillExists}");
        Console.WriteLine($"    -> {(ok ? "PASS" : "FAIL")}");
        if (!ok)
        {
            incrementFailures();
        }
    }

    /// <summary>Reads a memo's current concurrency token as the invariant wire string (If-Match value).</summary>
    private static async Task<string?> ReadTokenAsync(IServiceProvider provider, int memoId)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<NorthwindDbContext>();
        var row = await ctx.Memos.AsNoTracking().SingleOrDefaultAsync(m => m.MemoId == memoId);
        return row?.RowVersion.ToString();
    }
}
