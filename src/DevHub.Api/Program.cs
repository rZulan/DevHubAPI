using DevHub.Api.OpenApi;
using DevHub.Api.Errors;
using DevHub.Application;
using DevHub.Infrastructure;
using DevHub.Api.Realtime;
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

builder.Services.AddSignalR(options =>
    options.EnableDetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, ChatUserIdProvider>();
builder.Services.AddSingleton<WorkshopPresenceTracker>();
builder.Services.AddSingleton<IdeasSelectionTracker>();
builder.Services.AddHostedService<WorkshopPresenceCleanup>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiDocumentMetadataTransformer>();
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

var allowedOrigins = builder.Configuration
    .GetSection("Frontend:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
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

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<WorkshopHub>("/hubs/workshop");
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
