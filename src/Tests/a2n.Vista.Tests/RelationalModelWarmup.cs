// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Reflection;
using System.Runtime.CompilerServices;
using a2n.Vista.Examples.StyleBExecP3;
using a2n.Vista.Examples.StyleBExecP7;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.Tests;

/// <summary>
/// Finalizes EF relational models on one thread before TUnit or CsCheck run anything in parallel.
/// </summary>
/// <remarks>
/// <para>
/// EF Core 9 and 10 build the relational model on the first <c>GetRelationalModel</c> call, and the
/// pinned packages (EF 9.0.6, EF 10.0.0) can throw <c>ModelNotFinalized</c> when two threads take that
/// first step on the same model (dotnet/efcore#39214). EF 8.0.11 builds the model during context
/// initialization, so it does not have this window. A <c>DbConnection</c> is not part of the SQLite
/// service-provider cache key, so every context of a given type shares one runtime model. One
/// single-threaded <c>EnsureCreated</c> per type publishes that model; later parallel calls take the
/// fast path.
/// </para>
/// <para>
/// Contexts constructed inside a CsCheck <c>Sample</c> (CsCheck runs iterations on worker threads) or
/// another parallel sample:
/// </para>
/// <list type="bullet">
/// <item><see cref="CompositeKeyOrderIndependencePropertyTests"/> — <c>CompositeKeyContext</c></item>
/// <item><c>DeletePrecisionPropertyTests</c> — <c>DeleteContext</c></item>
/// <item><c>DetailByKeyRoundTripPropertyTests</c> — <see cref="P3TestDbContext"/> (fixture assembly)</item>
/// <item><c>DisallowedFieldRejectionPropertyTests</c> — <c>P4TestDbContext</c></item>
/// <item><c>GeneratedRucParityPropertyTests</c> — <c>P1ParityDbContext</c></item>
/// <item><c>HttpSurfaceOracleParityPropertyTests</c> — <c>HttpSurfaceParityDbContext</c></item>
/// <item><c>ConditionalMaskingPropertyTests</c> — <c>P5MaskingDbContext</c></item>
/// <item><c>RequestKeyAuthorityPropertyTests</c> — <c>SpoofContext</c></item>
/// <item><c>SingleSourcePkDerivationPropertyTests</c> — <see cref="P7TestDbContext"/> (fixture assembly)</item>
/// <item><c>ListPageBoundPropertyTests</c> — <c>P2ListDbContext</c></item>
/// <item><c>NonProbeableMaskedFieldPropertyTests</c> — <c>P6Db</c></item>
/// </list>
/// <para>
/// The same hook also finalizes every other concrete <see cref="DbContext"/> in this test assembly.
/// TUnit runs test methods in parallel, and several of those types (for example <c>WidgetContext</c>)
/// are first-touched from more than one test. Property tests that build one context on the calling
/// thread before <c>Sample</c> — <c>ConcurrencyAbortPropertyTests</c>,
/// <c>WriteResponseMinimalityPropertyTests</c>, <c>RejectedWriteStatePreservationPropertyTests</c>,
/// <c>ErrorEnvelopeConformancePropertyTests</c>, <c>NotFoundIndistinguishabilityPropertyTests</c> —
/// do not create a context inside the sample; they are still covered because their context types live
/// in this assembly.
/// </para>
/// </remarks>
internal static class RelationalModelWarmup
{
    /// <summary>
    /// Builds each concrete test <see cref="DbContext"/> once, on this thread, and calls
    /// <c>EnsureCreated</c> so the shared relational model is published before parallel work.
    /// </summary>
    [ModuleInitializer]
    internal static void WarmAll()
    {
        var seen = new HashSet<Type>();
        foreach (var type in typeof(RelationalModelWarmup).Assembly.GetTypes())
        {
            if (IsConcreteContext(type))
            {
                seen.Add(type);
            }
        }

        // These two live in fixture assemblies. Parallel samples in this test assembly construct them.
        seen.Add(typeof(P3TestDbContext));
        seen.Add(typeof(P7TestDbContext));

        foreach (var type in seen)
        {
            Warm(type);
        }
    }

    private static bool IsConcreteContext(Type type) =>
        type is { IsAbstract: false, IsGenericTypeDefinition: false }
        && typeof(DbContext).IsAssignableFrom(type);

    private static void Warm(Type contextType)
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var builderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(builderType)!;
        builder.UseSqlite(connection);
        // DbContextOptionsBuilder<T>.Options hides the base property; ask for the declared one.
        var options = builderType
                .GetProperty(nameof(DbContextOptionsBuilder.Options), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
                .GetValue(builder)
            ?? throw new InvalidOperationException($"Options for '{contextType.FullName}' were null.");

        var ctor = contextType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(candidate => candidate.GetParameters().Any(parameter =>
                typeof(DbContextOptions).IsAssignableFrom(parameter.ParameterType)))
            .OrderByDescending(candidate => candidate.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Test context '{contextType.FullName}' has no constructor that takes DbContextOptions, " +
                "so RelationalModelWarmup cannot finalize its relational model.");

        var args = new object[ctor.GetParameters().Length];
        var parameters = ctor.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            args[i] = CreateArgument(parameters[i].ParameterType, options, contextType);
        }

        using var context = (DbContext)ctor.Invoke(args);
        try
        {
            context.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"RelationalModelWarmup failed while finalizing '{contextType.FullName}'.",
                ex);
        }
    }

    private static object CreateArgument(Type parameterType, object options, Type contextType)
    {
        if (parameterType.IsInstanceOfType(options))
        {
            return options;
        }

        if (parameterType.IsAbstract || parameterType.IsInterface)
        {
            throw new InvalidOperationException(
                $"Test context '{contextType.FullName}' constructor parameter '{parameterType.FullName}' " +
                "is abstract, so RelationalModelWarmup cannot construct it.");
        }

        return Activator.CreateInstance(parameterType, nonPublic: true)
            ?? throw new InvalidOperationException(
                $"Could not construct '{parameterType.FullName}' while warming '{contextType.FullName}'.");
    }
}
