using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace DevHub.Api.OpenApi;

internal sealed class ApiDocumentMetadataTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "DevHub API",
            Version = "v1",
            Description =
                "API for DevHub accounts and user profiles. " +
                "Authentication endpoints issue both an HTTP-only authentication cookie " +
                "and a JWT access/refresh token pair. Protected endpoints accept either " +
                "the authentication cookie or an `Authorization: Bearer <token>` header."
        };

        return Task.CompletedTask;
    }
}
