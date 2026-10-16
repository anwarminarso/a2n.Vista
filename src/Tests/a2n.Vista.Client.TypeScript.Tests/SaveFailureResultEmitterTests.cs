// Licensed to the a2n.Vista project. Published artifact — English only.

using System.Threading.Tasks;
using a2n.Vista.Client.TypeScript.Emit.Runtime;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Client.TypeScript.Tests;

/// <summary>
/// D158: the emitted TypeScript classifier knows the save-failure wire codes and does not emit the
/// retired <c>write-conflict</c> string.
/// </summary>
public sealed class SaveFailureResultEmitterTests
{
    [Test]
    public async Task Emitted_Result_Module_Names_The_Save_Failure_Codes()
    {
        var content = ResultEmitter.Emit().Content;

        await Assert.That(content).Contains("write-concurrency-conflict");
        await Assert.That(content).Contains("write-constraint-violation");
        await Assert.That(content).Contains("write-persistence-failed");
        await Assert.That(content).Contains("constraint-violation");
        await Assert.That(content).Contains("persistence-failed");
        await Assert.That(content.Contains("\"write-conflict\"", System.StringComparison.Ordinal)).IsFalse();
    }
}
