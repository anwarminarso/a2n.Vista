using a2n.Vista.Client.TypeScript.Emit;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// Unit tests for <see cref="DeterministicOrder"/>, the single ordering helper the emitters delegate to
/// (Requirement 9.2). They pin the fixed ordinal, case-sensitive comparison, order-independence of the
/// input, and the helper's purity (the source sequence is left untouched).
/// </summary>
public sealed class DeterministicOrderTests
{
    private sealed record Item(string Name, int Payload);

    [Test]
    public async Task Comparer_Is_Ordinal_Case_Sensitive()
    {
        await Assert.That(DeterministicOrder.Comparer).IsSameReferenceAs(StringComparer.Ordinal);
    }

    [Test]
    public async Task OrderNames_Sorts_By_Ordinal_Comparison()
    {
        var ordered = DeterministicOrder.OrderNames(new[] { "banana", "Apple", "apple", "Banana" });

        // Ordinal comparison orders all uppercase code points before lowercase ones ('A' < 'B' < 'a' < 'b').
        await Assert.That(ordered).IsEquivalentTo(new[] { "Apple", "Banana", "apple", "banana" });
    }

    [Test]
    public async Task OrderNames_Is_Independent_Of_Input_Order()
    {
        var names = new[] { "CustomerRow", "FilterNode", "ProblemDetails", "VistaSortBody" };
        var expected = DeterministicOrder.OrderNames(names);

        var permuted = new[] { "VistaSortBody", "ProblemDetails", "CustomerRow", "FilterNode" };
        var actual = DeterministicOrder.OrderNames(permuted);

        await Assert.That(actual).IsEquivalentTo(expected);
    }

    [Test]
    public async Task ByName_Orders_Items_By_Their_Declared_Name()
    {
        var items = new[]
        {
            new Item("delete", 1),
            new Item("create", 2),
            new Item("Update", 3),
        };

        var ordered = DeterministicOrder.ByName(items, i => i.Name);

        // 'U' (0x55) precedes 'c' (0x63) and 'd' (0x64) under ordinal comparison.
        await Assert.That(ordered.Select(i => i.Name).ToArray())
            .IsEquivalentTo(new[] { "Update", "create", "delete" });
        // The payload rides along with its item — ordering is by name only, not a reshuffle.
        await Assert.That(ordered[0].Payload).IsEqualTo(3);
    }

    [Test]
    public async Task ByName_Does_Not_Mutate_The_Source_Sequence()
    {
        var source = new List<Item>
        {
            new("zebra", 1),
            new("alpha", 2),
        };

        _ = DeterministicOrder.ByName(source, i => i.Name);

        // The helper is pure: it allocates a fresh result and leaves the caller's list in its original order.
        await Assert.That(source.Select(i => i.Name).ToArray())
            .IsEquivalentTo(new[] { "zebra", "alpha" });
    }

    [Test]
    public async Task OrderNames_On_Empty_Sequence_Returns_Empty()
    {
        var ordered = DeterministicOrder.OrderNames(Array.Empty<string>());

        await Assert.That(ordered).IsEmpty();
    }
}
