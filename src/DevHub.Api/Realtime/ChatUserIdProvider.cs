using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Realtime;

public sealed class ChatUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
}
