using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using a2n.Vista.Contracts;
using a2n.Vista.Filter;
using a2n.Vista.Metadata;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace a2n.Vista.Tests;

/// <summary>
/// Correctness Property 3 — tri-whitelist enforcement (design.md §"Property 3"; spec §8.3).
/// Each leaf is validated against the whitelist for its <see cref="FilterOrigin"/> BEFORE any
/// expression is built; a violation surfaces from Core as a <see cref="FilterValidationException"/>
/// (which the AspNetCore layer maps to HTTP 400):
/// <list type="bullet">
/// <item>R5.5 — filtering a non-filterable (opt-out) field → <see cref="FilterErrorCode.FieldNotAllowed"/>.</item>
/// <item>R5.6 — using an operator outside the field's allowed set → <see cref="FilterErrorCode.OperatorNotAllowed"/>.</item>
/// <item>R6.2 — scoping on a non-scopable field → <see cref="FilterErrorCode.ScopeNotAllowed"/>.</item>
/// </list>
/// Exception assertions use a try/catch capture helper (<see cref="Capture{T}"/>) so the precise
/// <see cref="FilterValidationException.Code"/> / <see cref="FilterValidationException.ErrorCode"/>
/// can be asserted with TUnit's <c>Assert.That</c> across every targeted TFM.
/// </summary>
public sealed class EnforcementTests
{
    // FilterCompiler.Compile is [RequiresUnreferencedCode] (reflects over T at runtime; the AOT-clean
    // path is the Pilar 3 source generator). The test harness deliberately exercises the reflection
    // path, so the trim/AOT diagnostic is suppressed locally for these test-only call sites.
    private static readonly FilterCompiler Compiler = new();

    /// <summary>
    /// R5.5: a structured <see cref="FilterOrigin.Filter"/> leaf targeting a field whose
    /// <see cref="FieldMetadata.IsFilterable"/> is <see langword="false"/> is rejected with
    /// <see cref="FilterErrorCode.FieldNotAllowed"/> (wire <c>filter-field-not-allowed</c>).
    /// </summary>
    [Test]
    public async Task Filter_On_OptOut_Field_Throws_FieldNotAllowed()
    {
        var view = TestViews.BuildEnforcementView();
        var leaf = new FilterLeaf(nameof(TestRow.Secret), FilterOperator.Equals, "x");

        var ex = Capture<TestRow>(leaf, FilterOrigin.Filter, view);

        await Assert.That(ex.Code).IsEqualTo(FilterErrorCode.FieldNotAllowed);
        await Assert.That(ex.ErrorCode).IsEqualTo(FilterErrorCodes.FieldNotAllowed);
        await Assert.That(ex.Field).IsEqualTo(nameof(TestRow.Secret));
        await Assert.That(ex.Operator).IsEqualTo((FilterOperator?)FilterOperator.Equals);
    }

    /// <summary>
    /// R5.6: a <see cref="FilterOrigin.Filter"/> leaf using an operator that is not within the field's
    /// <see cref="FieldMetadata.AllowedOperators"/> is rejected with
    /// <see cref="FilterErrorCode.OperatorNotAllowed"/> (wire <c>filter-operator-not-allowed</c>).
    /// <c>Price</c> allows range/equality operators but not <see cref="FilterOperator.Contains"/>.
    /// </summary>
    [Test]
    public async Task Filter_With_Disallowed_Operator_Throws_OperatorNotAllowed()
    {
        var view = TestViews.BuildEnforcementView();
        var leaf = new FilterLeaf(nameof(TestRow.Price), FilterOperator.Contains, "5");

        var ex = Capture<TestRow>(leaf, FilterOrigin.Filter, view);

        await Assert.That(ex.Code).IsEqualTo(FilterErrorCode.OperatorNotAllowed);
        await Assert.That(ex.ErrorCode).IsEqualTo(FilterErrorCodes.OperatorNotAllowed);
        await Assert.That(ex.Field).IsEqualTo(nameof(TestRow.Price));
        await Assert.That(ex.Operator).IsEqualTo((FilterOperator?)FilterOperator.Contains);
    }

    /// <summary>
    /// R6.2: a <see cref="FilterOrigin.Scope"/> leaf targeting a field whose
    /// <see cref="FieldMetadata.IsScopable"/> is <see langword="false"/> is rejected with
    /// <see cref="FilterErrorCode.ScopeNotAllowed"/> (wire <c>filter-scope-not-allowed</c>).
    /// <c>Name</c> is filterable but NOT scopable, so it may not be used as a client scope key.
    /// </summary>
    [Test]
    public async Task Scope_On_NonScopable_Field_Throws_ScopeNotAllowed()
    {
        var view = TestViews.BuildEnforcementView();
        var leaf = new FilterLeaf(nameof(TestRow.Name), FilterOperator.Equals, "x");

        var ex = Capture<TestRow>(leaf, FilterOrigin.Scope, view);

        await Assert.That(ex.Code).IsEqualTo(FilterErrorCode.ScopeNotAllowed);
        await Assert.That(ex.ErrorCode).IsEqualTo(FilterErrorCodes.ScopeNotAllowed);
        await Assert.That(ex.Field).IsEqualTo(nameof(TestRow.Name));
    }

    /// <summary>
    /// Positive control: a valid filterable leaf with an allowed operator compiles without throwing
    /// and yields an <see cref="Expression{TDelegate}"/> of <see cref="Func{T, TResult}"/>
    /// (<c>TestRow</c> → <see cref="bool"/>) that evaluates correctly.
    /// </summary>
    [Test]
    public async Task Valid_Filterable_Leaf_Compiles_Without_Throwing()
    {
        var view = TestViews.BuildEnforcementView();
        var leaf = new FilterLeaf(nameof(TestRow.Name), FilterOperator.Equals, "x");

        var predicate = Compile<TestRow>(leaf, FilterOrigin.Filter, view);

        await Assert.That(predicate).IsNotNull();

        var compiled = predicate.Compile();
        var match = new TestRow(1, "x", 0m, "s", 7);
        var noMatch = new TestRow(2, "y", 0m, "s", 7);
        await Assert.That(compiled(match)).IsTrue();
        await Assert.That(compiled(noMatch)).IsFalse();
    }

    /// <summary>
    /// R5.6 (parameterized): every operator outside <c>Price</c>'s allowed set is rejected with
    /// <see cref="FilterErrorCode.OperatorNotAllowed"/>. Keeps the focus on the operator-whitelist
    /// rule while covering several representative disallowed operators.
    /// </summary>
    [Test]
    [Arguments(FilterOperator.Contains)]
    [Arguments(FilterOperator.StartsWith)]
    [Arguments(FilterOperator.EndsWith)]
    [Arguments(FilterOperator.NotEquals)]
    public async Task Filter_With_Various_Disallowed_Operators_On_Price_Throws(FilterOperator op)
    {
        var view = TestViews.BuildEnforcementView();
        var leaf = new FilterLeaf(nameof(TestRow.Price), op, "5");

        var ex = Capture<TestRow>(leaf, FilterOrigin.Filter, view);

        await Assert.That(ex.Code).IsEqualTo(FilterErrorCode.OperatorNotAllowed);
        await Assert.That(ex.Operator).IsEqualTo((FilterOperator?)op);
    }

    /// <summary>
    /// Compiles a leaf, asserting that a <see cref="FilterValidationException"/> IS thrown and
    /// returning it for inspection. Fails the test (returns via thrown assertion) when no exception
    /// or a different exception type occurs.
    /// </summary>
    [SuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "Test exercises the runtime reflection path of FilterCompiler by design; trimming is not used for tests.")]
    private static FilterValidationException Capture<T>(FilterNode node, FilterOrigin origin, ViewMetadata view)
    {
        try
        {
            _ = Compiler.Compile<T>(node, origin, view);
        }
        catch (FilterValidationException ex)
        {
            return ex;
        }

        throw new InvalidOperationException(
            "Expected a FilterValidationException, but Compile completed without throwing.");
    }

    /// <summary>Compiles a leaf and returns the predicate (positive-path helper).</summary>
    [SuppressMessage(
        "Trimming",
        "IL2026:Members annotated with RequiresUnreferencedCodeAttribute may break when trimming",
        Justification = "Test exercises the runtime reflection path of FilterCompiler by design; trimming is not used for tests.")]
    private static Expression<Func<T, bool>> Compile<T>(FilterNode node, FilterOrigin origin, ViewMetadata view) =>
        Compiler.Compile<T>(node, origin, view);
}
