namespace Celd.Api.Application;

/// <summary>The five error categories FR-ERR-001 requires the API to distinguish.</summary>
public enum ErrorKind
{
    Validation,
    Unauthenticated,
    Forbidden,
    NotFound,
    Conflict,
}

public sealed record OperationError(ErrorKind Kind, string Code, string Message)
{
    public static OperationError Validation(string code, string message) => new(ErrorKind.Validation, code, message);

    public static OperationError Unauthenticated() => new(ErrorKind.Unauthenticated, "unauthenticated", "Sign in to continue.");

    public static OperationError Forbidden() => new(ErrorKind.Forbidden, "forbidden", "Your role is not allowed to do this.");

    public static OperationError NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static OperationError Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);
}

public sealed class OperationResult<T>
{
    private OperationResult(T? value, OperationError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public OperationError? Error { get; }

    public bool Succeeded => Error is null;

    public static OperationResult<T> Ok(T value) => new(value, null);

    public static OperationResult<T> Fail(OperationError error) => new(default, error);

    public static implicit operator OperationResult<T>(OperationError error) => Fail(error);
}
