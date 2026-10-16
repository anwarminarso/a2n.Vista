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
/// iterations on worker threads). No other test context is warmed here:
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
    /// <summary>
    /// Assembly-qualified CLR names (<c>FullName</c>, nested types joined with <c>+</c>) of the
    /// contexts constructed inside a parallel sample. <see cref="WarmAll"/> warms this set and nothing else.
    /// </summary>
    internal static readonly IReadOnlyList<string> InventoriedContextNames =
    [
        "a2n.Vista.Tests.CompositeKeyOrderIndependencePropertyTests+CompositeKeyContext",
        "a2n.Vista.Tests.DeletePrecisionPropertyTests+DeleteContext",
        "a2n.Vista.Examples.StyleBExecP3.P3TestDbContext",
        "a2n.Vista.Tests.DisallowedFieldRejectionPropertyTests+P4TestDbContext",
        "a2n.Vista.Tests.P1ParityDbContext",
        "a2n.Vista.Tests.HttpSurfaceParityDbContext",
        "a2n.Vista.Tests.ConditionalMaskingPropertyTests+P5MaskingDbContext",
        "a2n.Vista.Tests.RequestKeyAuthorityPropertyTests+SpoofContext",
        "a2n.Vista.Examples.StyleBExecP7.P7TestDbContext",
        "a2n.Vista.Tests.ListPageBoundPropertyTests+P2ListDbContext",
        "a2n.Vista.Tests.NonProbeableMaskedFieldPropertyTests+P6Db",
    ];

    private static readonly Assembly[] CandidateAssemblies =
    [
        typeof(RelationalModelWarmup).Assembly,
        typeof(P3TestDbContext).Assembly,
        typeof(P7TestDbContext).Assembly,
    ];

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
        var warmed = new Type[InventoriedContextNames.Count];
        for (var i = 0; i < InventoriedContextNames.Count; i++)
        {
            var contextName = InventoriedContextNames[i];
            var contextType = Resolve(contextName);
            try
            {
                Warm(contextType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"RelationalModelWarmup failed while finalizing '{contextType.FullName}'.",
                    ex);
            }

            warmed[i] = contextType;
        }

        warmedContextTypes = warmed;
    }

    private static Type Resolve(string contextName)
    {
        foreach (var assembly in CandidateAssemblies)
        {
            var type = assembly.GetType(contextName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        throw new InvalidOperationException(
            $"RelationalModelWarmup could not resolve inventoried context '{contextName}'.");
    }

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
