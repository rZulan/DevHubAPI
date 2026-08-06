namespace ProjectRoadMapper.Application.Abstractions.Persistence;

public sealed class UniqueConstraintViolationException(
    string message,
    Exception innerException)
    : Exception(message, innerException);
