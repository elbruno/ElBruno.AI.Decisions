namespace ElBruno.AI.Decisions.Foundry;

/// <summary>The decision API contract used by a Foundry deployment.</summary>
public enum FoundryDecisionApi
{
    /// <summary>Microsoft-Decision-1's SystemOne API at /providers/microsoft/v1/systemone.</summary>
    SystemOne,

    /// <summary>The Azure OpenAI Decisions API at /openai/v1/decisions.</summary>
    OpenAIDecisions
}
