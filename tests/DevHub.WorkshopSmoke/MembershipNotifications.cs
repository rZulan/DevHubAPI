using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Ideas;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using DevHub.Api.Controllers;
using DevHub.Api.Realtime;
using DevHub.Application.Common;
using DevHub.Application.Organizations;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

internal static class MembershipNotifications
{
    public static async Task Run()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var events = new List<(string Target, string Event, string Organization)>();
        var success = true;
        var sender = Stub.Create<ISender>((method, args) =>
        {
            if (method.Name != "Send") throw new NotSupportedException(method.Name);
            var error = new Error("Test.Forbidden", "Denied", ErrorType.Forbidden);
            if (args![0] is CreateOrganizationRoleCommand or UpdateOrganizationRoleCommand)
                return Task.FromResult(success
                    ? Result<OrganizationRoleResponse>.Success(new(Guid.NewGuid(), "Role", "#ffffff", 1, false, false, [], 1))
                    : Result<OrganizationRoleResponse>.Failure(error));
            return args[0] is AcceptOrganizationInviteCommand or UpdateOrganizationCommand
                ? Task.FromResult(success
                    ? Result<OrganizationResponse>.Success(new(organizationId, actorId, "Test", null, DateTimeOffset.UtcNow, null, 2, 0))
                    : Result<OrganizationResponse>.Failure(error))
                : Task.FromResult(success ? Result<Unit>.Success(Unit.Value) : Result<Unit>.Failure(error));
        });
        var clients = Stub.Create<IHubClients>((method, args) =>
        {
            var target = $"{method.Name}:{args![0]}";
            return Stub.Create<IClientProxy>((_, sendArgs) =>
            {
                var token = (CancellationToken)sendArgs![2]!;
                if (token.IsCancellationRequested) throw new Exception("Committed notification used cancelled request token");
                events.Add((target, (string)sendArgs[0]!, (string)((object?[])sendArgs[1]!)[0]!));
                return Task.CompletedTask;
            });
        });
        var hub = Stub.Create<IHubContext<WorkshopHub>>((method, _) => method.Name == "get_Clients" ? clients : throw new NotSupportedException());
        var realtime = new WorkshopRealtime(new WorkshopSubscriptions(),
            Stub.Create<IOrganizationRepository>((_, _) => throw new Exception("Unexpected repository call")),
            Stub.Create<IIdeasService>((_, _) => throw new Exception("Unexpected Ideas call")), new WorkshopPresenceTracker(TimeProvider.System), hub);
        var controller = new OrganizationsController(sender, hub, realtime)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, actorId.ToString())], "test")),
                },
            },
        };
        // A disconnected caller must not suppress a broadcast after a successful write.
        var cancelled = new CancellationToken(true);
        var operations = new (string Name, Guid Affected, Func<Task<IActionResult>> Run)[]
        {
            ("direct join", memberId, () => controller.AddMember(organizationId, memberId, cancelled)),
            ("invite acceptance", actorId, () => controller.AcceptInvite("test-token", cancelled)),
            ("member removal", memberId, () => controller.RemoveMember(organizationId, memberId, cancelled)),
            ("voluntary leave", actorId, () => controller.Leave(organizationId, cancelled)),
        };
        foreach (var operation in operations)
        {
            events.Clear();
            success = true;
            await operation.Run();
            var revoked = operation.Name is "member removal" or "voluntary leave";
            if (revoked && !events.Contains(($"User:{operation.Affected}", "OrganizationAccessRevoked", organizationId.ToString())))
                throw new Exception("Missing immediate revocation event");
            if (events.Count != (revoked ? 3 : 2) ||
                !events.Contains(($"Group:{WorkshopHub.GetGroupName(organizationId)}", "OrganizationMembersChanged", organizationId.ToString())) ||
                !events.Contains(($"User:{operation.Affected}", "OrganizationMembershipChanged", organizationId.ToString())))
                throw new Exception($"Missing/scoped incorrectly: {operation.Name}");
            events.Clear();
            success = false;
            await operation.Run();
            if (events.Count != 0) throw new Exception($"Failed {operation.Name} was broadcast");
            Console.WriteLine($"PASS {operation.Name} notifies organization and affected user's tabs only after success");
        }
        var roleId = Guid.NewGuid();
        var role = new SaveOrganizationRoleRequest("Role", "#ffffff", 1, []);
        var changes = new (string Name, Func<Task<IActionResult>> Run)[]
        {
            ("create role", () => controller.CreateRole(organizationId, role, cancelled)),
            ("edit role permissions", () => controller.UpdateRole(organizationId, roleId, role, cancelled)),
            ("delete role", () => controller.DeleteRole(organizationId, roleId, cancelled)),
            ("assign role", () => controller.AssignRole(organizationId, roleId, memberId, cancelled)),
            ("remove role", () => controller.RemoveRole(organizationId, roleId, memberId, cancelled)),
            ("promote owner", () => controller.PromoteOwner(organizationId, memberId, cancelled)),
            ("rename organization", () => controller.Update(organizationId, new("Renamed", null), cancelled)),
        };
        foreach (var change in changes)
        {
            success = true;
            events.Clear();
            await change.Run();
            if (events.Count != 1 || events[0] != ($"Group:{WorkshopHub.GetGroupName(organizationId)}", "OrganizationMembersChanged", organizationId.ToString()))
                throw new Exception($"Missing change notification: {change.Name}");
            success = false;
            events.Clear();
            await change.Run();
            if (events.Count != 0) throw new Exception($"Failed {change.Name} was broadcast");
            Console.WriteLine($"PASS {change.Name} broadcasts only after success");
        }
        success = true;
        events.Clear();
        await controller.Delete(organizationId, cancelled);
        if (events.Single() != ($"Group:{WorkshopHub.GetGroupName(organizationId)}", "OrganizationAccessRevoked", organizationId.ToString()))
            throw new Exception("Organization deletion did not revoke every open tab");
        Console.WriteLine("PASS deletion revokes organization tabs");

    }
}

public class Stub : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, Stub>();
        ((Stub)(object)proxy).Handler = handler;
        return proxy;
    }
}
