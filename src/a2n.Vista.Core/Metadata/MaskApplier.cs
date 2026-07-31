// Licensed to the a2n.Vista project. Published artifact — English only.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace a2n.Vista.Metadata;

/// <summary>
/// Raised when a field mask cannot be applied — the <see cref="MaskSpec.ShouldMask"/> predicate or the
/// <see cref="MaskSpec.Masker"/> transform threw, the original value could not be read, or the masked
/// value could not be written (source-generator Phase 2 / Decision Log D118, Requirement R7.6). Masking
/// fails <b>closed</b>: the executor surfaces this error and never serializes the field's original value.
/// </summary>
public sealed class MaskingException : Exception
{
    /// <summary>Initializes a new <see cref="MaskingException"/>.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying error, when masking failed because a delegate threw.</param>
    public MaskingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Applies the captured field masks (Decision Log D29/D95, source-generator Phase 2 / D118) to
/// materialized rows, post-projection and in memory, on the List, Detail, and export read paths
/// (Requirement R7.5 / R8.3). Build one per request via <see cref="Create"/> (AOT-clean, generated
/// <see cref="MaskAccessor"/>s) or <see cref="CreateWithReflectionFallback"/> (reflection fallback for
/// Style A / non-generated views), then call <see cref="Apply"/> on each row.
/// </summary>
/// <remarks>
/// <para>
/// Each masked field's <see cref="MaskSpec.ShouldMask"/> predicate is evaluated <b>once per request</b>
/// when the applier is created (R7.2): the predicate is request-scoped, while the per-row value
/// substitution happens for every row. A field whose predicate returns <see langword="false"/> is left
/// untouched (R7.4). When the predicate or any read/transform/write step throws, the applier fails
/// closed with a <see cref="MaskingException"/> — the original value never leaks (R7.6).
/// </para>
/// <para>
/// Masking runs entirely after the EF query executes, so it never alters the SQL (R7.5).
/// </para>
/// </remarks>
public sealed class MaskApplier
{
    /// <summary>A no-op applier: no masked field is active for the request.</summary>
    public static readonly MaskApplier None = new(string.Empty, Array.Empty<ActiveMask>());

    private readonly string _viewName;
    private readonly IReadOnlyList<ActiveMask> _active;

    private MaskApplier(string viewName, IReadOnlyList<ActiveMask> active)
    {
        _viewName = viewName;
        _active = active;
    }

    /// <summary>
    /// Whether any masked field is active for the current request. When <see langword="false"/>,
    /// <see cref="Apply"/> returns its argument unchanged and callers may skip the row walk entirely.
    /// </summary>
    public bool HasWork => _active.Count > 0;

    /// <summary>
    /// Builds an applier for a request using <b>generated</b> mask accessors (the AOT-clean path used by
    /// the source-generated compiled execution plan). The mask specs are resolved from
    /// <see cref="MaskSpecRegistry"/> by <paramref name="viewName"/>; each active mask is matched to a
    /// generated <see cref="MaskAccessor"/> by field name. No reflection is used.
    /// </summary>
    /// <param name="viewName">The view whose masks to apply.</param>
    /// <param name="accessors">The generated read/write accessors for the view's masked fields.</param>
    /// <param name="services">The request services, used to evaluate each predicate once.</param>
    /// <returns>An applier, or <see cref="None"/> when the view has no active masked fields.</returns>
    /// <exception cref="MaskingException">A predicate threw, or a masked field has no matching accessor.</exception>
    public static MaskApplier Create(
        string viewName,
        IReadOnlyList<MaskAccessor> accessors,
        IServiceProvider? services)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(accessors);

        // AOT-clean accessor resolver: generated accessors only, no reflection reachable from here.
        return Build(
            viewName,
            services,
            (vn, spec) => FindAccessor(accessors, spec.FieldName)
                ?? throw new MaskingException(
                    $"View '{vn}' masked field '{spec.FieldName}' has no generated mask accessor, so it " +
                    "cannot be masked on the compiled path."));
    }

    /// <summary>
    /// Builds an applier for a request, falling back to <b>reflection</b> for any masked field that has
    /// no generated <see cref="MaskAccessor"/> (the RUC path for Style A / non-generated views). Used by
    /// the executor's reflection (RUC) read path.
    /// </summary>
    /// <param name="viewName">The view whose masks to apply.</param>
    /// <param name="rowType">The projected row type, used to build reflection accessors.</param>
    /// <param name="generatedAccessors">Any generated accessors to prefer over reflection (may be empty).</param>
    /// <param name="services">The request services, used to evaluate each predicate once.</param>
    /// <returns>An applier, or <see cref="None"/> when the view has no active masked fields.</returns>
    /// <exception cref="MaskingException">A predicate threw, or a masked field cannot be accessed.</exception>
    [RequiresUnreferencedCode("Reflection mask accessors read/write the masked property by name; use the source generator path for AOT.")]
    public static MaskApplier CreateWithReflectionFallback(
        string viewName,
        Type rowType,
        IReadOnlyList<MaskAccessor> generatedAccessors,
        IServiceProvider? services)
    {
        ArgumentNullException.ThrowIfNull(viewName);
        ArgumentNullException.ThrowIfNull(rowType);
        ArgumentNullException.ThrowIfNull(generatedAccessors);

        // The reflection reference lives only inside this RUC method's resolver, so the AOT-clean Create
        // path above never reaches BuildReflectionAccessor.
        return Build(
            viewName,
            services,
            (vn, spec) => FindAccessor(generatedAccessors, spec.FieldName)
                ?? BuildReflectionAccessor(vn, rowType, spec.FieldName));
    }

    /// <summary>
    /// Applies every active mask to <paramref name="row"/>: for each, it reads the original value, passes
    /// it (the pre-mask value, R7.3) to the masker, and writes the masked result back, threading the row
    /// through each <see cref="MaskAccessor.Set"/> (so immutable record rows are rebuilt). Returns the
    /// resulting (possibly new) row. A failure at any step throws <see cref="MaskingException"/> and the
    /// original value is never returned (R7.6).
    /// </summary>
    /// <param name="row">The materialized row to mask. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// The masked row: the same instance when every masked member is writable — which on the reflection path
    /// includes init-only and record members, since reflection can set those — and a rebuilt instance when a
    /// masked member is get-only, as on an anonymous Style A projection. Callers must use the return value
    /// rather than assume the argument was mutated.
    /// </returns>
    public object Apply(object row)
    {
        ArgumentNullException.ThrowIfNull(row);

        foreach (var mask in _active)
        {
            object? original;
            try
            {
                original = mask.Get(row);
            }
            catch (Exception ex)
            {
                throw Fail(mask.FieldName, "reading the original value", ex);
            }

            object? masked;
            try
            {
                masked = mask.Masker(original);
            }
            catch (Exception ex)
            {
                throw Fail(mask.FieldName, "applying the masker transform", ex);
            }

            try
            {
                row = mask.Set(row, masked);
            }
            catch (Exception ex)
            {
                throw Fail(mask.FieldName, "writing the masked value", ex);
            }
        }

        return row;
    }

    private static MaskApplier Build(
        string viewName,
        IServiceProvider? services,
        Func<string, MaskSpec, MaskAccessor> resolveAccessor)
    {
        if (!MaskSpecRegistry.TryGet(viewName, out var specs) || specs.Count == 0)
        {
            return None;
        }

        if (services is null)
        {
            throw new MaskingException(
                $"View '{viewName}' declares masked fields, but masking cannot be applied without a request " +
                "service provider to evaluate the mask predicates. Use the DI-wired executor.");
        }

        var active = new List<ActiveMask>(specs.Count);
        foreach (var spec in specs)
        {
            bool shouldMask;
            try
            {
                shouldMask = spec.ShouldMask(services);
            }
            catch (Exception ex)
            {
                // Fail closed (R7.6): a throwing predicate must not silently fall back to emitting the
                // original value.
                throw FailFor(viewName, spec.FieldName, "evaluating the masking predicate", ex);
            }

            if (!shouldMask)
            {
                continue;
            }

            var accessor = resolveAccessor(viewName, spec);
            active.Add(new ActiveMask(spec.FieldName, accessor.Get, spec.Masker, accessor.Set));
        }

        return active.Count == 0 ? None : new MaskApplier(viewName, active);
    }

    private static MaskAccessor? FindAccessor(IReadOnlyList<MaskAccessor> accessors, string fieldName)
    {
        foreach (var accessor in accessors)
        {
            if (string.Equals(accessor.FieldName, fieldName, StringComparison.Ordinal))
            {
                return accessor;
            }
        }

        return null;
    }

    [RequiresUnreferencedCode("Reflection mask accessors read/write the masked property by name; use the source generator path for AOT.")]
    private static MaskAccessor BuildReflectionAccessor(string viewName, Type rowType, string fieldName)
    {
        var property = rowType.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MaskingException(
                $"View '{viewName}' masked field '{fieldName}' was not found on row type '{rowType}', so it " +
                "cannot be masked via reflection.");

        if (property.GetMethod is null)
        {
            throw new MaskingException(
                $"View '{viewName}' masked field '{fieldName}' on row type '{rowType}' is not readable, so it " +
                "cannot be masked.");
        }

        if (property.SetMethod is null)
        {
            // A get-only member — in practice an anonymous Style A projection, since an init-only or record
            // member still exposes a setter to reflection. Rebuild the row through its constructor instead
            // of failing: refusing here meant the documented Style A fallback could not mask at all.
            return BuildRebuildAccessor(viewName, rowType, fieldName, property);
        }

        return new MaskAccessor(
            fieldName,
            property.GetValue,
            (row, value) =>
            {
                // init-only setters are still writable via reflection, so this covers init/record rows too.
                property.SetValue(row, value);
                return row;
            });
    }

    /// <summary>
    /// Builds a mask accessor for a row whose masked member is get-only, by rebuilding the row through a
    /// constructor that takes every readable property by name — the shape an anonymous type (and a
    /// positional record) exposes.
    /// </summary>
    /// <remarks>
    /// Requiring the constructor to cover <em>every</em> readable property is what makes the rebuild lossless:
    /// a partial constructor would silently drop the members it does not take. Resolution happens once when
    /// the applier is built (per request), so only the property reads and the constructor call are per-row.
    /// </remarks>
    [RequiresUnreferencedCode("Reflection mask accessors read/write the masked property by name; use the source generator path for AOT.")]
    private static MaskAccessor BuildRebuildAccessor(
        string viewName,
        Type rowType,
        string fieldName,
        PropertyInfo masked)
    {
        var properties = rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        ConstructorInfo? constructor = null;
        PropertyInfo[]? arguments = null;

        foreach (var candidate in rowType.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
        {
            var parameters = candidate.GetParameters();
            if (parameters.Length != properties.Length)
            {
                continue;
            }

            var mapped = new PropertyInfo[parameters.Length];
            var matched = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var property = FindProperty(properties, parameters[i].Name);
                if (property?.GetMethod is null
                    || !parameters[i].ParameterType.IsAssignableFrom(property.PropertyType))
                {
                    matched = false;
                    break;
                }

                mapped[i] = property;
            }

            if (matched)
            {
                constructor = candidate;
                arguments = mapped;
                break;
            }
        }

        var maskedIndex = arguments is null ? -1 : IndexOfProperty(arguments, fieldName);
        if (constructor is null || arguments is null || maskedIndex < 0)
        {
            throw new MaskingException(
                $"View '{viewName}' masked field '{fieldName}' on row type '{rowType}' has no setter, and the " +
                "row type has no constructor taking every readable property by name, so the mask cannot be " +
                "written without losing data. Use a settable or init-only property, a record, or an anonymous " +
                "projection.");
        }

        var index = maskedIndex;
        var argumentProperties = arguments;
        var rebuild = constructor;

        return new MaskAccessor(
            fieldName,
            masked.GetValue,
            (row, value) =>
            {
                var args = new object?[argumentProperties.Length];
                for (var i = 0; i < args.Length; i++)
                {
                    args[i] = i == index ? value : argumentProperties[i].GetValue(row);
                }

                return rebuild.Invoke(args);
            });
    }

    /// <summary>
    /// Maps a constructor parameter name to the property it initializes: an exact (ordinal) match first,
    /// then a single case-insensitive match. The fallback is what lets a hand-written immutable DTO
    /// (<c>Row(int id)</c> initializing <c>Id</c>) rebuild as well as an anonymous type or a positional
    /// record, whose parameter names already match exactly. An ambiguous case-insensitive match is treated
    /// as no match, so a wrong member can never be written.
    /// </summary>
    private static PropertyInfo? FindProperty(PropertyInfo[] properties, string? name)
    {
        if (name is null)
        {
            return null;
        }

        foreach (var property in properties)
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                return property;
            }
        }

        PropertyInfo? insensitive = null;
        foreach (var property in properties)
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (insensitive is not null)
            {
                return null;
            }

            insensitive = property;
        }

        return insensitive;
    }

    private static int IndexOfProperty(PropertyInfo[] properties, string name)
    {
        for (var i = 0; i < properties.Length; i++)
        {
            if (string.Equals(properties[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private MaskingException Fail(string fieldName, string action, Exception inner) =>
        FailFor(_viewName, fieldName, action, inner);

    private static MaskingException FailFor(string viewName, string fieldName, string action, Exception inner) =>
        new($"Masking failed for view '{viewName}' field '{fieldName}' while {action}; the request fails " +
            "closed and the original value is not emitted (R7.6).", inner);

    /// <summary>One masked field that is active for the current request (predicate returned true).</summary>
    /// <param name="FieldName">The masked field name, for diagnostics.</param>
    /// <param name="Get">Reads the original value from a row.</param>
    /// <param name="Masker">Transforms the pre-mask value.</param>
    /// <param name="Set">Writes the masked value into a row and returns the resulting row.</param>
    private readonly record struct ActiveMask(
        string FieldName,
        Func<object, object?> Get,
        Func<object?, object?> Masker,
        Func<object, object?, object> Set);
}
