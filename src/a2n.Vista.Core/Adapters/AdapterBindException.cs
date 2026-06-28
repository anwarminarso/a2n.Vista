using System;

namespace a2n.Vista.Adapters;

/// <summary>
/// Thrown by an <see cref="IViewAdapter"/> when the incoming request cannot be parsed into its typed
/// request shape — a syntactic failure such as a malformed bracket key, a non-integer scalar, or broken
/// <c>jsonQB</c>/<c>externalFilter</c> JSON. The host maps it to HTTP 400 (<c>adapter-bind-failed</c>).
/// This is distinct from whitelist/operator/complexity violations, which the engine raises while
/// compiling the filter tree (Spec 04 §10).
/// </summary>
public sealed class AdapterBindException : Exception
{
    /// <summary>Initializes a new <see cref="AdapterBindException"/> with a message.</summary>
    /// <param name="message">A description of the binding failure.</param>
    public AdapterBindException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new <see cref="AdapterBindException"/> with a message and inner exception.</summary>
    /// <param name="message">A description of the binding failure.</param>
    /// <param name="innerException">The underlying parse exception.</param>
    public AdapterBindException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
