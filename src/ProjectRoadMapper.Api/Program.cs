using ProjectRoadMapper.Api.OpenApi;
using ProjectRoadMapper.Api.Errors;
using ProjectRoadMapper.Application;
using ProjectRoadMapper.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
            ApiProblemDetails.CreateModelValidationResult(
                context.ModelState,
                context.HttpContext);
    });

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
        ApiProblemDetails.Standardize(
            context.ProblemDetails,
            context.HttpContext,
            $"Http.{context.ProblemDetails.Status ?? StatusCodes.Status500InternalServerError}");
});

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiDocumentMetadataTransformer>();
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(async context =>
{
    var problemDetailsService =
        context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

    await problemDetailsService.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context.HttpContext,
        ProblemDetails = ApiProblemDetails.CreateStatusCodeProblem(
            context.HttpContext.Response.StatusCode,
            context.HttpContext)
    });
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
