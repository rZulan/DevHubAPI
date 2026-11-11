using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Users;
using DevHub.Domain.Common;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using MediatR;
using System.Text.Json;

namespace DevHub.Application.Organizations;

internal static class OrganizationErrors
{
    public static readonly Error NotFound = new(
        "Organizations.NotFound",
        "The organization was not found or you are not one of its members.",
        ErrorType.NotFound);

    public static readonly Error NameAlreadyExists = new(
        "Organizations.NameAlreadyExists",
        "An organization with this name already exists.",
        ErrorType.Conflict);

    public static readonly Error UserNotFound = new(
        "Users.NotFound",
        "The requested user was not found.",
        ErrorType.NotFound);

    public static readonly Error OwnerRequired = new(
        "Organizations.OwnerRequired",
        "Only an organization owner can perform this action.",
        ErrorType.Forbidden);
    public static readonly Error MemberNotFound = new("Organizations.MemberNotFound", "Choose an existing organization member.", ErrorType.NotFound);

    public static readonly Error PermissionRequired = new(
        "Organizations.PermissionRequired",
        "You do not have permission to perform this organization action.",
        ErrorType.Forbidden);

    public static readonly Error RoleNotFound = new(
        "Organizations.RoleNotFound",
        "The organization role was not found.",
        ErrorType.NotFound);

    public static readonly Error RoleHierarchy = new(
        "Organizations.RoleHierarchy",
        "You can only manage roles and members below your highest role.",
        ErrorType.Forbidden);

    public static readonly Error CannotGrantPermission = new(
        "Organizations.CannotGrantPermission",
        "You cannot grant a permission you do not have yourself.",
        ErrorType.Forbidden);

    public static readonly Error LastOwner = new(
        "Organizations.LastOwner",
        "Transfer ownership to another member before leaving.",
        ErrorType.Conflict);

    public static readonly Error ProtectedRole = new(
        "Organizations.ProtectedRole",
        "This protected role cannot be deleted or removed from its members.",
        ErrorType.Conflict);

    public static readonly Error RoleNameAlreadyExists = new(
        "Organizations.RoleNameAlreadyExists",
        "A role with this name already exists in the organization.",
        ErrorType.Conflict);

    public static readonly Error InvalidInvite = new(
        "Organizations.InvalidInvite",
        "This invite link is invalid or has expired.",
        ErrorType.NotFound);

    public static Error Invalid(IReadOnlyDictionary<string, string[]> errors) => new(
        "Organizations.ValidationFailed",
        "One or more organization fields are invalid.",
        ErrorType.Validation,
        errors);
}

internal static class OrganizationMappings
{
    public static OrganizationResponse ToResponse(this Organization organization) => new(
        organization.Id,
        organization.OwnerUserId,
        organization.Name,
        organization.Description,
        organization.CreatedAtUtc,
        organization.UpdatedAtUtc,
        organization.Members.Count,
        organization.Teams.Count,
        organization.AvatarUrl);

    public static UserResponse ToResponse(this User user) => user.ToUserResponse();

    public static OrganizationRoleResponse ToResponse(
        this OrganizationRole role,
        Organization organization) => new(
        role.Id,
        role.Name,
        role.Color,
        role.Position,
        role.IsOwnerRole,
        role.IsDefaultRole,
        role.IsOwnerRole || role.Permissions.Contains(OrganizationPermissions.Administrator)
            ? OrganizationPermissions.All
            : role.Permissions,
        organization.Members.Count(member =>
            member.RoleAssignments.Any(assignment => assignment.RoleId == role.Id)));

    public static OrganizationMemberResponse ToMemberResponse(
        this User user,
        OrganizationMember member)
    {
        var response = user.ToUserResponse();
        return new OrganizationMemberResponse(
            response.Id,
            response.Email,
            response.FirstName,
            response.LastName,
            response.CreatedAtUtc,
            response.Username,
            response.DateOfBirth,
            response.AvatarUrl,
            response.ConnectedAccounts,
            member.IsOwner,
            member.RoleAssignments.Select(assignment => assignment.RoleId).ToArray(),
            member.JoinedAtUtc,
            member.Nickname);
    }
}

internal sealed class CreateOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateOrganizationCommand, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.Invalid(errors));
        }

        if (await userRepository.GetByIdAsync(request.RequestingUserId, cancellationToken) is null)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.UserNotFound);
        }

        if (await organizationRepository.NameExistsAsync(request.Name, cancellationToken: cancellationToken))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NameAlreadyExists);
        }

        var organization = Organization.Create(
            request.Name,
            request.Description,
            request.RequestingUserId,
            timeProvider.GetUtcNow());
        organizationRepository.Add(organization);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NameAlreadyExists);
        }

        return Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}

internal sealed class UpdateOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateOrganizationCommand, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(
        UpdateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.Invalid(errors));
        }

        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.HasPermission(
            request.RequestingUserId,
            OrganizationPermissions.ManageOrganization))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.PermissionRequired);
        }

        if (await organizationRepository.NameExistsAsync(
                request.Name,
                organization.Id,
                cancellationToken))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NameAlreadyExists);
        }

        organization.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NameAlreadyExists);
        }

        return Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}

internal sealed class DeleteOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteOrganizationCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        DeleteOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.IsOwner(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.OwnerRequired);
        }

        organizationRepository.Remove(organization);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class AddOrganizationMemberCommandHandler(
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<AddOrganizationMemberCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        AddOrganizationMemberCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.HasPermission(
            request.RequestingUserId,
            OrganizationPermissions.ManageMembers))
        {
            return Result<Unit>.Failure(OrganizationErrors.PermissionRequired);
        }

        if (await userRepository.GetByIdAsync(request.UserId, cancellationToken) is null)
        {
            return Result<Unit>.Failure(OrganizationErrors.UserNotFound);
        }

        if (organization.AddMember(request.UserId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class RemoveOrganizationMemberCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<RemoveOrganizationMemberCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveOrganizationMemberCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.CanManageMember(request.RequestingUserId, request.UserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        }

        if (organization.RemoveMember(request.UserId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class ChangeOrganizationMemberNicknameCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ChangeOrganizationMemberNicknameCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(ChangeOrganizationMemberNicknameCommand request, CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ChangeNicknames))
            return Result<Unit>.Failure(OrganizationErrors.PermissionRequired);
        if (!organization.HasMember(request.UserId))
            return Result<Unit>.Failure(OrganizationErrors.NotFound);

        organization.ChangeNickname(request.UserId, request.Nickname);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class ListOrganizationsQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<ListOrganizationsQuery, Result<IReadOnlyList<OrganizationResponse>>>
{
    public async Task<Result<IReadOnlyList<OrganizationResponse>>> Handle(
        ListOrganizationsQuery request,
        CancellationToken cancellationToken)
    {
        var organizations = await organizationRepository.ListSummariesForUserAsync(
            request.RequestingUserId,
            cancellationToken);
        return Result<IReadOnlyList<OrganizationResponse>>.Success(
            organizations);
    }
}

internal sealed class GetOrganizationQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<GetOrganizationQuery, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(
        GetOrganizationQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        return organization is null || !organization.HasMember(request.RequestingUserId)
            ? Result<OrganizationResponse>.Failure(OrganizationErrors.NotFound)
            : Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}

internal sealed class ListOrganizationMembersQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<ListOrganizationMembersQuery, Result<IReadOnlyList<OrganizationMemberResponse>>>
{
    public async Task<Result<IReadOnlyList<OrganizationMemberResponse>>> Handle(
        ListOrganizationMembersQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetMembershipForUserAsync(
            request.OrganizationId, request.RequestingUserId,
            cancellationToken);
        if (organization is null)
        {
            return Result<IReadOnlyList<OrganizationMemberResponse>>.Failure(OrganizationErrors.NotFound);
        }

        var users = await organizationRepository.ListMembersAsync(
            organization.Id,
            cancellationToken);
        var membersById = organization.Members.ToDictionary(member => member.UserId);
        return Result<IReadOnlyList<OrganizationMemberResponse>>.Success(
            users.Where(user => membersById.ContainsKey(user.Id))
                .Select(user => user.ToMemberResponse(membersById[user.Id])).ToArray());
    }
}

internal sealed class CreateOrganizationInviteCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    DevHub.Application.Workshops.IWorkshopUsage usage,
    DevHub.Application.Workshops.WorkshopLimits limits)
    : IRequestHandler<CreateOrganizationInviteCommand, Result<OrganizationInviteResponse>>
{
    public async Task<Result<OrganizationInviteResponse>> Handle(
        CreateOrganizationInviteCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationInviteResponse>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.HasPermission(
            request.RequestingUserId,
            OrganizationPermissions.CreateInvites))
        {
            return Result<OrganizationInviteResponse>.Failure(OrganizationErrors.PermissionRequired);
        }

        var now = timeProvider.GetUtcNow();
        if (await usage.Reserve([
            new($"invite-user:{request.RequestingUserId:N}", limits.InvitesPerUserPerDay, 1, TimeSpan.FromDays(1)),
            new($"invite-workshop:{organization.Id:N}", limits.InvitesPerWorkshopPerDay, 1, TimeSpan.FromDays(1))
        ], cancellationToken) is { } quotaError) return Result<OrganizationInviteResponse>.Failure(quotaError);
        var token = OrganizationInviteCode.Create();
        var expiresAtUtc = now.AddDays(7);
        organizationRepository.AddInvite(OrganizationInvite.Create(
            organization.Id,
            request.RequestingUserId,
            OrganizationInviteCode.Hash(token),
            expiresAtUtc,
            now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<OrganizationInviteResponse>.Success(new(token, expiresAtUtc));
    }

}

internal sealed class AcceptOrganizationInviteCommandHandler(
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<AcceptOrganizationInviteCommand, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(
        AcceptOrganizationInviteCommand request,
        CancellationToken cancellationToken)
    {
        var code = OrganizationInviteCode.Normalize(request.Token);
        if (code is null)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.InvalidInvite);
        }

        var tokenHash = OrganizationInviteCode.Hash(code);
        var invite = await organizationRepository.GetInviteByTokenHashAsync(tokenHash, cancellationToken);
        if (invite is null || invite.IsExpired(timeProvider.GetUtcNow()))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.InvalidInvite);
        }

        if (await userRepository.GetByIdAsync(request.RequestingUserId, cancellationToken) is null)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.UserNotFound);
        }

        var organization = await organizationRepository.GetByIdAsync(
            invite.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasPermission(invite.CreatedByUserId, OrganizationPermissions.CreateInvites))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.InvalidInvite);
        }

        if (organization.AddMember(request.RequestingUserId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}

internal static class OrganizationDashboard
{
    private static readonly HashSet<string> SupportedSizes = ["full", "two-thirds", "half", "third", "sixth"];

    private static readonly Dictionary<string, (double Width, double Height)> MinimumDimensions = new(StringComparer.Ordinal)
    {
        ["quick-actions"] = (2, 1.5),
        ["quick-action-project"] = (1, 1),
        ["quick-action-tasks"] = (1, 1),
        ["quick-action-idea"] = (1, 1),
        ["quick-action-team"] = (1, 1),
        ["project-stats"] = (2, 1),
        ["team-distribution"] = (1.5, 1),
        ["welcome"] = (1.5, 1),
        ["assigned-tasks"] = (2, 1),
        ["recent-activity"] = (1.5, 1.5),
        ["online-members"] = (1, 1),
        ["project-overview"] = (1, 1),
        ["text"] = (0.5, 0.5)
    };

    public static IReadOnlyDictionary<string, string[]> Validate(
        IReadOnlyList<DashboardWidgetDefinition> widgets, int? widgetGap)
    {
        var errors = new Dictionary<string, string[]>();
        if (widgetGap is < 8 or > 24) errors[nameof(widgetGap)] = ["Widget gap must be between 8 and 24 pixels."];
        if (widgets.Count > 30)
        {
            errors[nameof(widgets)] = ["A dashboard can contain at most 30 widgets."];
            return errors;
        }

        if (widgets.Any(widget =>
            string.IsNullOrWhiteSpace(widget.Id) || widget.Id.Length > 100 ||
            widget.Type is null ||
            !SupportedSizes.Contains(widget.Size) ||
            !HasValidDimensions(widget) ||
            (widget.SectionId is not null && (string.IsNullOrWhiteSpace(widget.SectionId) || widget.SectionId.Length > 100)) ||
            !HasValidAppearance(widget.Appearance) ||
            !HasValidContent(widget)))
        {
            errors[nameof(widgets)] = ["One or more dashboard widgets are invalid."];
        }
        else if (widgets.Select(widget => widget.Id).Distinct(StringComparer.Ordinal).Count() != widgets.Count)
        {
            errors[nameof(widgets)] = ["Dashboard widget identifiers must be unique."];
        }
        return errors;
    }

    private static bool HasValidDimensions(DashboardWidgetDefinition widget)
    {
        var fixedSize = false;
        (double Width, double Height) minimum;
        if (NewDimensions.TryGetValue(widget.Type, out var template))
        {
            minimum = (template.Width, template.Height);
            fixedSize = template.Fixed;
        }
        else if (!MinimumDimensions.TryGetValue(widget.Type, out minimum)) return false;
        var width = widget.Width ?? widget.Size switch
        {
            "sixth" => 0.5,
            "third" => 1,
            "half" => 2,
            "two-thirds" => 3,
            _ => 4
        };
        var image = widget.Type.StartsWith("image-", StringComparison.Ordinal);
        // Older clients can still save the fixed chart sizes they already loaded.
        if (widget.Type.StartsWith("graph-", StringComparison.Ordinal) && width == (widget.Type is "graph-tasks" or "graph-todos" or "graph-bugs" ? 1.5 : 2))
            minimum.Width = width;
        if (widget.Type == "info-org-members" && widget.Height == 0.5) minimum.Height = 0.5;

        return (widget.Column is null && widget.Row is null ||
                widget.Column is >= 0 and < 10 && widget.Column * 2 % 1 == 0 && widget.Column + width * 2 <= 10 &&
                widget.Row is >= 0 and <= 120 && widget.Row % 1 == 0) &&
               width >= minimum.Width && width <= (image ? 5 : 4) && (fixedSize || width * 2 % 1 == 0) &&
               widget.Height >= minimum.Height && widget.Height <= (image ? 4 : 2) && widget.Height * 2 % 1 == 0 &&
               (!fixedSize || width == minimum.Width && widget.Height == minimum.Height);
    }

    private static bool HasValidAppearance(DashboardWidgetAppearance? appearance)
    {
        if (appearance is null) return true;
        if (appearance.Radius is < 0 or > 48 || appearance.Opacity is < 0 or > 100) return false;
        var color = appearance.Color;
        return color is null or "primary" or "secondary" or "accent" or "muted" or "background" or "card" or "popover" or "sidebar"
            || color.Length == 7 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
    }

    private static bool HasValidContent(DashboardWidgetDefinition widget)
    {
        if (string.IsNullOrEmpty(widget.Content)) return true;
        if (!widget.Type.StartsWith("image-", StringComparison.Ordinal))
        {
            var limit = widget.Type.StartsWith("text-rows-", StringComparison.Ordinal) ? 120000 : widget.Type == "text" ? 16000 : 2000;
            return widget.Content.Length <= limit;
        }

        // A caption can store up to 250 separately formatted characters alongside the image.
        const int metadataLimit = 60000;
        if (widget.Content.Length > metadataLimit + AvatarImage.MaxLength) return false;
        try
        {
            using var document = JsonDocument.Parse(widget.Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!document.RootElement.TryGetProperty("url", out var url) || url.ValueKind == JsonValueKind.Null)
                return widget.Content.Length <= metadataLimit;
            if (url.ValueKind != JsonValueKind.String || url.GetString() is not { } source) return false;
            return source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? widget.Content.Length - source.Length <= metadataLimit && AvatarImage.IsValid(source)
                : widget.Content.Length <= metadataLimit && source.Length <= 900;
        }
        catch (JsonException) { return false; }
    }

    private static readonly Dictionary<string, (double Width, double Height, bool Fixed)> NewDimensions = BuildWidgetDimensions();

    private static Dictionary<string, (double Width, double Height, bool Fixed)> BuildWidgetDimensions()
    {
        var result = new Dictionary<string, (double, double, bool)>(StringComparer.Ordinal);
        foreach (var link in new[] { "teams", "org-chart", "sdlc", "todo", "calendar", "bug-reports", "erd", "ui-editor", "uml", "project-wiki", "project" })
        {
            result.Add($"link-{link}-card", (1.25, 1, true));
            result.Add($"link-{link}-chip", (1, 0.5, true));
        }
        foreach (var rows in new[] { 1, 2, 3 }) result.Add($"text-rows-{rows}", (1, rows == 3 ? 1 : 0.5, false));
        foreach (var frame in new[] { "top", "top-inset", "left", "right", "left-inset", "right-inset", "full", "rounded" })
            result.Add($"image-{frame}", (0.5, 0.5, false));
        foreach (var graph in new[] { "tasks", "todos", "bugs" })
            result.Add($"graph-{graph}", (2.5, 1, true));
        result.Add("graph-bug-status", (2.5, 1.5, true));
        foreach (var graph in new[] { "todo-activity", "task-activity" })
            result.Add($"graph-{graph}", (2.5, 1, true));
        result.Add("info-org-members", (1.5, 1, true));
        foreach (var info in new[] { "org-todos", "team-todos", "personal-todos", "org-tasks", "team-tasks", "personal-tasks", "schedule", "org-teams", "team-members" })
            result.Add($"info-{info}", (1.5, 1, false));
        return result;
    }

    public static OrganizationDashboardResponse ToDashboardResponse(this Organization organization)
    {
        if (string.IsNullOrWhiteSpace(organization.DashboardLayoutJson))
        {
            return new OrganizationDashboardResponse([], organization.DashboardPublishedAtUtc, organization.DashboardWidgetGap);
        }

        try
        {
            var widgets = JsonSerializer.Deserialize<DashboardWidgetDefinition[]>(
                organization.DashboardLayoutJson) ?? [];
            return new OrganizationDashboardResponse(widgets, organization.DashboardPublishedAtUtc, organization.DashboardWidgetGap);
        }
        catch (JsonException)
        {
            return new OrganizationDashboardResponse([], organization.DashboardPublishedAtUtc, organization.DashboardWidgetGap);
        }
    }
}

internal sealed class GetOrganizationDashboardQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<GetOrganizationDashboardQuery, Result<OrganizationDashboardResponse>>
{
    public async Task<Result<OrganizationDashboardResponse>> Handle(
        GetOrganizationDashboardQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetDashboardForMemberAsync(
            request.OrganizationId, request.RequestingUserId, cancellationToken);
        return organization is null
            ? Result<OrganizationDashboardResponse>.Failure(OrganizationErrors.NotFound)
            : Result<OrganizationDashboardResponse>.Success(organization.ToDashboardResponse());
    }
}

internal sealed class PublishOrganizationDashboardCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<PublishOrganizationDashboardCommand, Result<OrganizationDashboardResponse>>
{
    public async Task<Result<OrganizationDashboardResponse>> Handle(
        PublishOrganizationDashboardCommand request,
        CancellationToken cancellationToken)
    {
        var errors = OrganizationDashboard.Validate(request.Widgets, request.WidgetGap);
        if (errors.Count > 0)
        {
            return Result<OrganizationDashboardResponse>.Failure(OrganizationErrors.Invalid(errors));
        }

        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationDashboardResponse>.Failure(OrganizationErrors.NotFound);
        }
        if (!organization.HasPermission(
            request.RequestingUserId, OrganizationPermissions.ManageDashboard))
        {
            return Result<OrganizationDashboardResponse>.Failure(OrganizationErrors.PermissionRequired);
        }

        organization.PublishDashboard(
            JsonSerializer.Serialize(request.Widgets), timeProvider.GetUtcNow(), request.WidgetGap);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrganizationDashboardResponse>.Success(organization.ToDashboardResponse());
    }
}

internal sealed class ListOrganizationRolesQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<ListOrganizationRolesQuery, Result<IReadOnlyList<OrganizationRoleResponse>>>
{
    public async Task<Result<IReadOnlyList<OrganizationRoleResponse>>> Handle(
        ListOrganizationRolesQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<IReadOnlyList<OrganizationRoleResponse>>.Failure(OrganizationErrors.NotFound);
        }

        return Result<IReadOnlyList<OrganizationRoleResponse>>.Success(
            organization.Roles.OrderBy(role => role.Position)
                .Select(role => role.ToResponse(organization)).ToArray());
    }
}

internal sealed class CreateOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateOrganizationRoleCommand, Result<OrganizationRoleResponse>>
{
    public async Task<Result<OrganizationRoleResponse>> Handle(
        CreateOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.NotFound);
        }
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageRoles))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.PermissionRequired);
        }
        if (!organization.CanGrantPermissions(request.RequestingUserId, request.Permissions))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.CannotGrantPermission);
        }
        if (organization.Roles.Any(role => role.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleNameAlreadyExists);
        }

        var role = organization.AddRole(
            request.Name, request.Color, request.Permissions, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrganizationRoleResponse>.Success(role.ToResponse(organization));
    }
}

internal sealed class UpdateOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateOrganizationRoleCommand, Result<OrganizationRoleResponse>>
{
    public async Task<Result<OrganizationRoleResponse>> Handle(
        UpdateOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.NotFound);
        }
        var role = organization.Roles.SingleOrDefault(candidate => candidate.Id == request.RoleId);
        if (role is null) return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleNotFound);
        if (role.IsOwnerRole && !organization.IsOwner(request.RequestingUserId))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.OwnerRequired);
        }
        if (!role.IsOwnerRole && !organization.CanManageRole(request.RequestingUserId, role))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleHierarchy);
        }
        if (!role.IsOwnerRole && !role.IsDefaultRole &&
            !organization.IsOwner(request.RequestingUserId) &&
            request.Position <= organization.HighestRolePosition(request.RequestingUserId))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleHierarchy);
        }
        if (!role.IsOwnerRole && !organization.CanGrantPermissions(request.RequestingUserId, request.Permissions))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.CannotGrantPermission);
        }
        if (organization.Roles.Any(candidate => candidate.Id != role.Id &&
            candidate.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleNameAlreadyExists);
        }

        role.Update(
            request.Name,
            role.IsOwnerRole ? role.Color : request.Color,
            role.IsOwnerRole || role.IsDefaultRole ? role.Position : request.Position,
            role.IsOwnerRole ? OrganizationPermissions.All : request.Permissions,
            timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrganizationRoleResponse>.Success(role.ToResponse(organization));
    }
}

internal sealed class DeleteOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteOrganizationRoleCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        DeleteOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        var role = organization.Roles.SingleOrDefault(candidate => candidate.Id == request.RoleId);
        if (role is null) return Result<Unit>.Failure(OrganizationErrors.RoleNotFound);
        if (role.IsOwnerRole || role.IsDefaultRole)
        {
            return Result<Unit>.Failure(OrganizationErrors.ProtectedRole);
        }
        if (!organization.CanManageRole(request.RequestingUserId, role))
        {
            return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        }

        organization.DeleteRole(role.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class AssignOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<AssignOrganizationRoleCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        AssignOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        var role = organization.Roles.SingleOrDefault(candidate => candidate.Id == request.RoleId);
        if (role is null) return Result<Unit>.Failure(OrganizationErrors.RoleNotFound);
        if (role.IsOwnerRole) return Result<Unit>.Failure(OrganizationErrors.ProtectedRole);
        if (!organization.CanAssignRole(request.RequestingUserId, request.UserId, role))
        {
            return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        }

        if (organization.AssignRole(request.UserId, role.Id, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class RemoveOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveOrganizationRoleCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        var role = organization.Roles.SingleOrDefault(candidate => candidate.Id == request.RoleId);
        if (role is null) return Result<Unit>.Failure(OrganizationErrors.RoleNotFound);
        if (role.IsOwnerRole || role.IsDefaultRole)
        {
            return Result<Unit>.Failure(OrganizationErrors.ProtectedRole);
        }
        if (!organization.CanAssignRole(request.RequestingUserId, request.UserId, role))
        {
            return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        }

        if (organization.RemoveRole(request.UserId, role.Id))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class TransferOrganizationOwnershipCommandHandler(
    IOrganizationRepository organizationRepository,
    TimeProvider timeProvider)
    : IRequestHandler<TransferOrganizationOwnershipCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        TransferOrganizationOwnershipCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        if (!organization.IsOwner(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.OwnerRequired);
        }
        if (!organization.HasMember(request.UserId)) return Result<Unit>.Failure(OrganizationErrors.MemberNotFound);
        if (request.UserId == request.RequestingUserId) return Result<Unit>.Failure(new("Organizations.InvalidTransfer", "Choose another organization member.", ErrorType.Validation));
        return await organizationRepository.TryTransferOwnershipAsync(organization.Id, request.RequestingUserId, request.UserId, timeProvider.GetUtcNow(), cancellationToken)
            ? Result<Unit>.Success(Unit.Value)
            : Result<Unit>.Failure(new("Organizations.OwnershipChanged", "Ownership changed in another session. Refresh and try again.", ErrorType.Conflict));
    }
}

internal sealed class LeaveOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<LeaveOrganizationCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        LeaveOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        if (organization.IsOwner(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.LastOwner);
        }
        if (!organization.RemoveMember(request.RequestingUserId, timeProvider.GetUtcNow()))
        {
            return Result<Unit>.Failure(OrganizationErrors.PermissionRequired);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}
