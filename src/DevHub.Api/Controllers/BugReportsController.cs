using System.ComponentModel.DataAnnotations;
using DevHub.Application.BugReports;
using DevHub.Infrastructure.BugReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevHub.Api.Controllers;

public sealed class SubmitBugForm
{
    public Guid SubmissionId {get; set;}
    [Required, EmailAddress, StringLength(254)] public string Email {get; set;} = "";
    [Required, StringLength(200)] public string Title {get; set;} = "";
    [Required, StringLength(20000)] public string Description {get; set;} = "";
    [StringLength(10000)] public string Steps {get; set;} = "";
    [StringLength(1000)] public string Environment {get; set;} = "";
    public Guid? CategoryId {get; set;}
    [Required] public string Priority {get; set;} = "medium";
    public List<IFormFile> Files {get; set;} = [];
    public async Task<SubmitBugRequest?> Read(CancellationToken ct)
    {
        if (Files.Count > BugFileValidation.MaxFiles || Files.Any(f => f.Length is <= 0 or > BugFileValidation.MaxFileBytes) || Files.Sum(f => f.Length) > BugFileValidation.MaxTotalBytes) return null;
        var attachments = new List<BugAttachmentInput>();
        foreach (var file in Files) {await using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct); attachments.Add(new(file.FileName, buffer.ToArray()));}
        return new(SubmissionId, Email, Title, Description, Steps ?? "", Environment ?? "", CategoryId, Priority, attachments);
    }
}
public sealed record SaveBugPortalRequest(bool Enabled, bool Rotate, int Revision);
public sealed record SaveBugCategoryRequest([Required, StringLength(50)] string Name, bool Archived);

[Tags("Bug reports")]
[Authorize]
[Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/bug-reports")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BugReportsController(IBugReportsService reports) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, Guid projectId, [FromQuery] string? status, [FromQuery] Guid? categoryId, [FromQuery] string? search, [FromQuery] int page = 0, CancellationToken ct = default)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await reports.List(new(organizationId, projectId, userId), status, categoryId, search, page, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpGet("{reportId:guid}")]
    public async Task<IActionResult> Get(Guid organizationId, Guid projectId, Guid reportId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await reports.Get(new(organizationId, projectId, userId), reportId, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpPut("{reportId:guid}")]
    public async Task<IActionResult> Triage(Guid organizationId, Guid projectId, Guid reportId, TriageBugRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await reports.Triage(new(organizationId, projectId, userId), reportId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpGet("{reportId:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Download(Guid organizationId, Guid projectId, Guid reportId, Guid attachmentId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await reports.Download(new(organizationId, projectId, userId), reportId, attachmentId, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(result.Value!.Content, "application/octet-stream", result.Value.FileName);
    }
    [HttpGet("portal")]
    public async Task<IActionResult> Portal(Guid organizationId, Guid projectId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await reports.GetPortal(new(organizationId, projectId, userId), ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpPut("portal")]
    public async Task<IActionResult> SavePortal(Guid organizationId, Guid projectId, SaveBugPortalRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await reports.SavePortal(new(organizationId, projectId, userId), request.Enabled, request.Rotate, request.Revision, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpPost("categories")]
    [HttpPut("categories/{categoryId:guid}")]
    public async Task<IActionResult> SaveCategory(Guid organizationId, Guid projectId, SaveBugCategoryRequest request, CancellationToken ct, Guid? categoryId = null)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await reports.SaveCategory(new(organizationId, projectId, userId), categoryId, request.Name, request.Archived, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpPost]
    [RequestSizeLimit(28 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 28 * 1024 * 1024)]
    public async Task<IActionResult> Submit(Guid organizationId, Guid projectId, [FromForm] SubmitBugForm request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var input = await request.Read(ct);
        if (input is null) return Failure(new("Bugs.Files", "Attach up to 5 files, 10 MB each and 25 MB total.", DevHub.Application.Common.ErrorType.Validation));
        var result = await reports.SubmitTeam(new(organizationId, projectId, userId), input, ct);
        return result.IsSuccess ? StatusCode(201, result.Value) : Failure(result.Error!);
    }
}

[Tags("Public bug reports")]
[AllowAnonymous]
[Route("api/public/bug-reports/{token}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicBugReportsController(IBugReportsService reports) : ApiControllerBase
{
    [HttpGet]
    [EnableRateLimiting("public-bug-read")]
    public async Task<IActionResult> Form(string token, CancellationToken ct)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var result = await reports.GetPublicForm(token, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpPost]
    [EnableRateLimiting("public-bug-submit")]
    [RequestSizeLimit(28 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 28 * 1024 * 1024)]
    public async Task<IActionResult> Submit(string token, [FromForm] SubmitBugForm request, CancellationToken ct)
    {
        var form = await reports.GetPublicForm(token, ct);
        if (!form.IsSuccess) return Failure(form.Error!);
        var input = await request.Read(ct);
        if (input is null) return Failure(new("Bugs.Files", "Attach up to 5 files, 10 MB each and 25 MB total.", DevHub.Application.Common.ErrorType.Validation));
        var result = await reports.SubmitPublic(token, input, ct);
        return result.IsSuccess ? StatusCode(201, result.Value) : Failure(result.Error!);
    }
}
