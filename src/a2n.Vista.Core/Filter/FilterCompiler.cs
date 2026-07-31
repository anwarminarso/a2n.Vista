using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Filter;

/// <summary>
/// Compiles a neutral <see cref="FilterNode"/> tree into an <see cref="Expression{TDelegate}"/> of
/// <see cref="Func{T, TResult}"/> (<c>T</c> → <see cref="bool"/>) using only
/// <see cref="System.Linq.Expressions"/>, so Core stays free of any EF/ASP.NET dependency
/// (Requirement R11.1). Before building any expression the compiler enforces the tri-whitelist for
/// the supplied <see cref="FilterOrigin"/> (Requirements R5.5, R5.6, R6.2, R9.2; §8.3); a violation
/// is reported as a <see cref="FilterValidationException"/>, which the AspNetCore layer maps to HTTP
/// 400.
/// </summary>
/// <remarks>
/// <para>
/// <b>Field resolution.</b> A leaf's <see cref="FilterLeaf.Field"/> is matched against
/// <see cref="ViewMetadata.Fields"/> ordinally, then the predicate accesses the property of the same
/// name on <c>T</c> (the projected row type). <c>T</c> is therefore expected to expose a public
/// property named after each projected field.
/// </para>
/// <para>
/// <b>Case-sensitivity (Decision Log D17, §8.2).</b> Case-sensitivity is server-decided, never a
/// client flag. The base string operators (<see cref="BuildContains"/>, <see cref="BuildStartsWith"/>,
/// <see cref="BuildEndsWith"/>) use <see cref="StringComparison.OrdinalIgnoreCase"/>, matching the
/// documented in-memory/test behavior. The EF executor (Task 9.3) derives a subclass and overrides
/// these to emit provider-correct translations (for example <c>EF.Functions.ILike</c> on Npgsql).
/// </para>
/// <para>
/// <b>AOT hygiene (R11.4, §9).</b> The compiler reflects over <c>T</c>'s properties and constructs
/// closed generics at runtime, so its public entry points are marked
/// <see cref="RequiresUnreferencedCodeAttribute"/>; the AOT-clean route is the source generator
/// (Pilar 3).
/// </para>
/// </remarks>
public class FilterCompiler
{
    private static readonly MethodInfo StringContainsMethod =
        typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string), typeof(StringComparison) })!;

    private static readonly MethodInfo StringStartsWithMethod =
        typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string), typeof(StringComparison) })!;

    private static readonly MethodInfo StringEndsWithMethod =
        typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string), typeof(StringComparison) })!;

    private static readonly MethodInfo EnumerableContainsMethod =
        typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

    private readonly IQueryDialect? _dialect;

    /// <summary>
    /// Initializes a <see cref="FilterCompiler"/> with no dialect: text operators use the in-memory
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> path (the test/InMemory path).
    /// </summary>
    public FilterCompiler()
    {
    }

    /// <summary>
    /// Initializes a <see cref="FilterCompiler"/> with an optional provider dialect (Decision Log D107).
    /// When supplied, text operators (<c>Contains</c>/<c>StartsWith</c>/<c>EndsWith</c>) delegate to
    /// <see cref="IQueryDialect.BuildStringMatch"/> (provider-correct SQL <c>LIKE</c>/<c>ILIKE</c> with
    /// wildcard escaping); when <see langword="null"/>, the in-memory ordinal path is used.
    /// </summary>
    /// <param name="dialect">The provider dialect, or <see langword="null"/> for the in-memory path.</param>
    public FilterCompiler(IQueryDialect? dialect)
    {
        _dialect = dialect;
    }

    /// <summary>
    /// Compiles <paramref name="node"/> into a predicate over <typeparamref name="T"/>, validating
    /// every leaf against the whitelist for <paramref name="origin"/> before building.
    /// </summary>
    /// <typeparam name="T">The projected row type the predicate runs against.</typeparam>
    /// <param name="node">The filter tree to compile.</param>
    /// <param name="origin">The whitelist path that governs every leaf in <paramref name="node"/>.</param>
    /// <param name="view">The view metadata supplying field whitelist information.</param>
    /// <returns>A predicate expression equivalent to <paramref name="node"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> or <paramref name="view"/> is <see langword="null"/>.</exception>
    /// <exception cref="FilterValidationException">A leaf violates the whitelist or carries an invalid value.</exception>
    [RequiresUnreferencedCode("Filter compilation reflects over T's properties and builds closed generics at runtime; use the source generator path for AOT.")]
    public Expression<Func<T, bool>> Compile<T>(FilterNode node, FilterOrigin origin, ViewMetadata view)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(view);

        EnforceLimits(node, view.Limits);

        var fields = ViewFieldLookup.For(view);
        var parameter = Expression.Parameter(typeof(T), "x");

        // RUC path: each whitelisted field resolves to a member by reflecting over T with
        // Expression.Property(string). The reflective call lives in this RUC-annotated factory only.
        var body = Build(node, origin, parameter, fields, field => Expression.Property(parameter, field.Name));
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>
    /// Compiles <paramref name="node"/> into a predicate over <typeparamref name="T"/> using
    /// <em>generated</em> member-access lambdas resolved via <paramref name="memberAccessResolver"/>
    /// instead of <c>Expression.Property(string)</c> — the AOT-clean seam for the source-generated
    /// Style B compiled read path (source-generator Phase 2 / Decision Log D118, R2.2/R2.3). The
    /// tri-whitelist (<see cref="FilterOrigin"/>) is enforced identically to the reflection overload, so
    /// disallowed / non-projected / masked-without-opt-in fields are still rejected before any query
    /// executes (R2.4, R8.1).
    /// </summary>
    /// <typeparam name="T">The projected row type the predicate runs against.</typeparam>
    /// <param name="node">The filter tree to compile.</param>
    /// <param name="origin">The whitelist path that governs every leaf in <paramref name="node"/>.</param>
    /// <param name="view">The view metadata supplying field whitelist information.</param>
    /// <param name="memberAccessResolver">
    /// Resolves a field name to its generated member-access lambda
    /// (<c>Expression&lt;Func&lt;T, TField&gt;&gt;</c>), or <see langword="null"/> when the field has no
    /// generated member-access (i.e. it is not a projected field).
    /// </param>
    /// <returns>A predicate expression equivalent to <paramref name="node"/>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="FilterValidationException">A leaf violates the whitelist or carries an invalid value.</exception>
    public Expression<Func<T, bool>> Compile<T>(
        FilterNode node,
        FilterOrigin origin,
        ViewMetadata view,
        Func<string, LambdaExpression?> memberAccessResolver)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(memberAccessResolver);

        EnforceLimits(node, view.Limits);

        var fields = ViewFieldLookup.For(view);
        var parameter = Expression.Parameter(typeof(T), "x");

        // Compiled path: each whitelisted field resolves to its generated member-access lambda, whose
        // single parameter is rebound to the shared predicate parameter. No Expression.Property(string)
        // is reached, keeping this seam AOT-clean.
        var body = Build(node, origin, parameter, fields, field => ResolveGeneratedMember(field, parameter, memberAccessResolver));
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>
    /// Resolves a whitelisted field to a member expression over <paramref name="parameter"/> using the
    /// generated member-access lambda, rebinding the lambda's own parameter to
    /// <paramref name="parameter"/>. A field that passed the whitelist but has no generated member-access
    /// is treated as not part of the projection (R2.4).
    /// </summary>
    private static Expression ResolveGeneratedMember(
        FieldMetadata field,
        ParameterExpression parameter,
        Func<string, LambdaExpression?> memberAccessResolver)
    {
        var accessor = memberAccessResolver(field.Name);
        if (accessor is null)
        {
            throw new FilterValidationException(
                FilterErrorCode.UnknownField,
                $"Field '{field.Name}' has no generated member-access and is not part of the view projection.",
                field.Name);
        }

        return ParameterReplaceVisitor.Replace(accessor.Body, accessor.Parameters[0], parameter);
    }

    private Expression Build(
        FilterNode node,
        FilterOrigin origin,
        ParameterExpression parameter,
        IReadOnlyDictionary<string, FieldMetadata> fields,
        Func<FieldMetadata, Expression> memberFactory)
    {
        return node switch
        {
            FilterLeaf leaf => BuildLeaf(leaf, origin, parameter, fields, memberFactory),
            FilterAnd and => Combine(and.Children, origin, parameter, fields, memberFactory, Expression.AndAlso, seed: true),
            FilterOr or => Combine(or.Children, origin, parameter, fields, memberFactory, Expression.OrElse, seed: false),
            FilterNot not => Expression.Not(Build(not.Child, origin, parameter, fields, memberFactory)),
            _ => throw new FilterValidationException(
                FilterErrorCode.InvalidValue,
                $"Unsupported filter node type '{node.GetType().Name}'."),
        };
    }

    private Expression Combine(
        IReadOnlyList<FilterNode> children,
        FilterOrigin origin,
        ParameterExpression parameter,
        IReadOnlyDictionary<string, FieldMetadata> fields,
        Func<FieldMetadata, Expression> memberFactory,
        Func<Expression, Expression, Expression> combine,
        bool seed)
    {
        // An empty AND is vacuously true; an empty OR is vacuously false.
        if (children.Count == 0)
        {
            return Expression.Constant(seed);
        }

        Expression? acc = null;
        foreach (var child in children)
        {
            var current = Build(child, origin, parameter, fields, memberFactory);
            acc = acc is null ? current : combine(acc, current);
        }

        return acc!;
    }

    private Expression BuildLeaf(
        FilterLeaf leaf,
        FilterOrigin origin,
        ParameterExpression parameter,
        IReadOnlyDictionary<string, FieldMetadata> fields,
        Func<FieldMetadata, Expression> memberFactory)
    {
        var field = ValidateLeaf(leaf, origin, fields);

        var member = memberFactory(field);
        var memberType = member.Type;
        var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;

        return leaf.Op switch
        {
            FilterOperator.Equals => Expression.Equal(member, ConstantFor(leaf, underlying, memberType)),
            FilterOperator.NotEquals => Expression.NotEqual(member, ConstantFor(leaf, underlying, memberType)),
            FilterOperator.GreaterThan => Expression.GreaterThan(member, ConstantFor(leaf, underlying, memberType)),
            FilterOperator.GreaterThanOrEqual => Expression.GreaterThanOrEqual(member, ConstantFor(leaf, underlying, memberType)),
            FilterOperator.LessThan => Expression.LessThan(member, ConstantFor(leaf, underlying, memberType)),
            FilterOperator.LessThanOrEqual => Expression.LessThanOrEqual(member, ConstantFor(leaf, underlying, memberType)),
            FilterOperator.Contains => BuildContains(member, StringValue(leaf), leaf),
            FilterOperator.StartsWith => BuildStartsWith(member, StringValue(leaf), leaf),
            FilterOperator.EndsWith => BuildEndsWith(member, StringValue(leaf), leaf),
            FilterOperator.In => BuildIn(leaf, member, underlying, memberType),
            FilterOperator.Between => BuildBetween(leaf, member, underlying, memberType),
            FilterOperator.IsNull => BuildIsNull(member, memberType),
            _ => throw new FilterValidationException(
                FilterErrorCode.OperatorNotAllowed,
                $"Operator '{leaf.Op}' is not supported for field '{leaf.Field}'.",
                leaf.Field,
                leaf.Op),
        };
    }

    private static FieldMetadata ValidateLeaf(
        FilterLeaf leaf,
        FilterOrigin origin,
        IReadOnlyDictionary<string, FieldMetadata> fields)
    {
        if (!fields.TryGetValue(leaf.Field, out var field))
        {
            throw new FilterValidationException(
                FilterErrorCode.UnknownField,
                $"Field '{leaf.Field}' does not exist in the view projection.",
                leaf.Field,
                leaf.Op);
        }

        if (!IsSingleOperator(leaf.Op))
        {
            throw new FilterValidationException(
                FilterErrorCode.OperatorNotAllowed,
                $"Filter leaf for field '{leaf.Field}' must carry exactly one operator, but was '{leaf.Op}'.",
                leaf.Field,
                leaf.Op);
        }

        switch (origin)
        {
            case FilterOrigin.Filter:
                if (!field.IsFilterable)
                {
                    throw new FilterValidationException(
                        FilterErrorCode.FieldNotAllowed,
                        $"Field '{leaf.Field}' is not filterable.",
                        leaf.Field,
                        leaf.Op);
                }

                if ((field.AllowedOperators & leaf.Op) != leaf.Op)
                {
                    throw new FilterValidationException(
                        FilterErrorCode.OperatorNotAllowed,
                        $"Operator '{leaf.Op}' is not allowed on field '{leaf.Field}'.",
                        leaf.Field,
                        leaf.Op);
                }

                break;

            case FilterOrigin.Search:
                if (!field.IsSearchable || field.ClrType != typeof(string))
                {
                    throw new FilterValidationException(
                        FilterErrorCode.FieldNotAllowed,
                        $"Field '{leaf.Field}' does not participate in global search.",
                        leaf.Field,
                        leaf.Op);
                }

                if (leaf.Op != FilterOperator.Contains)
                {
                    throw new FilterValidationException(
                        FilterErrorCode.OperatorNotAllowed,
                        $"Global search only allows the 'Contains' operator, but field '{leaf.Field}' used '{leaf.Op}'.",
                        leaf.Field,
                        leaf.Op);
                }

                break;

            case FilterOrigin.Scope:
                if (!field.IsScopable)
                {
                    throw new FilterValidationException(
                        FilterErrorCode.ScopeNotAllowed,
                        $"Field '{leaf.Field}' is not scopable.",
                        leaf.Field,
                        leaf.Op);
                }

                break;

            default:
                throw new FilterValidationException(
                    FilterErrorCode.InvalidValue,
                    $"Unknown filter origin '{origin}'.",
                    leaf.Field,
                    leaf.Op);
        }

        return field;
    }

    /// <summary>
    /// Builds a substring-match predicate. Overridable so the EF executor can emit a provider-correct
    /// translation (for example <c>EF.Functions.ILike</c>); the base implementation uses
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> with a null guard (§8.2, Decision Log D17).
    /// </summary>
    /// <param name="member">The string member expression being tested.</param>
    /// <param name="value">The string constant expression to search for.</param>
    /// <param name="leaf">The originating leaf, for diagnostics.</param>
    /// <returns>A boolean expression evaluating the substring match.</returns>
    protected virtual Expression BuildContains(Expression member, Expression value, FilterLeaf leaf) =>
        _dialect is not null
            ? _dialect.BuildStringMatch(member, RawString(leaf, value), StringMatchKind.Contains)
            : BuildStringCall(member, value, StringContainsMethod);

    /// <summary>
    /// Builds a prefix-match predicate. Overridable for provider-correct translation; the base uses
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> with a null guard (§8.2, Decision Log D17).
    /// </summary>
    /// <param name="member">The string member expression being tested.</param>
    /// <param name="value">The string constant expression to match as a prefix.</param>
    /// <param name="leaf">The originating leaf, for diagnostics.</param>
    /// <returns>A boolean expression evaluating the prefix match.</returns>
    protected virtual Expression BuildStartsWith(Expression member, Expression value, FilterLeaf leaf) =>
        _dialect is not null
            ? _dialect.BuildStringMatch(member, RawString(leaf, value), StringMatchKind.StartsWith)
            : BuildStringCall(member, value, StringStartsWithMethod);

    /// <summary>
    /// Builds a suffix-match predicate. Overridable for provider-correct translation; the base uses
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> with a null guard (§8.2, Decision Log D17).
    /// </summary>
    /// <param name="member">The string member expression being tested.</param>
    /// <param name="value">The string constant expression to match as a suffix.</param>
    /// <param name="leaf">The originating leaf, for diagnostics.</param>
    /// <returns>A boolean expression evaluating the suffix match.</returns>
    protected virtual Expression BuildEndsWith(Expression member, Expression value, FilterLeaf leaf) =>
        _dialect is not null
            ? _dialect.BuildStringMatch(member, RawString(leaf, value), StringMatchKind.EndsWith)
            : BuildStringCall(member, value, StringEndsWithMethod);

    /// <summary>
    /// Reads the raw search string for the dialect path, from the leaf value (validated as a string by
    /// the caller) or, failing that, the constant expression the base supplies.
    /// </summary>
    private static string RawString(FilterLeaf leaf, Expression value) => leaf.Value switch
    {
        string s => s,
        _ when value is ConstantExpression { Value: string c } => c,
        _ => throw new FilterValidationException(
            FilterErrorCode.InvalidValue,
            $"Operator '{leaf.Op}' on field '{leaf.Field}' requires a string value.",
            leaf.Field,
            leaf.Op),
    };

    private static Expression BuildStringCall(Expression member, Expression value, MethodInfo method)
    {
        // member != null && member.<Method>(value, OrdinalIgnoreCase) — guards LINQ-to-objects against null members.
        var comparison = Expression.Constant(StringComparison.OrdinalIgnoreCase);
        var call = Expression.Call(member, method, value, comparison);
        var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        return Expression.AndAlso(notNull, call);
    }

    private Expression BuildIn(FilterLeaf leaf, Expression member, Type underlying, Type memberType)
    {
        if (leaf.Value is not IEnumerable enumerable || leaf.Value is string)
        {
            throw new FilterValidationException(
                FilterErrorCode.InvalidValue,
                $"Operator 'In' on field '{leaf.Field}' requires a collection of values.",
                leaf.Field,
                leaf.Op);
        }

        var listType = typeof(List<>).MakeGenericType(memberType);
        var list = (IList)Activator.CreateInstance(listType)!;
        foreach (var item in enumerable)
        {
            list.Add(Coerce(item, underlying, memberType, leaf));
        }

        var enumerableType = typeof(IEnumerable<>).MakeGenericType(memberType);
        var setConstant = Expression.Constant(list, enumerableType);
        var contains = EnumerableContainsMethod.MakeGenericMethod(memberType);
        return Expression.Call(contains, setConstant, member);
    }

    private Expression BuildBetween(FilterLeaf leaf, Expression member, Type underlying, Type memberType)
    {
        if (leaf.Value is string || leaf.Value is not IEnumerable enumerable)
        {
            throw new FilterValidationException(
                FilterErrorCode.InvalidValue,
                $"Operator 'Between' on field '{leaf.Field}' requires a two-element [low, high] value.",
                leaf.Field,
                leaf.Op);
        }

        var bounds = new List<object?>();
        foreach (var item in enumerable)
        {
            bounds.Add(item);
        }

        if (bounds.Count != 2)
        {
            throw new FilterValidationException(
                FilterErrorCode.InvalidValue,
                $"Operator 'Between' on field '{leaf.Field}' requires exactly two values, but got {bounds.Count}.",
                leaf.Field,
                leaf.Op);
        }

        var low = MakeConstant(Coerce(bounds[0], underlying, memberType, leaf), underlying, memberType);
        var high = MakeConstant(Coerce(bounds[1], underlying, memberType, leaf), underlying, memberType);
        var lowerBound = Expression.GreaterThanOrEqual(member, low);
        var upperBound = Expression.LessThanOrEqual(member, high);
        return Expression.AndAlso(lowerBound, upperBound);
    }

    private static Expression BuildIsNull(Expression member, Type memberType)
    {
        var underlying = Nullable.GetUnderlyingType(memberType);
        if (underlying is null && memberType.IsValueType)
        {
            // A non-nullable value type can never be null.
            return Expression.Constant(false);
        }

        return Expression.Equal(member, Expression.Constant(null, memberType));
    }

    private Expression ConstantFor(FilterLeaf leaf, Type underlying, Type memberType) =>
        MakeConstant(Coerce(leaf.Value, underlying, memberType, leaf), underlying, memberType);

    private static Expression MakeConstant(object? value, Type underlying, Type memberType)
    {
        if (value is null)
        {
            return Expression.Constant(null, memberType);
        }

        Expression constant = Expression.Constant(value, underlying);
        return constant.Type == memberType ? constant : Expression.Convert(constant, memberType);
    }

    private static Expression StringValue(FilterLeaf leaf)
    {
        if (leaf.Value is not string s)
        {
            throw new FilterValidationException(
                FilterErrorCode.InvalidValue,
                $"Operator '{leaf.Op}' on field '{leaf.Field}' requires a string value.",
                leaf.Field,
                leaf.Op);
        }

        return Expression.Constant(s, typeof(string));
    }

    private static object? Coerce(object? value, Type underlying, Type memberType, FilterLeaf leaf)
    {
        if (value is null)
        {
            var isNullable = !memberType.IsValueType || Nullable.GetUnderlyingType(memberType) is not null;
            if (!isNullable)
            {
                throw new FilterValidationException(
                    FilterErrorCode.InvalidValue,
                    $"Field '{leaf.Field}' is not nullable, so a null value is invalid.",
                    leaf.Field,
                    leaf.Op);
            }

            return null;
        }

        if (underlying.IsInstanceOfType(value))
        {
            return value;
        }

        try
        {
            if (underlying.IsEnum)
            {
                return value is string enumText
                    ? Enum.Parse(underlying, enumText, ignoreCase: true)
                    : Enum.ToObject(underlying, value);
            }

            if (underlying == typeof(Guid))
            {
                // A non-string value must still be converted here: returning it unchanged would escape
                // this guarded block and surface later as an unmapped ArgumentException → HTTP 500
                // instead of the documented 400 (Expression.Constant type mismatch).
                return value is string guidText
                    ? Guid.Parse(guidText)
                    : Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            }

            if (underlying == typeof(DateTime))
            {
                return value is string dt
                    ? DateTime.Parse(dt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    : Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            }

            if (underlying == typeof(DateTimeOffset))
            {
                return value is string dto
                    ? DateTimeOffset.Parse(dto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    : Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            }

            if (underlying == typeof(DateOnly))
            {
                return value is string d
                    ? DateOnly.Parse(d, CultureInfo.InvariantCulture)
                    : Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            }

            if (underlying == typeof(TimeOnly))
            {
                return value is string t
                    ? TimeOnly.Parse(t, CultureInfo.InvariantCulture)
                    : Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
            }

            return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new FilterValidationException(
                FilterErrorCode.InvalidValue,
                $"Value '{value}' could not be converted to type '{underlying.Name}' for field '{leaf.Field}'.",
                leaf.Field,
                leaf.Op,
                ex);
        }
    }

    private static bool IsSingleOperator(FilterOperator op) =>
        op != FilterOperator.None && (op & (op - 1)) == 0;

    /// <summary>
    /// Coerces a raw filter/key value to the target field's CLR type, exposed to the EF layer so
    /// Detail-by-key segment coercion uses the same single code path (Decision Log D109). Mirrors the
    /// per-leaf coercion used by the filter operators.
    /// </summary>
    /// <param name="value">The raw value (string/number/etc.) to coerce.</param>
    /// <param name="targetType">The target CLR type (the field's <see cref="FieldMetadata.ClrType"/>).</param>
    /// <param name="fieldName">The field name, for diagnostics.</param>
    /// <returns>The coerced value.</returns>
    /// <exception cref="FilterValidationException">The value cannot be coerced to <paramref name="targetType"/>.</exception>
    internal static object? CoerceValue(object? value, Type targetType, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(targetType);
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var leaf = new FilterLeaf(fieldName, FilterOperator.Equals, value);
        return Coerce(value, underlying, targetType, leaf);
    }

    /// <summary>
    /// Enforces the per-view complexity hard limits (Decision Log D108, §8.2/§8.3) before any expression
    /// is built: filter nesting depth, total leaf count, single string-value length, and <c>In</c> value
    /// count. A breach is reported as <see cref="FilterErrorCode.RequestTooComplex"/> (HTTP 400).
    /// </summary>
    private static void EnforceLimits(FilterNode node, HardLimits limits)
    {
        var leafCount = 0;
        Walk(node, 1);

        void Walk(FilterNode current, int depth)
        {
            if (depth > limits.MaxFilterDepth)
            {
                throw new FilterValidationException(
                    FilterErrorCode.RequestTooComplex,
                    $"Filter nesting depth exceeds the maximum of {limits.MaxFilterDepth}.");
            }

            switch (current)
            {
                case FilterLeaf leaf:
                    if (++leafCount > limits.MaxFilterLeaves)
                    {
                        throw new FilterValidationException(
                            FilterErrorCode.RequestTooComplex,
                            $"Filter leaf count exceeds the maximum of {limits.MaxFilterLeaves}.");
                    }

                    EnforceValueLimits(leaf, limits);
                    break;

                case FilterAnd and:
                    foreach (var child in and.Children)
                    {
                        Walk(child, depth + 1);
                    }

                    break;

                case FilterOr or:
                    foreach (var child in or.Children)
                    {
                        Walk(child, depth + 1);
                    }

                    break;

                case FilterNot not:
                    Walk(not.Child, depth + 1);
                    break;
            }
        }
    }

    private static void EnforceValueLimits(FilterLeaf leaf, HardLimits limits)
    {
        if (leaf.Value is string s && s.Length > limits.MaxFilterStringLength)
        {
            throw new FilterValidationException(
                FilterErrorCode.RequestTooComplex,
                $"Filter string value on field '{leaf.Field}' exceeds the maximum length of {limits.MaxFilterStringLength}.",
                leaf.Field,
                leaf.Op);
        }

        if (leaf.Op == FilterOperator.In && leaf.Value is IEnumerable enumerable and not string)
        {
            var count = 0;
            foreach (var item in enumerable)
            {
                if (++count > limits.MaxInValues)
                {
                    throw new FilterValidationException(
                        FilterErrorCode.RequestTooComplex,
                        $"The 'In' operator on field '{leaf.Field}' exceeds the maximum of {limits.MaxInValues} values.",
                        leaf.Field,
                        leaf.Op);
                }

                if (item is string itemString && itemString.Length > limits.MaxFilterStringLength)
                {
                    throw new FilterValidationException(
                        FilterErrorCode.RequestTooComplex,
                        $"An 'In' string value on field '{leaf.Field}' exceeds the maximum length of {limits.MaxFilterStringLength}.",
                        leaf.Field,
                        leaf.Op);
                }
            }
        }
    }
}

/// <summary>
/// Rebinds every occurrence of one <see cref="ParameterExpression"/> to another inside an expression
/// tree. Used to splice a generated member-access lambda (whose body references its own parameter) onto
/// the shared predicate/key parameter without reflecting over the row type — the AOT-clean replacement
/// for <c>Expression.Property(string)</c> on the source-generated compiled read path (Decision Log
/// D118). Internal so the EF execution layer can reuse the same rebinding for Detail-by-key.
/// </summary>
internal sealed class ParameterReplaceVisitor : ExpressionVisitor
{
    private readonly ParameterExpression _from;
    private readonly ParameterExpression _to;

    private ParameterReplaceVisitor(ParameterExpression from, ParameterExpression to)
    {
        _from = from;
        _to = to;
    }

    /// <summary>
    /// Returns <paramref name="body"/> with every reference to <paramref name="from"/> replaced by
    /// <paramref name="to"/>.
    /// </summary>
    /// <param name="body">The expression to rewrite.</param>
    /// <param name="from">The parameter to replace (the generated lambda's parameter).</param>
    /// <param name="to">The parameter to substitute in (the shared parameter).</param>
    public static Expression Replace(Expression body, ParameterExpression from, ParameterExpression to) =>
        new ParameterReplaceVisitor(from, to).Visit(body);

    /// <inheritdoc />
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == _from ? _to : base.VisitParameter(node);
}
