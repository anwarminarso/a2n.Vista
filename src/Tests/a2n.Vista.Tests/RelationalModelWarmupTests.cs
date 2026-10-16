// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Linq;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Locks <see cref="RelationalModelWarmup"/> to the contexts constructed inside a parallel sample.
/// </summary>
public sealed class RelationalModelWarmupTests
{
    /// <summary>
    /// The 11 contexts the module initializer is required to finalize. Nested types use the CLR
    /// <c>FullName</c> form (<c>Outer+Inner</c>).
    /// </summary>
    private static readonly string[] InventoriedContexts =
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

    /// <summary>
    /// The set actually warmed by the module initializer contains every inventoried context and no others.
    /// </summary>
    [Test]
    public async Task Warmed_Set_Contains_Every_Inventoried_Context()
    {
        var warmed = RelationalModelWarmup.WarmedContextTypes
            .Select(type => type.FullName!)
            .ToArray();

        foreach (var name in InventoriedContexts)
        {
            await Assert.That(warmed).Contains(name);
        }

        await Assert.That(warmed).IsEquivalentTo(InventoriedContexts);
        await Assert.That(RelationalModelWarmup.InventoriedContextNames).IsEquivalentTo(InventoriedContexts);
    }
}
