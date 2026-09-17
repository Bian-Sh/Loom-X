namespace LoomX.Assistant.UserDecisions;

internal static class UserDecisionBrokerTestExtensions
{
    private const string ClaimantId = "loomx-test-ui";

    public static bool Submit(
        this IUserDecisionBroker broker,
        string requestId,
        IReadOnlyDictionary<string, object?> values)
    {
        broker.TryClaim(requestId, ClaimantId);
        return broker.Submit(requestId, ClaimantId, values);
    }

    public static bool Cancel(this IUserDecisionBroker broker, string requestId, string reason)
    {
        broker.TryClaim(requestId, ClaimantId);
        return broker.Cancel(requestId, ClaimantId, reason);
    }
}
