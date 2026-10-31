namespace DevHub.Application.Common;

public enum ErrorType
{
    Validation,
    Conflict,
    Unauthorized,
    Forbidden,
    NotFound,
    Throttled
}

public sealed record Error(
    string Code,
    string Description,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    int? RetryAfterSeconds = null);
