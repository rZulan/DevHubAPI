using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ProjectRoadMapper.Api.OpenApi;

internal sealed class ApiDocumentMetadataTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Project Road Mapper API",
            Version = "v1",
            Description =
                "API for Project Road Mapper accounts and user profiles. " +
                "Authentication endpoints issue both an HTTP-only authentication cookie " +
                "and a JWT access/refresh token pair. Protected endpoints accept either " +
                "the authentication cookie or an `Authorization: Bearer <token>` header."
        };

        return Task.CompletedTask;
    }
}
