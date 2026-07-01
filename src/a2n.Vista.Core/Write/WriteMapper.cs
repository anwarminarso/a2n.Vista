namespace a2n.Vista.Write;

/// <summary>
/// The single fixed-signature seam through which client-supplied values reach a target entity on a
/// write (Create/Update). The executor resolves exactly one <see cref="WriteMapper"/> per write and
/// applies it once, so a reflection-based mapper (now) and a source-generated mapper (later, Pilar 3)
/// are interchangeable with zero source changes to the executor (Decision Log D119, Requirement
/// R13.1/R13.2). Authoritative behavior: docs/spec write-path §"Write mapper seam".
/// </summary>
/// <param name="model">
/// The typed write model (<c>TCrud</c>) the client posted, boxed as <see cref="object"/> so the seam
/// stays free of any generic type parameter. Only the members whitelisted via <c>MapWritable</c> are
/// read.
/// </param>
/// <param name="entity">
/// The target entity (<c>TEntity</c>) the whitelisted values are assigned to, boxed as
/// <see cref="object"/>. Key fields and the concurrency token are never assigned by a conforming
/// mapper (Requirements R4, R5).
/// </param>
/// <remarks>
/// This delegate is intentionally EF-free and HTTP-free: it lives in Core so both the EF execution
/// layer and the future generated mapper can share one contract without Core taking any adapter
/// dependency (Requirement R14.1). Implementations must assign only scalar members named by
/// <c>MapWritable</c> targets and must leave every other member — including keys and the concurrency
/// token — byte-identical to its pre-write value (Property 1).
/// </remarks>
public delegate void WriteMapper(object model, object entity);
