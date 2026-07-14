namespace a2n.Vista.Client.TypeScript.Pipeline;

/// <summary>
/// A minimal, allocation-light success-or-error outcome used by the generator's pipeline stages.
/// Each stage returns a <see cref="Result{T, E}"/> instead of throwing for expected failures, so
/// the buffered pipeline can route every fatal cause through a single abort path (design §A.2).
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
/// <typeparam name="E">The typed error type (for the pipeline this is a <see cref="GenerationError"/> family).</typeparam>
public readonly struct Result<T, E>
{
    private readonly T? _value;
    private readonly E? _error;

    private Result(bool isOk, T? value, E? error)
    {
        IsOk = isOk;
        _value = value;
        _error = error;
    }

    /// <summary>Gets a value indicating whether this result carries a success value.</summary>
    public bool IsOk { get; }

    /// <summary>Gets a value indicating whether this result carries an error.</summary>
    public bool IsError => !IsOk;

    /// <summary>Creates a successful result carrying <paramref name="value"/>.</summary>
    public static Result<T, E> Ok(T value) => new(true, value, default);

    /// <summary>Creates a failed result carrying <paramref name="error"/>.</summary>
    public static Result<T, E> Err(E error) => new(false, default, error);

    /// <summary>Gets the success value. Throws if this result is an error.</summary>
    public T Value => IsOk
        ? _value!
        : throw new InvalidOperationException("Result is an error; no success value is available.");

    /// <summary>Gets the error. Throws if this result is a success.</summary>
    public E Error => !IsOk
        ? _error!
        : throw new InvalidOperationException("Result is a success; no error is available.");

    /// <summary>
    /// Attempts to read the success value without throwing. Returns <c>true</c> and sets
    /// <paramref name="value"/> when successful; otherwise returns <c>false</c> and sets
    /// <paramref name="error"/>.
    /// </summary>
    public bool TryGetValue(out T value, out E error)
    {
        value = _value!;
        error = _error!;
        return IsOk;
    }

    /// <summary>Folds the result into a single value, handling both the success and error cases.</summary>
    public TResult Match<TResult>(Func<T, TResult> onOk, Func<E, TResult> onError)
    {
        ArgumentNullException.ThrowIfNull(onOk);
        ArgumentNullException.ThrowIfNull(onError);
        return IsOk ? onOk(_value!) : onError(_error!);
    }

    /// <summary>Projects the success value while preserving the error unchanged.</summary>
    public Result<TNext, E> Map<TNext>(Func<T, TNext> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return IsOk ? Result<TNext, E>.Ok(map(_value!)) : Result<TNext, E>.Err(_error!);
    }

    /// <summary>Widens the error type when <typeparamref name="EWide"/> is assignable from <typeparamref name="E"/>.</summary>
    public Result<T, EWide> MapError<EWide>(Func<E, EWide> mapError)
    {
        ArgumentNullException.ThrowIfNull(mapError);
        return IsOk ? Result<T, EWide>.Ok(_value!) : Result<T, EWide>.Err(mapError(_error!));
    }
}
