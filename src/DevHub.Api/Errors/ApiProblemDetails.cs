using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using DevHub.Application.Common;

namespace DevHub.Api.Errors;

internal static class ApiProblemDetails
{
    public const string ContentType = "application/problem+json";

    public static ObjectResult CreateResult(Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);

        var statusCode = GetStatusCode(error.Type);
        ProblemDetails problemDetails;

        if (error.Type == ErrorType.Validation)
        {
            var validationErrors = error.ValidationErrors?.ToDictionary(
                    errorEntry => errorEntry.Key,
                    errorEntry => errorEntry.Value,
                    StringComparer.Ordinal) ??
                new Dictionary<string, string[]>(StringComparer.Ordinal);

            problemDetails = new ValidationProblemDetails(validationErrors);
        }
        else
        {
            problemDetails = new ProblemDetails();
        }

        problemDetails.Type = CreateType(error.Code);
        problemDetails.Title = GetTitle(error.Type);
        problemDetails.Status = statusCode;
        problemDetails.Detail = error.Description;
        Standardize(problemDetails, httpContext, error.Code);

        var result = new ObjectResult(problemDetails)
        {
            StatusCode = statusCode
        };

        result.ContentTypes.Add(ContentType);
        return result;
    }

    public static ObjectResult CreateModelValidationResult(
        ModelStateDictionary modelState,
        HttpContext httpContext)
    {
        var problemDetails = new ValidationProblemDetails(modelState)
        {
            Type = CreateType("Validation.Failed"),
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest,
            Detail = "One or more request fields are invalid."
        };

        Standardize(problemDetails, httpContext, "Validation.Failed");

        var result = new BadRequestObjectResult(problemDetails);
        result.ContentTypes.Add(ContentType);
        return result;
    }

    public static ProblemDetails CreateStatusCodeProblem(
        int statusCode,
        HttpContext httpContext)
    {
        var problemDetails = new ProblemDetails
        {
            Type = "about:blank",
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Status = statusCode
        };

        Standardize(problemDetails, httpContext, $"Http.{statusCode}");
        return problemDetails;
    }

    public static void Standardize(
        ProblemDetails problemDetails,
        HttpContext httpContext,
        string? errorCode = null)
    {
        problemDetails.Instance ??= httpContext.Request.Path;

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            problemDetails.Extensions.TryAdd("code", errorCode);
        }

        problemDetails.Extensions.TryAdd(
            "traceId",
            Activity.Current?.Id ?? httpContext.TraceIdentifier);
    }

    private static int GetStatusCode(ErrorType errorType) =>
        errorType switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            _ => throw new ArgumentOutOfRangeException(
                nameof(errorType),
                errorType,
                "The error type does not have an HTTP status mapping.")
        };

    private static string GetTitle(ErrorType errorType) =>
        errorType switch
        {
            ErrorType.Validation => "Validation failed",
            ErrorType.Conflict => "Conflict",
            ErrorType.Unauthorized => "Authentication failed",
            ErrorType.Forbidden => "Permission denied",
            ErrorType.NotFound => "Resource not found",
            _ => throw new ArgumentOutOfRangeException(
                nameof(errorType),
                errorType,
                "The error type does not have a problem title.")
        };

    private static string CreateType(string errorCode) =>
        $"urn:devhub:error:{errorCode.ToLowerInvariant()}";
}
