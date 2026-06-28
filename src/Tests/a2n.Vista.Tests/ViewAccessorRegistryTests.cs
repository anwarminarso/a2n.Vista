using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for <see cref="ViewAccessorRegistry"/> (Decision Log D117): a static, thread-safe store of
/// generated read accessors. Verifies idempotent (first-wins) registration and field lookup. Each test
/// uses a unique view name so the process-wide static store stays isolated across tests.
/// </summary>
public sealed class ViewAccessorRegistryTests
{
    private static string UniqueViewName(string hint) => $"{hint}-{Guid.NewGuid():N}";

    private static Dictionary<string, Func<object, object?>> Map(params (string Field, object? Value)[] entries)
    {
        var map = new Dictionary<string, Func<object, object?>>(StringComparer.Ordinal);
        foreach (var (field, value) in entries)
        {
            map[field] = _ => value;
        }

        return map;
    }

    [Test]
    public async Task TryGetAccessor_Returns_Registered_Accessor()
    {
        var view = UniqueViewName("customers");
        ViewAccessorRegistry.Register(view, Map(("Id", 42), ("Name", "Ada")));

        var hasId = ViewAccessorRegistry.TryGetAccessor(view, "Id", out var idAccessor);
        var hasName = ViewAccessorRegistry.TryGetAccessor(view, "Name", out var nameAccessor);

        await Assert.That(hasId).IsTrue();
        await Assert.That(idAccessor!(new object())).IsEqualTo((object?)42);
        await Assert.That(hasName).IsTrue();
        await Assert.That(nameAccessor!(new object())).IsEqualTo((object?)"Ada");
    }

    [Test]
    public async Task TryGetAccessor_Returns_False_For_Unknown_View_Or_Field()
    {
        var view = UniqueViewName("orders");
        ViewAccessorRegistry.Register(view, Map(("Id", 1)));

        var unknownField = ViewAccessorRegistry.TryGetAccessor(view, "Missing", out var fieldAccessor);
        var unknownView = ViewAccessorRegistry.TryGetAccessor(UniqueViewName("nope"), "Id", out var viewAccessor);

        await Assert.That(unknownField).IsFalse();
        await Assert.That(fieldAccessor).IsNull();
        await Assert.That(unknownView).IsFalse();
        await Assert.That(viewAccessor).IsNull();
    }

    [Test]
    public async Task Register_Is_Idempotent_First_Registration_Wins()
    {
        var view = UniqueViewName("products");
        ViewAccessorRegistry.Register(view, Map(("Price", 100)));

        // A repeat registration for the same view name must be ignored (first wins).
        ViewAccessorRegistry.Register(view, Map(("Price", 999)));

        ViewAccessorRegistry.TryGetAccessor(view, "Price", out var accessor);
        await Assert.That(accessor!(new object())).IsEqualTo((object?)100);
    }

    [Test]
    public async Task Register_Null_Arguments_Throw()
    {
        await Assert.That(() => ViewAccessorRegistry.Register(null!, Map()))
            .Throws<ArgumentNullException>();
        await Assert.That(() => ViewAccessorRegistry.Register("v", null!))
            .Throws<ArgumentNullException>();
    }
}
