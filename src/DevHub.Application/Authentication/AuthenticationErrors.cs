using DevHub.Application.Common;

namespace DevHub.Application.Authentication;

internal static class AuthenticationErrors
{
    public static Error InvalidRegistration(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(
            "Authentication.InvalidRegistration",
            "One or more registration fields are invalid.",
            ErrorType.Validation,
            validationErrors);

    public static readonly Error EmailAlreadyExists = new(
        "Authentication.EmailAlreadyExists",
        "A user with this email address already exists.",
        ErrorType.Conflict);

    public static readonly Error InvalidCredentials = new(
        "Authentication.InvalidCredentials",
        "The email address or password is invalid.",
        ErrorType.Unauthorized);

    public static readonly Error InvalidRefreshToken = new(
        "Authentication.InvalidRefreshToken",
        "The refresh token is invalid or has expired.",
        ErrorType.Unauthorized);
}
