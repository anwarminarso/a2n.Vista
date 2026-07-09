// Licensed to the a2n.Vista project. Published artifact — English only.
//
// Phase 3 AOT verification (spec source-generator-write-mapper, Task 11.1, R10.1–R10.6; D121/D122).
//
// This probe drives the WRITE path end-to-end AOT-cleanly: it binds a typed payload (TCrud), resolves a
// WriteMapper for the writable Style B view through WriteMapperResolver, applies it against a target
// entity, and persists the result through EF. Once the Phase 3 emitter (tasks 6.x) lands, the generator
// registers a reflection-free generated WriteMapper into GeneratedWriteMapperStore via a
// [ModuleInitializer], and WriteMapperResolver.Resolve returns THAT generated mapper deterministically
// (generated-preferred) with ZERO source changes to this probe.
//
// Keeping the analyzed surface honest (mirrors StyleBExecutableProbe):
//   * WriteMapperResolver.Resolve(view) and the WriteMapper delegate invocation are the ONLY Vista
//     write-path surface under the strict (warning-as-error) trim/AOT analyzer. Resolve is NOT
//     [RequiresUnreferencedCode] — its reflection fallback is confined to a private branch — so an
//     AOT-clean caller that resolves a GENERATED mapper stays free of IL2026/IL3050 (R10.1/R10.2).
//     The public executor write methods (EfViewExecutor.CreateAsync/UpdateAsync) remain
//     [RequiresUnreferencedCode] by design (they resolve TCrud→entity metadata at runtime), so the
//     probe deliberately drives the write-mapper SEAM the executor consults, not those RUC wrappers —
//     exactly as the Phase 2 probe drives the compiled read helpers rather than the RUC ListAsync.
//   * Payload "binding" constructs the TCrud directly (as a source-generated model binder / JSON
//     context would). Reflection-based JSON is [RequiresUnreferencedCode] (D96) and is a separate spec's
//     concern, so the probe stays AOT-clean by building the payload by hand, exactly as the Phase 1/2
//     probes build their inputs by hand.
//   * EF Core provider wire-up, schema creation, seeding, and SaveChanges are framework infrastructure
//     documented as not trim/AOT compatible; they are NOT the generated write-mapper path, so they are
//     isolated in helpers with narrowly-scoped suppressions. Nothing on the generated write surface is
//     suppressed.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using a2n.Vista.Authoring;
using a2n.Vista.Contracts;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Metadata;
using a2n.Vista.Write;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.AotProbe;

/// <summary>
/// Exercises the generated write-mapper path for AOT verification (Task 11.1): bind a payload → resolve a
/// <see cref="WriteMapper"/> via <see cref="WriteMapperResolver"/> → apply it to an entity → persist.
/// </summary>
internal static class GeneratedWriteMapperProbe
{
    /// <summary>
    /// Runs the write probe: seeds a SQLite-backed memo, binds a typed payload, resolves the view's write
    /// mapper (generated-preferred, reflection fallback until the Phase 3 emitter lands), applies it on a
    /// Create shape and an Update shape, and persists — the exact write-mapper surface the analyzer must
    /// find free of IL2026/IL3050 once a generated mapper is registered.
    /// </summary>
    public static async Task RunAsync()
    {
        // Hand-built ViewMetadata (AOT-clean) and captured write facet (AOT-clean expression literals),
        // mirroring the Phase 1/2 probes' hand-built inputs. The facet backs the reflection fallback
        // until the generated mapper registers itself; once it does, the resolver never consults it.
        var view = BuildViewMetadata();
        var facetRegistry = BuildWriteFacetRegistry();

        // The single seam the executor consults on every write. Resolve is AOT-clean: it returns the
        // generated mapper when GeneratedWriteMapperStore has one (deterministically preferred), else the
        // reflection fallback (R10.1/R10.2 — no IL2026/IL3050 on the generated branch).
        var resolver = new WriteMapperResolver(facetRegistry);
        var mapper = resolver.Resolve(view);

        // Report the resolved origin honestly. While the Phase 3 emitter (tasks 6.x) is incomplete the
        // store is empty and the fallback is in use; once the emitter lands this flips to the generated
        // mapper with no change to this probe.
        var generatedPresent = GeneratedWriteMapperStore.TryGet(ProbeMemoView.ViewName, out _);

        Console.WriteLine();
        Console.WriteLine("AOT probe: generated write-mapper path exercised.");
        Console.WriteLine(
            $"WriteMapperResolver.Resolve(\"{ProbeMemoView.ViewName}\") => " +
            $"{(generatedPresent ? "GENERATED mapper (GeneratedWriteMapperStore hit)" : "reflection fallback (generated write mapper pending emitter, tasks 6.x/7.1)")}.");

        // When a generated mapper is present, assert (reflection-free of the mapper's own logic) that its
        // type and method carry no [RequiresUnreferencedCode] / [RequiresDynamicCode] annotation
        // (R10.3/R10.4). Attribute presence checks are AOT-safe (no trim/AOT annotation of their own).
        if (generatedPresent)
        {
            AssertMapperCarriesNoAotBarrierAttributes(mapper);
            Console.WriteLine(
                "Generated mapper type/members carry no [RequiresUnreferencedCode]/[RequiresDynamicCode] (R10.3/R10.4).");
        }

        // --- Drive the write end-to-end through the resolved mapper. ---
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = BuildSeededContext(connection);

        // 1) CREATE shape: bind a payload, apply the mapper to a fresh entity, Add + persist. The mapper
        //    assigns ONLY the whitelisted scalars (Text/Priority/Payload); Id (key) is never assigned.
        var createPayload = new ProbeMemoCrud
        {
            Text = "First memo",
            Priority = 3,
            Payload = new byte[] { 0x01, 0x02, 0x03 },
        };

        var created = new ProbeMemo();
        mapper(createPayload, created);
        context.Memos.Add(created);
        await SaveAsync(context).ConfigureAwait(false);

        // 2) UPDATE shape: load a keyed target within the context, bind an update payload, apply the
        //    mapper (Id stays the loaded row's key — never the body), and persist.
        var target = context.Memos.Single(m => m.Id == created.Id);
        var updatePayload = new ProbeMemoCrud
        {
            Text = "Updated memo",
            Priority = 9,
            Payload = null,
        };

        var keyBeforeUpdate = target.Id;
        mapper(updatePayload, target);
        await SaveAsync(context).ConfigureAwait(false);

        var reloaded = context.Memos.Single(m => m.Id == created.Id);
        Console.WriteLine(
            $"Create => Id={created.Id}, Text=\"{createPayload.Text}\", Priority={createPayload.Priority}; " +
            $"Update => Text=\"{reloaded.Text}\", Priority={reloaded.Priority}, Payload={(reloaded.Payload is null ? "null" : reloaded.Payload.Length + " bytes")}, " +
            $"key preserved={reloaded.Id == keyBeforeUpdate}.");
    }

    /// <summary>
    /// Builds the view metadata the write path consumes, by hand and AOT-clean (no reflection over the
    /// view type). <c>Id</c> is the single key field the mapper must never assign (defense in depth,
    /// R5.1); <c>Text</c>/<c>Priority</c>/<c>Payload</c> are the whitelisted scalar targets.
    /// </summary>
    private static ViewMetadata BuildViewMetadata()
    {
        var fields = new[]
        {
            FieldMetadata.Create("Id", typeof(int), isPrimaryKey: true),
            FieldMetadata.Create("Text", typeof(string)),
            FieldMetadata.Create("Priority", typeof(int)),
        };

        return new ViewMetadata(
            Name: ProbeMemoView.ViewName,
            Route: "/api/views/" + ProbeMemoView.ViewName,
            QueryType: typeof(ProbeMemoRow),
            CrudType: typeof(ProbeMemoCrud),
            CrudEntityType: typeof(ProbeMemo),
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: false)
        {
            KeyFields = new[] { "Id" },
        };
    }

    /// <summary>
    /// Builds and populates a Core <see cref="WriteFacetRegistry"/> with the probe view's captured write
    /// facet (the same three ordered <c>MapWritable</c> mappings the view declares), all as AOT-clean
    /// expression literals. This backs the reflection fallback while the Phase 3 emitter is incomplete;
    /// once the generated mapper registers itself into <see cref="GeneratedWriteMapperStore"/> the
    /// resolver returns it and never consults this registry.
    /// </summary>
    private static IWriteFacetRegistry BuildWriteFacetRegistry()
    {
        var mappings = new[]
        {
            new WritableFieldMapping(
                CrudMember: nameof(ProbeMemoCrud.Text),
                EntityMember: nameof(ProbeMemo.Text),
                From: (Expression<Func<ProbeMemoCrud, string>>)(c => c.Text),
                To: (Expression<Func<ProbeMemo, string>>)(e => e.Text)),
            new WritableFieldMapping(
                CrudMember: nameof(ProbeMemoCrud.Priority),
                EntityMember: nameof(ProbeMemo.Priority),
                From: (Expression<Func<ProbeMemoCrud, int>>)(c => c.Priority),
                To: (Expression<Func<ProbeMemo, int>>)(e => e.Priority)),
            new WritableFieldMapping(
                CrudMember: nameof(ProbeMemoCrud.Payload),
                EntityMember: nameof(ProbeMemo.Payload),
                From: (Expression<Func<ProbeMemoCrud, byte[]?>>)(c => c.Payload),
                To: (Expression<Func<ProbeMemo, byte[]?>>)(e => e.Payload)),
        };

        var facet = new CrudFacetDefinition(
            CrudType: typeof(ProbeMemoCrud),
            EntityType: typeof(ProbeMemo),
            WritableFields: mappings,
            ConcurrencyToken: null,
            AllowsBulk: false);

        var registry = new WriteFacetRegistry();
        registry.Register(ProbeMemoView.ViewName, facet);
        return registry;
    }

    /// <summary>
    /// Asserts, without invoking the mapper's own logic, that the generated <see cref="WriteMapper"/>'s
    /// backing method and declaring type carry neither <see cref="RequiresUnreferencedCodeAttribute"/>
    /// nor <see cref="RequiresDynamicCodeAttribute"/> (R10.3/R10.4). Attribute-presence checks via
    /// <see cref="Attribute.IsDefined(MemberInfo, Type)"/> are AOT-safe.
    /// </summary>
    private static void AssertMapperCarriesNoAotBarrierAttributes(WriteMapper mapper)
    {
        var method = mapper.Method;
        var declaringType = method.DeclaringType;

        var offenders =
            HasBarrier(method) ||
            (declaringType is not null &&
             (Attribute.IsDefined(declaringType, typeof(RequiresUnreferencedCodeAttribute)) ||
              Attribute.IsDefined(declaringType, typeof(RequiresDynamicCodeAttribute))));

        if (offenders)
        {
            throw new InvalidOperationException(
                "The generated write mapper must carry no [RequiresUnreferencedCode]/[RequiresDynamicCode] " +
                $"on its type or members (R10.3/R10.4), but '{declaringType?.FullName}.{method.Name}' does.");
        }

        static bool HasBarrier(MemberInfo member) =>
            Attribute.IsDefined(member, typeof(RequiresUnreferencedCodeAttribute)) ||
            Attribute.IsDefined(member, typeof(RequiresDynamicCodeAttribute));
    }

    /// <summary>
    /// Builds a <see cref="ProbeMemoDbContext"/> over the supplied open SQLite connection and creates the
    /// schema. Isolated from the generated-path call sites because EF Core's provider/options/migration
    /// surface is framework infrastructure documented as not trim/AOT compatible — it is not the
    /// generated write-mapper path this probe verifies (R10.1/R10.2).
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "EF Core provider/schema setup is framework infrastructure, not the generated write-mapper path under AOT verification (R10.1/R10.2).")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "EF Core provider/schema setup is framework infrastructure, not the generated write-mapper path under AOT verification (R10.1/R10.2).")]
    private static ProbeMemoDbContext BuildSeededContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ProbeMemoDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ProbeMemoDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    /// <summary>
    /// Persists pending changes. <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> is EF Core
    /// framework infrastructure (not the generated write-mapper path), so the call is isolated here with
    /// narrowly-scoped suppressions.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' may break when trimming",
        Justification = "EF Core SaveChanges is framework infrastructure, not the generated write-mapper path under AOT verification (R10.1/R10.2).")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:Members annotated with 'RequiresDynamicCodeAttribute' may break when AOT compiling",
        Justification = "EF Core SaveChanges is framework infrastructure, not the generated write-mapper path under AOT verification (R10.1/R10.2).")]
    private static Task SaveAsync(ProbeMemoDbContext context) =>
        context.SaveChangesAsync(CancellationToken.None);
}
