using MediatR;
using DevHub.Application.Common;

namespace DevHub.Application.Users.Queries.GetUserById;

public sealed record GetUserByIdQuery(Guid UserId) : IRequest<Result<UserResponse>>;
