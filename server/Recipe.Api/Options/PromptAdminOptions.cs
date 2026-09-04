namespace Recipe.Api.Options;

public sealed class PromptAdminOptions
{
    public const string SectionName = "PromptAdmin";

    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string StoragePath { get; init; } = "App_Data/prompt-settings.json";
    public int MaxPromptLength { get; init; } = 8000;
    public int SessionHours { get; init; } = 8;
}
