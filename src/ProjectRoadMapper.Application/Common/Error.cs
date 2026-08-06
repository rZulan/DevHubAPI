namespace ProjectRoadMapper.Application.Common;

public enum ErrorType
{
    Validation,
    Conflict,
    Unauthorized,
    NotFound
}

public sealed record Error(
    string Code,
    string Description,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
