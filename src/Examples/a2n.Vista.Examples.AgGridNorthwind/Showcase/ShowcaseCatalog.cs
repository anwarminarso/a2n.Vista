using System.Text;
using a2n.Vista.Metadata;
using a2n.Vista.Ports;

namespace a2n.Vista.Examples.AgGridNorthwind.Showcase;

/// <summary>
/// A single browsable-view entry supplied to the View Browser page's selector. This is an
/// app-level (example-host) DTO only — it is <b>not</b> a Vista package contract, and it adds no
/// new view route, request/response envelope, or error shape (Decision D138, Requirements R4.4).
/// </summary>
/// <param name="Name">The unique, registered view name (e.g. <c>vOrderDetail</c>).</param>
/// <param name="Route">
/// The full route at which the view is served, read verbatim from <see cref="ViewMetadata.Route"/>
/// (composed at registration per Decision D101/D103) — the client never composes routes by hand.
/// </param>
/// <param name="Title">
/// A human-readable display title derived from <see cref="Name"/> (Vista has no view-level display
/// title). Satisfies "a human-readable title WHERE available" (Requirement R2.1) without inventing a
/// new server contract.
/// </param>
public sealed record ViewCatalogEntry(string Name, string Route, string Title);

/// <summary>
/// Pure projection of the in-process <see cref="IViewRegistry"/> into the browser-facing view
/// catalog that populates the View Browser page's selector (Decision D138).
/// </summary>
/// <remarks>
/// <para>
/// <b>Secure-by-default.</b> The catalog is a one-to-one projection of <see cref="IViewRegistry.All"/>
/// — exactly the explicitly-registered views — so no arbitrary or unregistered database table can ever
/// appear in it (Requirements R2.6, R4.2).
/// </para>
/// <para>
/// <b>Pure, no I/O.</b> <see cref="Project"/> is a deterministic function of the registry contents,
/// which makes it unit- and property-testable (design Property 2). It performs no I/O and mutates no
/// state; the empty-registry case yields an empty list (Requirement R4.5, projection side).
/// </para>
/// </remarks>
public static class ShowcaseCatalog
{
    /// <summary>
    /// Projects every registered view into a <see cref="ViewCatalogEntry"/> carrying the view's
    /// <see cref="ViewMetadata.Name"/>, <see cref="ViewMetadata.Route"/>, and a derived humanized
    /// <see cref="ViewCatalogEntry.Title"/> — a bijection onto <see cref="IViewRegistry.All"/>.
    /// </summary>
    /// <param name="registry">The in-process view registry (the authoritative catalog source).</param>
    /// <returns>
    /// One entry per registered view, in the registry's enumeration order; an empty list when no views
    /// are registered.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<ViewCatalogEntry> Project(IViewRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var all = registry.All;
        var entries = new List<ViewCatalogEntry>(all.Count);
        foreach (var view in all)
        {
            entries.Add(new ViewCatalogEntry(view.Name, view.Route, Humanize(view.Name)));
        }

        return entries;
    }

    /// <summary>
    /// Derives a human-readable title from a Vista view name: strips a single leading <c>v</c>
    /// naming-convention prefix and inserts spaces at camel/Pascal-case word boundaries
    /// (e.g. <c>vOrderDetail</c> → <c>Order Detail</c>).
    /// </summary>
    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // Strip a single leading 'v' prefix (the Vista view naming convention, e.g. "vOrderDetail")
        // only when it is immediately followed by an uppercase letter, so genuine words that begin
        // with 'v' (e.g. "value") are not mangled.
        var core = name;
        if (core.Length >= 2 && core[0] == 'v' && char.IsUpper(core[1]))
        {
            core = core[1..];
        }

        // Insert a space before each uppercase letter that starts a new word: either it follows a
        // lowercase letter or digit, or it is the last letter of an acronym immediately preceding a
        // lowercase letter (e.g. "HTTPServer" → "HTTP Server").
        var sb = new StringBuilder(core.Length + 8);
        for (var i = 0; i < core.Length; i++)
        {
            var c = core[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = core[i - 1];
                var prevIsLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                var acronymBoundary = char.IsUpper(prev)
                    && i + 1 < core.Length
                    && char.IsLower(core[i + 1]);
                if (prevIsLowerOrDigit || acronymBoundary)
                {
                    sb.Append(' ');
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
