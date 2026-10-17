// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Reflection;
using a2n.Vista.Examples.StyleBExecP3;
using a2n.Vista.Examples.StyleBExecP7;
using a2n.Vista.GeneratorStyleASample;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Locks <see cref="RelationalModelWarmup.WarmedContextTypes"/> to every concrete
/// <see cref="DbContext"/> the test assembly and its example/test-support references actually contain.
/// </summary>
public sealed class RelationalModelWarmupTests
{
    /// <summary>
    /// Contexts that are real <see cref="DbContext"/> types in the scanned assemblies and are
    /// intentionally left unwarmed. Each entry names the type and why.
    /// </summary>
    private static readonly ContextExclusion[] Exclusions =
    [
        new(typeof(ErrorEnvelopeConformancePropertyTests.ErrorEnvContext), OutsideSample),
        new(typeof(HttpSurfaceR2Tests.R2Context), OutsideSample),
        new(typeof(HttpSurfaceR3Tests.R3Context), OutsideSample),
        new(typeof(HttpSurfaceRouteMappingTests.RouteMapContext), OutsideSample),
        new(typeof(ConcurrencyAbortPropertyTests.ConcurrencyContext), OutsideSample),
        new(typeof(ConcurrencyTokenGuardTests.BackedTokenContext), OutsideSample),
        new(typeof(ConcurrencyTokenGuardTests.UnbackedTokenContext), OutsideSample),
        new(typeof(WritableStyleB501Tests.WritableContext), OutsideSample),
        new(typeof(WriteEndpointAuthorizationExampleTests.AuthContext), OutsideSample),
        new(typeof(WriteEndpointExampleTests.ExampleWriteContext), OutsideSample),
        new(typeof(WritableConcurrencyRaceTests.RaceContext), OutsideSample),
        new(typeof(DateTimeConcurrencyTokenTests.StampContext), OutsideSample),
        new(typeof(StoreBusyWriteTests.BusyContext), OutsideSample),
        new(typeof(SqliteLockUpgradeTests.LockContext), OutsideSample),
        new(typeof(WritePersistenceFailureIntegrationTests.UniqueContext), OutsideSample),
        new(typeof(WritePersistenceFailureIntegrationTests.TokenContext), OutsideSample),
        new(typeof(WritePersistenceFailureIntegrationTests.ForeignKeyContext), OutsideSample),
        new(typeof(WriteResponseMinimalityPropertyTests.MinimalityContext), OutsideSample),
        new(typeof(WriteSaveChangesSemanticsTests.WriteContext), OutsideSample),
#if NET9_0_OR_GREATER
        new(typeof(OpenApiPipelineIntegrationTests.WidgetContext), OutsideSample),
#endif
        new(typeof(OpenApiServingCoexistenceTests.WidgetContext), OutsideSample),
        new(typeof(OpenApiServingTests.WidgetContext), OutsideSample),
        new(typeof(RejectedWriteStatePreservationPropertyTests.RejectContext), OutsideSample),
        new(typeof(NotFoundIndistinguishabilityPropertyTests.ScopedContext), OutsideSample),
        new(typeof(AuditWidgetContext), OutsideSample),
        new(typeof(WidgetContext), OutsideSample),
        new(typeof(FailFastContext), OutsideSample),
        new(typeof(KdContext), OutsideSample),
        new(
            typeof(StyleASampleDbContext),
            "Compile-time Style A generator fixture. Tests do not construct it inside a parallel sample."),
    ];

    private const string OutsideSample =
        "Not constructed inside a CsCheck sample, so RelationalModelWarmup does not finalize it.";

    /// <summary>
    /// The warmed set equals every discovered concrete context minus the explicit exclusions.
    /// </summary>
    [Test]
    public async Task Warmed_Set_Contains_Every_Inventoried_Context()
    {
        var problems = DescribeMismatch(
            DiscoverConcreteContexts(),
            RelationalModelWarmup.WarmedContextTypes,
            Exclusions);

        await Assert.That(string.Join(Environment.NewLine, problems)).IsEmpty();
    }

    /// <summary>
    /// The mismatch detector reports a stale exclusion and a discovered context that was not warmed.
    /// </summary>
    [Test]
    public async Task Mismatch_Detector_Flags_A_Stale_Exclusion_And_A_Missing_Warmup()
    {
        var discovered = DiscoverConcreteContexts();
        var omitted = RelationalModelWarmup.WarmedContextTypes[0];
        var warmed = RelationalModelWarmup.WarmedContextTypes.Skip(1).ToArray();
        var exclusions = Exclusions
            .Append(new ContextExclusion(typeof(object), "not a discovered DbContext"))
            .ToArray();

        var problems = DescribeMismatch(discovered, warmed, exclusions);
        var report = string.Join(Environment.NewLine, problems);

        await Assert.That(report).Contains("Stale exclusion");
        await Assert.That(report).Contains(omitted.FullName!);
    }

    /// <summary>
    /// Concrete (non-abstract, non-generic-definition) <see cref="DbContext"/> subclasses in the test
    /// assembly and in directly referenced example / generator-sample assemblies.
    /// </summary>
    private static IReadOnlyList<Type> DiscoverConcreteContexts()
    {
        var discovered = new List<Type>();
        foreach (var assembly in AssembliesToScan())
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (type is { IsAbstract: false, IsGenericTypeDefinition: false }
                    && typeof(DbContext).IsAssignableFrom(type))
                {
                    discovered.Add(type);
                }
            }
        }

        return discovered;
    }

    private static IEnumerable<global::System.Reflection.Assembly> AssembliesToScan()
    {
        var testAssembly = typeof(RelationalModelWarmupTests).Assembly;
        yield return testAssembly;

        foreach (var reference in testAssembly.GetReferencedAssemblies())
        {
            if (!IsExampleOrTestSupport(reference.Name))
            {
                continue;
            }

            yield return global::System.Reflection.Assembly.Load(reference);
        }
    }

    private static bool IsExampleOrTestSupport(string? name) =>
        name is not null
        && (name.Contains(".Examples.", StringComparison.Ordinal)
            || name.StartsWith("a2n.Vista.Generator", StringComparison.Ordinal)
            || name.StartsWith("a2n.Vista.StyleB", StringComparison.Ordinal));

    private static IEnumerable<Type> SafeGetTypes(global::System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null)!;
        }
    }

    /// <summary>
    /// Returns the problems that keep <paramref name="discovered"/> minus <paramref name="exclusions"/>
    /// from equaling <paramref name="warmed"/>. An exclusion whose type was not discovered is stale.
    /// </summary>
    private static IReadOnlyList<string> DescribeMismatch(
        IReadOnlyCollection<Type> discovered,
        IReadOnlyCollection<Type> warmed,
        IReadOnlyList<ContextExclusion> exclusions)
    {
        var problems = new List<string>();
        var discoveredSet = discovered as ISet<Type> ?? discovered.ToHashSet();
        var excluded = new HashSet<Type>();
        foreach (var exclusion in exclusions)
        {
            if (string.IsNullOrWhiteSpace(exclusion.Reason))
            {
                problems.Add($"Exclusion '{exclusion.Type.FullName}' has no reason.");
            }

            if (!excluded.Add(exclusion.Type))
            {
                problems.Add($"Exclusion '{exclusion.Type.FullName}' is listed more than once.");
            }

            if (!discoveredSet.Contains(exclusion.Type))
            {
                problems.Add(
                    $"Stale exclusion '{exclusion.Type.FullName}': not a discovered concrete DbContext.");
            }
        }

        var expected = discoveredSet.Where(type => !excluded.Contains(type)).ToHashSet();
        var warmedSet = warmed as ISet<Type> ?? warmed.ToHashSet();
        foreach (var missing in expected.Except(warmedSet).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            problems.Add($"Discovered context '{missing.FullName}' is neither warmed nor excluded.");
        }

        foreach (var extra in warmedSet.Except(expected).OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            problems.Add($"Warmed context '{extra.FullName}' is not a discovered non-excluded context.");
        }

        return problems;
    }

    private readonly record struct ContextExclusion(Type Type, string Reason);
}
