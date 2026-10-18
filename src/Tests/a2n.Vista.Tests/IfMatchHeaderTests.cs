// Licensed to the a2n.Vista project. Published artifact — English only.

using a2n.Vista.Write;
using TUnit.Assertions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>RFC 9110 <c>If-Match</c> parsing: quoted tags, a legacy unquoted token, lists, and <c>*</c>.</summary>
public sealed class IfMatchHeaderTests
{
    [Test]
    public async Task Quoted_Strong_Tag_Is_The_Opaque_Token()
    {
        var parsed = IfMatchHeader.Parse("\"6f0c2c1e-1111-4222-8333-444444444444\"");

        await Assert.That(parsed.IsMissing).IsFalse();
        await Assert.That(parsed.IsWildcard).IsFalse();
        await Assert.That(parsed.IsLegacyUnquoted).IsFalse();
        await Assert.That(parsed.StrongTags.Count).IsEqualTo(1);
        await Assert.That(parsed.StrongTags[0]).IsEqualTo("6f0c2c1e-1111-4222-8333-444444444444");
    }

    [Test]
    public async Task Unquoted_Token_Is_Accepted_As_Legacy()
    {
        var parsed = IfMatchHeader.Parse("42");

        await Assert.That(parsed.IsLegacyUnquoted).IsTrue();
        await Assert.That(parsed.StrongTags.Count).IsEqualTo(1);
        await Assert.That(parsed.StrongTags[0]).IsEqualTo("42");
    }

    [Test]
    public async Task Star_Is_A_Wildcard_With_No_Tag()
    {
        var parsed = IfMatchHeader.Parse("*");

        await Assert.That(parsed.IsWildcard).IsTrue();
        await Assert.That(parsed.HasStrongTag).IsFalse();
    }

    [Test]
    public async Task Quoted_List_Keeps_Every_Strong_Tag_And_Drops_Weak_Tags()
    {
        var parsed = IfMatchHeader.Parse("\"alpha\", W/\"weak\", \"beta\"");

        await Assert.That(parsed.StrongTags.Count).IsEqualTo(2);
        await Assert.That(parsed.StrongTags[0]).IsEqualTo("alpha");
        await Assert.That(parsed.StrongTags[1]).IsEqualTo("beta");
    }

    [Test]
    public async Task Weak_Only_Header_Has_No_Strong_Tag()
    {
        var parsed = IfMatchHeader.Parse("W/\"42\"");

        await Assert.That(parsed.IsWildcard).IsFalse();
        await Assert.That(parsed.HasStrongTag).IsFalse();
    }

    [Test]
    public async Task Missing_Blank_And_Unusable_Headers()
    {
        await Assert.That(IfMatchHeader.Parse(null).IsMissing).IsTrue();
        await Assert.That(IfMatchHeader.Parse("   ").IsMissing).IsTrue();
        await Assert.That(IfMatchHeader.Parse("\"unterminated").HasStrongTag).IsFalse();
        await Assert.That(IfMatchHeader.Parse("\"a\", *").HasStrongTag).IsFalse();
        await Assert.That(IfMatchHeader.Parse("a, b").HasStrongTag).IsFalse();
    }

    [Test]
    public async Task Strong_Entity_Tag_Is_Quoted()
    {
        await Assert.That(IfMatchHeader.ToStrongEntityTag("42")).IsEqualTo("\"42\"");
    }
}
