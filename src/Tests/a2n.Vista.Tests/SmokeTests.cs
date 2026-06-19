using a2n.Vista.Results;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Trivial smoke test proving the TUnit harness runs and can reference the Vista
/// Core contracts. Real coverage (enforcement, default-allow, paging, auth, layering)
/// is added by tasks 12.2-12.7.
/// </summary>
public sealed class SmokeTests
{
    [Test]
    public async Task Harness_Runs_And_Can_Construct_PagedResult()
    {
        var page = new PagedResult<int>(
            Items: new[] { 1, 2, 3 },
            TotalRows: 3,
            PageIndex: 0,
            PageSize: 10,
            TotalPages: 1);

        await Assert.That(page.Items.Count).IsEqualTo(3);
        await Assert.That(page.TotalRows).IsEqualTo(3L);
        await Assert.That(page.PageIndex).IsEqualTo(0);
    }
}
