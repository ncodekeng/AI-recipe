namespace Recipe.Api.Models;

public sealed record ServiceStatusResponse(string Status, string AiProvider, bool AzureConfigured);
