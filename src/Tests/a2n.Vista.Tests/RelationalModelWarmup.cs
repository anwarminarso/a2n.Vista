// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Reflection;
using System.Runtime.CompilerServices;
using a2n.Vista.Examples.StyleBExecP3;
using a2n.Vista.Examples.StyleBExecP7;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace a2n.Vista.Tests;

/// <summary>
/// Finalizes the relational model of each inventoried test context on one thread before TUnit or
/// CsCheck run anything in parallel.
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
/// The warmed set is exactly the contexts constructed inside a CsCheck <c>Sample</c> (CsCheck runs
/// iterations on worker threads). Each one is named with <c>Warm&lt;T&gt;()</c>. No other test context
/// is warmed here:
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
/// </remarks>
internal static class RelationalModelWarmup
{
    private static Type[] warmedContextTypes = [];

    /// <summary>
    /// The context types <see cref="WarmAll"/> actually finalized, in inventory order.
    /// </summary>
    internal static IReadOnlyList<Type> WarmedContextTypes => warmedContextTypes;

    /// <summary>
    /// Builds each inventoried <see cref="DbContext"/> once, on this thread, and calls
    /// <c>EnsureCreated</c> so the shared relational model is published before parallel work.
    /// </summary>
    [ModuleInitializer]
    internal static void WarmAll()
    {
        warmedContextTypes =
        [
            Warm<CompositeKeyOrderIndependencePropertyTests.CompositeKeyContext>(),
            Warm<DeletePrecisionPropertyTests.DeleteContext>(),
            Warm<P3TestDbContext>(),
            Warm<DisallowedFieldRejectionPropertyTests.P4TestDbContext>(),
            Warm<P1ParityDbContext>(),
            Warm<HttpSurfaceParityDbContext>(),
            Warm<ConditionalMaskingPropertyTests.P5MaskingDbContext>(),
            Warm<RequestKeyAuthorityPropertyTests.SpoofContext>(),
            Warm<P7TestDbContext>(),
            Warm<ListPageBoundPropertyTests.P2ListDbContext>(),
            Warm<NonProbeableMaskedFieldPropertyTests.P6Db>(),
        ];
    }

    private static Type Warm<TContext>()
        where TContext : DbContext
    {
        try
        {
            Finalize<TContext>();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"RelationalModelWarmup failed while finalizing '{typeof(TContext).FullName}'.",
                ex);
        }

        return typeof(TContext);
    }

    private static void Finalize<TContext>()
        where TContext : DbContext
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var builder = new DbContextOptionsBuilder<TContext>();
        builder.UseSqlite(connection);

        var contextType = typeof(TContext);
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
            args[i] = CreateArgument(parameters[i].ParameterType, builder.Options, contextType);
        }

        using var context = (DbContext)ctor.Invoke(args);
        context.Database.EnsureCreated();
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
