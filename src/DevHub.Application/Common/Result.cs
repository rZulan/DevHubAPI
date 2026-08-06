namespace DevHub.Application.Common;

public sealed class Result<T>
{
    private Result(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Value = value;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public Error? Error { get; }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);
}
