using System.ComponentModel.DataAnnotations;

namespace DevHub.Application.Common;

internal static class RequestValidation
{
    public static IReadOnlyDictionary<string, string[]> Validate<TRequest>(TRequest request)
        where TRequest : class
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(request);

        if (Validator.TryValidateObject(
                request,
                validationContext,
                validationResults,
                validateAllProperties: true))
        {
            return new Dictionary<string, string[]>();
        }

        return validationResults
            .SelectMany(result =>
            {
                var memberNames = result.MemberNames.Any()
                    ? result.MemberNames
                    : [string.Empty];

                return memberNames.Select(memberName => new
                {
                    MemberName = memberName,
                    Message = result.ErrorMessage ?? "The value is invalid."
                });
            })
            .GroupBy(error => error.MemberName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(error => error.Message)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
    }
}
