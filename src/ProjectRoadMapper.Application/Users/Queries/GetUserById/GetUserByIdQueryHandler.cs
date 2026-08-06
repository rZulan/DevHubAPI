using MediatR;
using ProjectRoadMapper.Application.Abstractions.Persistence;
using ProjectRoadMapper.Application.Common;

namespace ProjectRoadMapper.Application.Users.Queries.GetUserById;

internal sealed class GetUserByIdQueryHandler(IUserRepository userRepository)
    : IRequestHandler<GetUserByIdQuery, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> Handle(
        GetUserByIdQuery request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            return Result<UserResponse>.Failure(new Error(
                "Users.NotFound",
                "The requested user was not found.",
                ErrorType.NotFound));
        }

        return Result<UserResponse>.Success(new UserResponse(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            user.CreatedAtUtc));
    }
}
