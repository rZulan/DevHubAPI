namespace DevHub.Application.Abstractions.Persistence;

public sealed class PersistenceConcurrencyException(
    string message,
    Exception innerException)
    : Exception(message, innerException);
