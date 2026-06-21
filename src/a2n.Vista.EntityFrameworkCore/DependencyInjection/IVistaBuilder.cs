using System.Diagnostics.CodeAnalysis;
using a2n.Vista.Authoring;
using a2n.Vista.EntityFrameworkCore.Execution;
using a2n.Vista.Ports;

namespace a2n.Vista.EntityFrameworkCore;

/// <summary>
/// The configuration surface returned by <c>AddVista</c> for registering views at the composition root
/// (Requirement R11.2). It writes into the same singleton <see cref="IViewRegistry"/> (metadata, the
/// EF-free transport surface) and <see cref="IViewExecutionPlanRegistry"/> (the EF-side execution
/// plans) that the request-scoped executor reads back, so registration is a startup-only activity
/// (§5.3, §5.5).
/// </summary>
/// <remarks>
/// <para>
/// Both registration paths fail fast on a duplicate view name: the underlying registries reject a
/// second view with the same <see cref="a2n.Vista.Metadata.ViewMetadata.Name"/> (Requirement R1.3), and
/// only explicitly registered views are ever resolved (Requirement R1.2, no auto-expose).
/// </para>
/// <para>
/// The reflection-driven members are marked <see cref="RequiresUnreferencedCodeAttribute"/>: Gaya A
/// (<see cref="RegisterTemplate{TTemplate, TDbContext}"/>) enumerates the projection row type to derive
/// field metadata, and Gaya B (<see cref="Register{TView}()"/>) introspects the view type. The
/// AOT-clean route is the source generator (Pilar 3); a generated plan can be paired through
/// <see cref="Register{TView}(IViewExecutionPlan)"/>.
/// </para>
/// </remarks>
public interface IVistaBuilder
{
    /// <summary>
    /// Registers all views authored by a Gaya A (central-template) <see cref="ViewTemplate{TDbContext}"/>:
    /// instantiates <typeparamref name="TTemplate"/>, runs its <c>Configure</c>, and for each produced
    /// view adds the metadata to the <see cref="IViewRegistry"/> and a matching execution plan to the
    /// <see cref="IViewExecutionPlanRegistry"/>. Also records <typeparamref name="TDbContext"/> so the
    /// scoped executor can resolve the right context (see <see cref="VistaDbContextAccessor"/>).
    /// </summary>
    /// <typeparam name="TTemplate">The concrete template type (parameterless).</typeparam>
    /// <typeparam name="TDbContext">The data-source type the template's projections are authored against.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Two views resolve to the same name (R1.3), or a view declares a CRUD facet without any
    /// <c>MapWritable</c> mapping.
    /// </exception>
    /// <remarks>
    /// <typeparamref name="TDbContext"/> is an explicit type parameter (rather than reflected from the
    /// template's base type) so the captured context type is statically known — clearer and friendlier
    /// to trimming/AOT analysis.
    /// </remarks>
    [RequiresUnreferencedCode("Gaya A authoring enumerates the (possibly anonymous) projection row type via reflection to derive field metadata; use the source generator path for AOT.")]
    IVistaBuilder RegisterTemplate<TTemplate, TDbContext>()
        where TTemplate : ViewTemplate<TDbContext>, new()
        where TDbContext : class;

    /// <summary>
    /// Registers a Gaya B (class-per-view) <see cref="View{TQuery}"/> / <see cref="View{TQuery, TCrud}"/>
    /// by building its <see cref="a2n.Vista.Metadata.ViewMetadata"/> and adding it to the
    /// <see cref="IViewRegistry"/>.
    /// </summary>
    /// <typeparam name="TView">The view type (parameterless), deriving from a Gaya B base class.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TView"/> does not derive from <see cref="View{TQuery}"/> or
    /// <see cref="View{TQuery, TCrud}"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">A view with the same name is already registered (R1.3).</exception>
    /// <remarks>
    /// <b>Documented limitation (flagged, consistent with Task 9.2).</b> This overload registers
    /// <em>metadata only</em>. The Gaya B authoring builder does not yet surface the captured source
    /// query and projection to the EF layer, so no <see cref="IViewExecutionPlan"/> can be built here.
    /// The view is discoverable and returns 404-free metadata, but <em>executing</em> it (List/Detail)
    /// throws because no plan is registered. To make a Gaya B view executable today, supply a plan via
    /// <see cref="Register{TView}(IViewExecutionPlan)"/> (hand-built or, later, source-generated).
    /// </remarks>
    [RequiresUnreferencedCode("Gaya B registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    IVistaBuilder Register<TView>()
        where TView : class, new();

    /// <summary>
    /// Registers a Gaya B (class-per-view) view together with an explicitly supplied
    /// <see cref="IViewExecutionPlan"/>, making the view both discoverable and executable. This is the
    /// escape hatch for Gaya B until its builder surfaces the source/projection to the EF layer, and the
    /// interop point for source-generated plans (Pilar 3).
    /// </summary>
    /// <typeparam name="TView">The view type (parameterless), deriving from a Gaya B base class.</typeparam>
    /// <param name="plan">The execution plan; its <see cref="IViewExecutionPlan.ViewName"/> must match the view's name.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="plan"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <typeparamref name="TView"/> is not a Gaya B view, or <paramref name="plan"/>'s view name does
    /// not match the view's built name.
    /// </exception>
    /// <exception cref="InvalidOperationException">A view (or plan) with the same name is already registered (R1.3).</exception>
    [RequiresUnreferencedCode("Gaya B registration introspects the view type at runtime to build its metadata; use the source generator path for AOT.")]
    IVistaBuilder Register<TView>(IViewExecutionPlan plan)
        where TView : class, new();

    /// <summary>
    /// Opens a route group: every view registered inside <paramref name="configure"/> is mounted under
    /// <paramref name="prefix"/> (its full route becomes <c>{prefix}/{viewName}</c>), instead of the
    /// default root (<c>/api/views</c>). Groups let an application separate endpoints — e.g. internal vs
    /// external — and let a module own its own prefix (Decision Log D103). Groups may nest; an inner
    /// prefix is appended to the outer one. Registration is the single source of a view's route
    /// (Decision Log D101): the resolved full route is recorded in
    /// <see cref="a2n.Vista.Metadata.ViewMetadata.Route"/> and the AspNetCore layer maps it verbatim.
    /// </summary>
    /// <param name="prefix">The route prefix for the group, for example <c>/internal</c> or <c>/api/external</c>.</param>
    /// <param name="configure">A callback that registers the group's views on this same builder.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is <see langword="null"/> or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A view name is globally unique (Requirement R1.3) and a view is consumed by exactly one endpoint
    /// (R3.5): registering the same view in two groups fails fast with a duplicate-name error.
    /// </remarks>
    IVistaBuilder RouteGroup(string prefix, Action<IVistaBuilder> configure);

    /// <summary>
    /// Discovers and registers every Gaya B view type (deriving from <see cref="View{TQuery}"/> /
    /// <see cref="View{TQuery, TCrud}"/>, non-abstract, with a public parameterless constructor) in
    /// <paramref name="assembly"/>, honoring the current route group. Each view is registered
    /// metadata-only (like <see cref="Register{TView}()"/>); pair with a source-generated plan to make
    /// them executable. Intended for the modular-monolith case where a sub-project's assembly is
    /// attached under a group prefix.
    /// </summary>
    /// <param name="assembly">The assembly to scan for view types.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Two discovered views resolve to the same name (R1.3).</exception>
    [RequiresUnreferencedCode("Assembly scanning enumerates all types via reflection and introspects each view type's metadata; use explicit Register<TView> or the source generator for AOT.")]
    IVistaBuilder RegisterAssembly(System.Reflection.Assembly assembly);
}
