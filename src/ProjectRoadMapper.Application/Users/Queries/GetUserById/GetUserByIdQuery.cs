using MediatR;
using ProjectRoadMapper.Application.Common;

namespace ProjectRoadMapper.Application.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<Result<UserResponse>>;
