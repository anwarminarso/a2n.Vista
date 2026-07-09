// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Linq;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Write;
using CsCheck;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Property-based test for the first-wins idempotence of <see cref="GeneratedWriteMapperStore"/>
/// (source-generator-write-mapper task 8.1; Decision Log D121). The store is the process-wide,
/// thread-safe sink the generated <c>[ModuleInitializer]</c>s populate at assembly load, keyed by the
/// view's runtime name. Because a view's module may be initialized more than once in a process, the
/// store must keep the <em>first</em> mapper registered under each name, ignore later registrations for
/// that name, and never disturb mappers registered under other names (Requirement R6.3).
/// </summary>
/// <remarks>
/// <see cref="GeneratedWriteMapperStore"/> is a process-wide static store, so every generated case uses
/// a fresh <see cref="Guid"/>-unique name prefix to stay isolated from sibling tests and from any
/// module-initializer registrations already present in the process. Each registered mapper carries a
/// unique integer tag it stamps into a one-element <c>int[]</c> "entity" when invoked, so the stored
/// mapper's identity is observable without reflection: invoking it reveals which registration won.
/// </remarks>
public sealed class StoreFirstWinsIdempotencePropertyTests
{
    /// <summary>Minimum generated cases required for the property (design Testing Strategy).</summary>
    private const int Iterations = 100;

    /// <summary>Model argument is unused by the tag mappers; a shared sentinel avoids per-call allocation.</summary>
    private static readonly object UnusedModel = new();

    /// <summary>
    /// Builds a write mapper whose only effect is to stamp <paramref name="tag"/> into a one-element
    /// <c>int[]</c> passed as the entity, making the stored mapper's registration identity observable.
    /// </summary>
    private static WriteMapper TagMapper(int tag) => (_, entity) => ((int[])entity)[0] = tag;

    /// <summary>Invokes a stored mapper against a fresh box and returns the tag it stamped.</summary>
    private static int RevealTag(WriteMapper mapper)
    {
        var box = new int[1];
        mapper(UnusedModel, box);
        return box[0];
    }

    // Feature: source-generator-write-mapper, Property 4: For any sequence of registrations into
    // GeneratedWriteMapperStore (with repeated view names, in any order and count), the store retains the
    // first mapper registered under each name, discards later registrations for that name, and leaves
    // mappers registered under other names unchanged.
    //
    // Validates: Requirements 6.3
    [Test]
    public void Store_Retains_First_Registration_Per_Name_And_Leaves_Other_Names_Unchanged()
    {
        // A case is: how many distinct view names participate, and the ordered registration sequence
        // (each element names which of those views is being registered). Repeats within the sequence are
        // exactly the "same name registered again" scenario the first-wins rule must survive.
        var genCase =
            from nameCount in Gen.Int[1, 4]
            from sequence in Gen.Int[0, nameCount - 1].List[1, 15]
            select (nameCount, sequence);

        genCase.Sample(
            tuple =>
            {
                var (nameCount, sequence) = tuple;

                // Guid-unique per case so this case's names never collide with any other case, test, or
                // pre-existing module-initializer registration in the process.
                var prefix = Guid.NewGuid().ToString("N");
                var names = Enumerable
                    .Range(0, nameCount)
                    .Select(i => $"prop4-{prefix}-{i}")
                    .ToArray();

                // The expected winner per name = the tag of its FIRST registration in declaration order.
                var expectedFirstTag = new int?[nameCount];

                for (var seq = 0; seq < sequence.Count; seq++)
                {
                    var nameIndex = sequence[seq];
                    var tag = seq; // Unique per registration, so the winner is unambiguous.

                    GeneratedWriteMapperStore.Add(names[nameIndex], TagMapper(tag));

                    expectedFirstTag[nameIndex] ??= tag;
                }

                // First-wins retention + later-registration discard: every name that was registered at
                // least once must resolve to the mapper from its FIRST registration, regardless of how
                // many later registrations targeted the same name.
                for (var i = 0; i < nameCount; i++)
                {
                    if (expectedFirstTag[i] is not int expected)
                    {
                        continue; // This name never appeared in the sequence.
                    }

                    if (!GeneratedWriteMapperStore.TryGet(names[i], out var stored))
                    {
                        throw new Exception(
                            $"Expected a mapper registered for '{names[i]}', but the store had none.");
                    }

                    var actual = RevealTag(stored);
                    if (actual != expected)
                    {
                        throw new Exception(
                            $"Name '{names[i]}' retained the mapper tagged {actual}, expected the " +
                            $"first-registered mapper tagged {expected} (first-wins violated).");
                    }
                }

                // Non-interference: a fresh name that was never registered in this case must be absent —
                // no registration under any participating name may leak onto an unrelated name.
                var neverRegistered = $"prop4-{prefix}-absent";
                if (GeneratedWriteMapperStore.TryGet(neverRegistered, out _))
                {
                    throw new Exception(
                        $"Unregistered name '{neverRegistered}' unexpectedly resolved to a mapper; " +
                        "registrations must not affect other names.");
                }
            },
            iter: Iterations);
    }
}
