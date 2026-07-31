using a2n.Vista.Client.TypeScript.Model;
using a2n.Vista.Client.TypeScript.Pipeline;

namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// Validates the view names derived from the acquired document before anything is emitted. A view name is
/// external input: it comes from an <c>operationId</c> (or a path segment) in a document the generator may
/// have fetched over HTTPS, and it flows into both the emitted <c>views/{name}.ts</c> path and the emitted
/// TypeScript symbols.
/// </summary>
/// <remarks>
/// The file-name transform copies every non-upper-case character verbatim, so a name containing <c>/</c>,
/// <c>\</c> or <c>..</c> would emit a path outside the output directory, and an empty name would throw an
/// unhandled <see cref="ArgumentException"/> from the emitter — contradicting the contract that every fatal
/// cause surfaces as a typed <see cref="GenerationError"/>. Restricting names to a conservative identifier
/// shape closes both at the model stage.
/// </remarks>
public static class ViewNameGuard
{
    /// <summary>
    /// Returns the first typed error for a view whose name is not a safe identifier, in the order the views
    /// are supplied (they are already deterministically ordered), or <see langword="null"/> when every name is
    /// safe.
    /// </summary>
    /// <param name="views">The modeled views to validate.</param>
    public static GenerationError? FirstUnsafe(IReadOnlyList<ViewModel> views)
    {
        ArgumentNullException.ThrowIfNull(views);

        for (var i = 0; i < views.Count; i++)
        {
            var error = Validate(views[i].ViewName);
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>
    /// Validates a single derived view name against <c>[A-Za-z_][A-Za-z0-9_]*</c>, returning the typed error
    /// describing the first violation or <see langword="null"/> when the name is safe.
    /// </summary>
    /// <param name="viewName">The derived view name to validate.</param>
    public static GenerationError? Validate(string? viewName)
    {
        if (string.IsNullOrEmpty(viewName))
        {
            return new GenerationError.UnsafeViewName(
                viewName ?? string.Empty,
                "the document supplies no operationId or view path segment to derive a name from.");
        }

        var first = viewName[0];
        if (!char.IsAsciiLetter(first) && first != '_')
        {
            return new GenerationError.UnsafeViewName(
                viewName,
                "a view name must start with an ASCII letter or underscore.");
        }

        for (var i = 1; i < viewName.Length; i++)
        {
            var c = viewName[i];
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
            {
                return new GenerationError.UnsafeViewName(
                    viewName,
                    $"the character '{c}' is not allowed; a view name must match [A-Za-z_][A-Za-z0-9_]*.");
            }
        }

        return null;
    }
}
