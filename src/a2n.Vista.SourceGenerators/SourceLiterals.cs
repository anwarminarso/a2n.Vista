namespace a2n.Vista.SourceGenerators
{
    /// <summary>
    /// The single C# string-literal writer shared by every emitter in this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists (audit finding <c>DEAD-09</c>).</b> Three emitters wrote string literals into
    /// generated source three ways: <c>JsonContextEmitter</c> and <c>StyleAShapeGenerator</c> each had a
    /// private, identical <c>Literal</c>, while <c>ViewAccessorGenerator</c> concatenated
    /// <c>"[\""</c> + name + <c>"\"]"</c> with **no escaping at all**. Two emitters producing the accessor map
    /// had therefore drifted — exactly the failure mode duplicated helpers predict. One writer removes the
    /// drift by construction.
    /// </para>
    /// <para>
    /// <b>Output is unchanged for identifiers.</b> A CLR member name cannot contain a quote or a backslash, so
    /// escaping one produces the same bytes as concatenating it raw. That is what keeps this a pure
    /// deduplication: the generator goldens and the byte-for-byte parity guard against the reflection oracle
    /// are untouched. The escaping matters for the values that are <em>not</em> identifiers — an author-supplied
    /// view name, or a JSON property name — where a raw concatenation would emit source that does not compile.
    /// </para>
    /// </remarks>
    internal static class SourceLiterals
    {
        /// <summary>
        /// Renders <paramref name="value"/> as a quoted, escaped C# string literal. Backslashes are escaped
        /// before quotes so an embedded <c>\"</c> survives both passes.
        /// </summary>
        /// <param name="value">The raw string to embed in generated source.</param>
        /// <returns>The literal, including its surrounding quotes.</returns>
        internal static string Literal(string value)
            => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
