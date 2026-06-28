using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using a2n.Vista.Export;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for the <see cref="ExportColumns.Value(string, object?, string)"/> coexistence overload
/// (Decision Log D117): it prefers a generated accessor registered in <see cref="ViewAccessorRegistry"/>
/// and falls back to the reflection read only when no generated accessor exists. Each test uses a unique
/// view name so the process-wide static store stays isolated across tests.
/// </summary>
public sealed class ExportColumnsValueTests
{
    private sealed record Row(int Id, string Name);

    private static string UniqueViewName(string hint) => $"{hint}-{Guid.NewGuid():N}";

    [Test]
    public async Task Value_Prefers_Registered_Generated_Accessor()
    {
        var view = UniqueViewName("customers");
        var map = new Dictionary<string, Func<object, object?>>(StringComparer.Ordinal)
        {
            // A sentinel accessor that ignores the row, proving the registry path is taken (not reflection).
            ["Name"] = _ => "from-accessor",
        };
        ViewAccessorRegistry.Register(view, map);

        var value = ExportColumns.Value(view, new Row(1, "from-reflection"), "Name");

        await Assert.That(value).IsEqualTo((object?)"from-accessor");
    }

    [Test]
    public async Task Value_Falls_Back_To_Reflection_When_No_Accessor()
    {
        // A view name that was never registered → no generated accessor → reflection read.
        var view = UniqueViewName("orders");

        var id = ExportColumns.Value(view, new Row(42, "Ada"), "Id");
        var name = ExportColumns.Value(view, new Row(42, "Ada"), "Name");

        await Assert.That(id).IsEqualTo((object?)42);
        await Assert.That(name).IsEqualTo((object?)"Ada");
    }

    [Test]
    public async Task Value_Returns_Null_For_Null_Row()
    {
        var view = UniqueViewName("empty");

        var value = ExportColumns.Value(view, null, "Id");

        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Value_Returns_Null_For_Unknown_Field_On_Reflection_Fallback()
    {
        var view = UniqueViewName("missing-field");

        var value = ExportColumns.Value(view, new Row(1, "Ada"), "DoesNotExist");

        await Assert.That(value).IsNull();
    }

    [Test]
    public async Task Value_Null_Arguments_Throw()
    {
        await Assert.That(() => ExportColumns.Value(null!, new Row(1, "Ada"), "Id"))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ExportColumns.Value("v", new Row(1, "Ada"), null!))
            .Throws<ArgumentNullException>();
    }
}
