using DevHub.Application.Connections;

namespace DevHub.Infrastructure.Connections;

internal static class ConnectionConditionEvaluation
{
    public static async Task<ConnectionRuleCheck> Check(IConnectionTools tools, ConnectionScope scope, ConnectionRule rule, CancellationToken ct)
    {
        var checks = new List<ConnectionConditionCheck>();
        foreach (var condition in rule.Conditions)
        {
            var check = await tools.Check(scope, condition, ct);
            checks.Add(new(condition.Node, condition.Kind, check.IsSuccess ? check.Value : null,
                check.IsSuccess ? null : "The configured node or value could not be read."));
        }

        var status = "ready";
        if (checks.Count > 0)
        {
            if (rule.MatchAny)
            {
                if (checks.All(check => check.Passed != true))
                    status = checks.Any(check => check.Passed is null) ? "condition-unavailable" : "conditions-unmet";
            }
            else if (checks.Any(check => check.Passed == false)) status = "conditions-unmet";
            else if (checks.Any(check => check.Passed is null)) status = "condition-unavailable";
        }
        return new(rule.Id, status, checks);
    }
}
