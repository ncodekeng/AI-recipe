namespace Recipe.Api.Options;

public sealed class FoodAiOptions
{
    public const string SectionName = "FoodAi";

    public string Provider { get; init; } = "Demo";
    public bool UseDemoFallback { get; init; } = true;
    public AzureOpenAiOptions AzureOpenAI { get; init; } = new();
}

public sealed class AzureOpenAiOptions
{
    public string Endpoint { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string Deployment { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) &&
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Deployment);
}
