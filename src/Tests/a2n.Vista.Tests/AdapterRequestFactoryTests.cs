using System.IO;
using System.Text;
using System.Threading.Tasks;
using a2n.Vista.AspNetCore.Execution;
using Microsoft.AspNetCore.Http;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Tests for <see cref="AdapterRequestFactory"/> (Decision Log D112): it merges the query string (and
/// form) into the neutral values bag and captures a JSON body when present.
/// </summary>
public sealed class AdapterRequestFactoryTests
{
    [Test]
    public async Task Merges_Query_String_Into_Values()
    {
        var http = new DefaultHttpContext();
        http.Request.QueryString = new QueryString("?draw=1&columns[0][data]=Name");

        var raw = await AdapterRequestFactory.CreateAsync(http, "Widgets");

        await Assert.That(raw.ViewName).IsEqualTo("Widgets");
        await Assert.That(raw.Values.ContainsKey("draw")).IsTrue();
        await Assert.That(raw.Values["draw"][0]).IsEqualTo("1");
        await Assert.That(raw.Values["columns[0][data]"][0]).IsEqualTo("Name");
        await Assert.That(raw.JsonBody).IsNull();
    }

    [Test]
    public async Task Captures_Json_Body()
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = "application/json";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));

        var raw = await AdapterRequestFactory.CreateAsync(http, "Widgets");

        await Assert.That(raw.JsonBody).IsEqualTo("{\"a\":1}");
    }
}
