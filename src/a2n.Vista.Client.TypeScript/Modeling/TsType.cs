namespace a2n.Vista.Client.TypeScript.Modeling;

/// <summary>
/// The minimal set of TypeScript scalar/primitive kinds the generator emits (design "Scalar type
/// mapping table"). These are the leaves of the <see cref="TsType"/> expression tree.
/// </summary>
public enum TsPrimitiveKind
{
    /// <summary>The TypeScript <c>number</c> type (OpenAPI <c>integer</c>/<c>number</c>).</summary>
    Number,

    /// <summary>The TypeScript <c>boolean</c> type.</summary>
    Boolean,

    /// <summary>The TypeScript <c>string</c> type.</summary>
    String,

    /// <summary>
    /// The TypeScript <c>unknown</c> type, used for permissive <c>{}</c> members (Requirement 3.6) and
    /// unrecognized scalar <c>type</c>/<c>format</c> combinations (Requirement 3.7).
    /// </summary>
    Unknown,

    /// <summary>The TypeScript <c>null</c> literal type, used as a member of a nullable union (Requirement 3.3).</summary>
    Null,
}

/// <summary>
/// A minimal, immutable representation of a TypeScript <em>type expression</em> (a type reference, not a
/// declaration). It is deliberately just large enough for the scalar mapper (<see cref="TypeMapper"/>) and
/// the later emitters: a named reference, a primitive, a string-literal union, an array-of, and a
/// nullable union. Optionality (the property <c>?</c> modifier) is intentionally <em>not</em> part of a
/// type — it is a property-level concern carried by <see cref="TsProperty"/>.
/// </summary>
/// <remarks>
/// <see cref="Render"/> produces the deterministic TypeScript source for the expression. It is a pure
/// function of the value, so it is safe for the emitters to reuse and for property-based tests to assert
/// against (Requirement 9).
/// </remarks>
public abstract record TsType
{
    // Non-public constructor so the closed hierarchy lives entirely in this file.
    private protected TsType()
    {
    }

    /// <summary>The TypeScript <c>number</c> type.</summary>
    public static TsType Number { get; } = new TsPrimitive(TsPrimitiveKind.Number);

    /// <summary>The TypeScript <c>boolean</c> type.</summary>
    public static TsType Boolean { get; } = new TsPrimitive(TsPrimitiveKind.Boolean);

    /// <summary>The TypeScript <c>string</c> type.</summary>
    public static TsType String { get; } = new TsPrimitive(TsPrimitiveKind.String);

    /// <summary>The TypeScript <c>unknown</c> type.</summary>
    public static TsType Unknown { get; } = new TsPrimitive(TsPrimitiveKind.Unknown);

    /// <summary>The TypeScript <c>null</c> literal type.</summary>
    public static TsType Null { get; } = new TsPrimitive(TsPrimitiveKind.Null);

    /// <summary>Creates a reference to a named, declared type (e.g. <c>CustomerRow</c>, <c>FilterNode</c>).</summary>
    /// <param name="name">The declared type name, used verbatim.</param>
    public static TsType Named(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new TsNamed(name);
    }

    /// <summary>Creates an array type <c>T[]</c> over the supplied element type.</summary>
    /// <param name="element">The element type <c>T</c>.</param>
    public static TsType ArrayOf(TsType element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return new TsArray(element);
    }

    /// <summary>
    /// Creates a generic-application type expression, e.g. <c>ViewListResult&lt;CustomerRow&gt;</c>. This is
    /// how the operation-graph step (task 7.5) references the single re-lifted generic
    /// <c>ViewListResult&lt;TRow&gt;</c> envelope (Requirement 2.6) bound to a view's row type, without
    /// duplicating the envelope per view.
    /// </summary>
    /// <param name="name">The generic type name, used verbatim (e.g. <c>ViewListResult</c>).</param>
    /// <param name="arguments">The type arguments in declaration order; must be non-empty.</param>
    public static TsType Generic(string name, IEnumerable<TsType> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(arguments);
        var args = arguments.ToArray();
        if (args.Length == 0)
        {
            throw new ArgumentException("A generic application must have at least one type argument.", nameof(arguments));
        }

        return new TsGeneric(name, args);
    }

    /// <summary>
    /// Creates a string-literal union (e.g. <c>"Equals" | "NotEquals"</c>) with the literals in the exact
    /// order supplied, preserving document order (Requirement 3.2). The set of members is used verbatim.
    /// </summary>
    /// <param name="literals">The literal values, in document order; must be non-empty.</param>
    public static TsType LiteralUnion(IEnumerable<string> literals)
    {
        ArgumentNullException.ThrowIfNull(literals);
        var values = literals.ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("A literal union must contain at least one literal.", nameof(literals));
        }

        return new TsLiteralUnion(values);
    }

    /// <summary>
    /// Wraps a type in a nullable union that includes <c>null</c> (Requirement 3.3). Wrapping is idempotent
    /// and never nests: a value that already admits <c>null</c> (a <see cref="TsNullable"/>, or the
    /// <c>unknown</c>/<c>null</c> primitives, since <c>unknown</c> already subsumes <c>null</c>) is returned
    /// unchanged so the emitted union stays canonical and deterministic.
    /// </summary>
    /// <param name="inner">The type to make nullable.</param>
    public static TsType NullableOf(TsType inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return inner switch
        {
            TsNullable => inner,
            TsPrimitive { Kind: TsPrimitiveKind.Unknown } => inner,
            TsPrimitive { Kind: TsPrimitiveKind.Null } => inner,
            _ => new TsNullable(inner),
        };
    }

    /// <summary>Renders this type as deterministic TypeScript source.</summary>
    public abstract string Render();
}

/// <summary>A primitive TypeScript type (<c>number</c>/<c>boolean</c>/<c>string</c>/<c>unknown</c>/<c>null</c>).</summary>
/// <param name="Kind">The primitive kind.</param>
public sealed record TsPrimitive(TsPrimitiveKind Kind) : TsType
{
    /// <inheritdoc />
    public override string Render() => Kind switch
    {
        TsPrimitiveKind.Number => "number",
        TsPrimitiveKind.Boolean => "boolean",
        TsPrimitiveKind.String => "string",
        TsPrimitiveKind.Unknown => "unknown",
        TsPrimitiveKind.Null => "null",
        _ => throw new InvalidOperationException($"Unhandled primitive kind '{Kind}'."),
    };
}

/// <summary>A reference to a named, declared type; emitted and referenced by name (Requirement 2.5).</summary>
/// <param name="Name">The declared type name.</param>
public sealed record TsNamed(string Name) : TsType
{
    /// <inheritdoc />
    public override string Render() => Name;
}

/// <summary>An array type <c>T[]</c>. Union element types are parenthesized so the <c>[]</c> binds correctly.</summary>
/// <param name="Element">The element type <c>T</c>.</param>
public sealed record TsArray(TsType Element) : TsType
{
    /// <inheritdoc />
    public override string Render()
    {
        // `A | B` and `T | null` must be parenthesized before `[]`, otherwise `A | B[]` parses as
        // `A | (B[])`. Named/primitive/array elements need no parentheses.
        var needsParens = Element is TsLiteralUnion or TsNullable;
        var element = Element.Render();
        return needsParens ? $"({element})[]" : $"{element}[]";
    }
}

/// <summary>
/// A string-literal union in document order (Requirement 3.2). Provides value (sequence) equality so two
/// unions with the same literals in the same order compare equal, which matters for property-based tests.
/// </summary>
/// <param name="Literals">The literal values, in document order.</param>
public sealed record TsLiteralUnion(IReadOnlyList<string> Literals) : TsType
{
    /// <inheritdoc />
    public override string Render() => string.Join(" | ", Literals.Select(EmitLiteral));

    // Emit a double-quoted TypeScript string literal, escaping the characters that would otherwise break
    // the literal. Kept deliberately small: the Vista enum members are simple identifiers, but escaping
    // keeps the emitter robust and deterministic.
    private static string EmitLiteral(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    /// <inheritdoc />
    public bool Equals(TsLiteralUnion? other) =>
        other is not null && Literals.SequenceEqual(other.Literals, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var literal in Literals)
        {
            hash.Add(literal, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

/// <summary>A nullable union <c>T | null</c> (Requirement 3.3).</summary>
/// <param name="Inner">The non-null member of the union.</param>
public sealed record TsNullable(TsType Inner) : TsType
{
    /// <inheritdoc />
    public override string Render() => $"{Inner.Render()} | null";
}

/// <summary>
/// A generic-application type expression <c>Name&lt;A, B, …&gt;</c> (for example
/// <c>ViewListResult&lt;CustomerRow&gt;</c>). Provides value (sequence) equality over the type arguments so
/// two applications of the same generic to the same arguments compare equal, which matters for
/// property-based tests and deterministic emission (Requirement 9).
/// </summary>
/// <param name="Name">The generic type name, used verbatim.</param>
/// <param name="Arguments">The type arguments in declaration order.</param>
public sealed record TsGeneric(string Name, IReadOnlyList<TsType> Arguments) : TsType
{
    /// <inheritdoc />
    public override string Render() => $"{Name}<{string.Join(", ", Arguments.Select(argument => argument.Render()))}>";

    /// <inheritdoc />
    public bool Equals(TsGeneric? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && Arguments.SequenceEqual(other.Arguments);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        foreach (var argument in Arguments)
        {
            hash.Add(argument);
        }

        return hash.ToHashCode();
    }
}
