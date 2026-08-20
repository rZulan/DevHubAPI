using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Users;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using MediatR;

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
        organization.Teams.Count);

    public static UserResponse ToResponse(this User user) => user.ToUserResponse();
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
    IUnitOfWork unitOfWork)
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

        if (organization.RemoveMember(request.UserId))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

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
        var organizations = await organizationRepository.ListForUserAsync(
            request.RequestingUserId,
            cancellationToken);
        return Result<IReadOnlyList<OrganizationResponse>>.Success(
            organizations.Select(organization => organization.ToResponse()).ToArray());
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
    : IRequestHandler<ListOrganizationMembersQuery, Result<IReadOnlyList<UserResponse>>>
{
    public async Task<Result<IReadOnlyList<UserResponse>>> Handle(
        ListOrganizationMembersQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<IReadOnlyList<UserResponse>>.Failure(OrganizationErrors.NotFound);
        }

        var users = await organizationRepository.ListMembersAsync(
            organization.Id,
            cancellationToken);
        return Result<IReadOnlyList<UserResponse>>.Success(
            users.Select(user => user.ToResponse()).ToArray());
    }
}
