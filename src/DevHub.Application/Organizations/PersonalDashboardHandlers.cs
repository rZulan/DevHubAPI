using System.Text.Json;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using MediatR;

namespace DevHub.Application.Organizations;

internal sealed class GetPersonalDashboardQueryHandler(IOrganizationRepository repository)
    : IRequestHandler<GetPersonalDashboardQuery, Result<PersonalDashboardResponse>>
{
    public async Task<Result<PersonalDashboardResponse>> Handle(GetPersonalDashboardQuery request, CancellationToken cancellationToken)
    {
        var member = await repository.GetDashboardMemberAsync(request.OrganizationId, request.RequestingUserId, cancellationToken);
        if (member is null) return Result<PersonalDashboardResponse>.Failure(OrganizationErrors.NotFound);
        var widgets = string.IsNullOrEmpty(member.PersonalDashboardLayoutJson)
            ? [] : JsonSerializer.Deserialize<DashboardWidgetDefinition[]>(member.PersonalDashboardLayoutJson) ?? [];
        return Result<PersonalDashboardResponse>.Success(new(widgets, member.PersonalDashboardSavedAtUtc));
    }
}

internal sealed class SavePersonalDashboardCommandHandler(IOrganizationRepository repository, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<SavePersonalDashboardCommand, Result<PersonalDashboardResponse>>
{
    public async Task<Result<PersonalDashboardResponse>> Handle(SavePersonalDashboardCommand request, CancellationToken cancellationToken)
    {
        var member = await repository.GetDashboardMemberAsync(request.OrganizationId, request.RequestingUserId, cancellationToken);
        if (member is null) return Result<PersonalDashboardResponse>.Failure(OrganizationErrors.NotFound);
        var errors = OrganizationDashboard.Validate(request.Widgets);
        if (errors.Count > 0) return Result<PersonalDashboardResponse>.Failure(OrganizationErrors.Invalid(errors));
        member.SavePersonalDashboard(JsonSerializer.Serialize(request.Widgets), clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<PersonalDashboardResponse>.Success(new(request.Widgets, member.PersonalDashboardSavedAtUtc));
    }
}
