using a2n.Vista.Contracts;
using a2n.Vista.Metadata;

namespace a2n.Vista.Tests;

/// <summary>
/// A small projected row type used by the Core filter/enforcement tests. Each property maps to a
/// <see cref="FieldMetadata"/> in <see cref="TestViews.BuildEnforcementView"/>, so the whitelist
/// flags (filterable / searchable / scopable / allowed operators) can be controlled precisely.
/// </summary>
/// <param name="Id">Primary key. Filterable, equality/membership only.</param>
/// <param name="Name">Searchable text field, filterable, NOT scopable.</param>
/// <param name="Price">Numeric field, filterable with range/equality operators only (no Contains).</param>
/// <param name="Secret">Opt-out field: NOT filterable and NOT scopable.</param>
/// <param name="TenantId">Contextual scope key: scopable (and filterable).</param>
public sealed record TestRow(int Id, string Name, decimal Price, string Secret, int TenantId);

/// <summary>
/// Additive, self-contained factory of <see cref="ViewMetadata"/> instances used by the Core
/// enforcement tests (tasks 12.2+). Construction is explicit (no authoring builders) so each test
/// controls the tri-whitelist flags directly. Authoritative behavior: docs/spec/01-view.md §8.3.
/// </summary>
internal static class TestViews
{
    /// <summary>
    /// Builds a read-only <see cref="ViewMetadata"/> over <see cref="TestRow"/> whose fields exercise
    /// every tri-whitelist rule (Requirements R5.5, R5.6, R6.2):
    /// <list type="bullet">
    /// <item><c>Id</c>: filterable, <c>Equals | In</c>.</item>
    /// <item><c>Name</c>: filterable + searchable string, <c>Text | In</c>, NOT scopable.</item>
    /// <item><c>Price</c>: filterable, range/equality operators only (no <c>Contains</c>).</item>
    /// <item><c>Secret</c>: NOT filterable (opt-out), NOT scopable.</item>
    /// <item><c>TenantId</c>: scopable (and filterable), <c>Equals | In</c>.</item>
    /// </list>
    /// </summary>
    /// <returns>A read-only view metadata snapshot over <see cref="TestRow"/>.</returns>
    public static ViewMetadata BuildEnforcementView()
    {
        var fields = new[]
        {
            FieldMetadata.Create(
                name: nameof(TestRow.Id),
                clrType: typeof(int),
                isFilterable: true,
                isSearchable: false,
                isScopable: false,
                allowedOperators: FilterOperator.Equals | FilterOperator.In),

            FieldMetadata.Create(
                name: nameof(TestRow.Name),
                clrType: typeof(string),
                isFilterable: true,
                isSearchable: true,
                isScopable: false,
                allowedOperators: FilterOperator.Text | FilterOperator.In),

            FieldMetadata.Create(
                name: nameof(TestRow.Price),
                clrType: typeof(decimal),
                isFilterable: true,
                isSearchable: false,
                isScopable: false,
                allowedOperators: FilterOperator.GreaterThanOrEqual
                    | FilterOperator.LessThanOrEqual
                    | FilterOperator.Between
                    | FilterOperator.Equals),

            FieldMetadata.Create(
                name: nameof(TestRow.Secret),
                clrType: typeof(string),
                isFilterable: false,
                isSearchable: false,
                isScopable: false,
                allowedOperators: FilterOperator.None),

            FieldMetadata.Create(
                name: nameof(TestRow.TenantId),
                clrType: typeof(int),
                isFilterable: true,
                isSearchable: false,
                isScopable: true,
                allowedOperators: FilterOperator.Equals | FilterOperator.In),
        };

        return new ViewMetadata(
            Name: "TestRows",
            Route: "/test/TestRows",
            QueryType: typeof(TestRow),
            CrudType: null,
            CrudEntityType: null,
            Fields: fields,
            Authorization: null,
            Limits: HardLimits.Default,
            IsReadOnly: true);
    }
}
