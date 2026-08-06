using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ProjectRoadMapper.Api.OpenApi;

internal sealed class BearerSecuritySchemeTransformer(
    IAuthenticationSchemeProvider authenticationSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var authenticationSchemes = await authenticationSchemeProvider.GetAllSchemesAsync();

        if (authenticationSchemes.All(scheme => scheme.Name != "Bearer"))
        {
            return;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT access token returned by the register, login, or refresh endpoint."
            }
        };

        var protectedEndpoints = context.DescriptionGroups
            .SelectMany(group => group.Items)
            .Where(description =>
                description.ActionDescriptor.EndpointMetadata
                    .OfType<IAuthorizeData>()
                    .Any() &&
                !description.ActionDescriptor.EndpointMetadata
                    .OfType<IAllowAnonymous>()
                    .Any())
            .ToArray();

        foreach (var description in protectedEndpoints)
        {
            var path = $"/{description.RelativePath?.TrimStart('/')}";

            if (!document.Paths.TryGetValue(path, out var pathItem) ||
                pathItem.Operations is null)
            {
                continue;
            }

            var operation = pathItem.Operations
                .SingleOrDefault(candidate =>
                    string.Equals(
                        candidate.Key.ToString(),
                        description.HttpMethod,
                        StringComparison.OrdinalIgnoreCase))
                .Value;

            if (operation is null)
            {
                continue;
            }

            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });
        }
    }
}
