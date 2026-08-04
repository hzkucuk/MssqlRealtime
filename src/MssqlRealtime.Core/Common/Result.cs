namespace MssqlRealtime.Core.Common;

/// <summary>
/// Error-as-value result. Exceptions are for exceptional faults, never control flow.
/// </summary>
public readonly record struct Result
{
    private Result(bool ok, string? error, string? code)
    {
        IsSuccess = ok;
        Error = error;
        Code = code;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public string? Code { get; }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error, string? code = null) => new(false, error, code);
}

public readonly record struct Result<T>
{
    private Result(bool ok, T? value, string? error, string? code)
    {
        IsSuccess = ok;
        Value = value;
        Error = error;
        Code = code;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string? Error { get; }
    public string? Code { get; }

    public static Result<T> Success(T value) => new(true, value, null, null);
    public static Result<T> Failure(string error, string? code = null) => new(false, default, error, code);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}
