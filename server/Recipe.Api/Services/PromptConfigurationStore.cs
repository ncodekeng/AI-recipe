using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;

namespace Recipe.Api.Services;

public static class AiPromptDefaults
{
    public const string IngredientRecognition = """
        Identify all visible edible ingredients in the supplied fridge, freezer, cupboard, or worktop photos.
        Use specific, common food names and estimate a practical quantity for each item.
        Classify packaged prepared food that visibly appears frozen as a Frozen meal.
        Do not guess hidden foods. Ignore images without identifiable food and combine obvious duplicates.
        """;

    public const string RecipeRecommendation = """
        Recommend common, practical recipes that use the largest number of the user's available ingredients.
        Put the best established traditional recipe requiring 1 to 5 missing non-staple ingredients first.
        Put the best recipe requiring no missing non-staple ingredients second when one exists.
        Randomize the remaining results, while keeping them practical and avoiding recipes that require many purchases.
        Search enough distinct publishers to provide up to 6 results rather than stopping after the first match.
        Keep any permitted wine pairing short and relevant to the selected dish.
        """;
}

public sealed record AiPromptSnapshot(
    string IngredientRecognitionPrompt,
    string RecipeRecommendationPrompt,
    bool UsingDefaults,
    DateTimeOffset? UpdatedAtUtc,
    string Revision);

public interface IAiPromptProvider
{
    AiPromptSnapshot Current { get; }
}

public sealed class PromptConfigurationStore : IAiPromptProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _storagePath;
    private readonly int _maxPromptLength;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PromptConfigurationStore> _logger;
    private AiPromptSnapshot _current;

    public PromptConfigurationStore(
        IOptions<PromptAdminOptions> options,
        IHostEnvironment environment,
        TimeProvider timeProvider,
        ILogger<PromptConfigurationStore> logger)
    {
        var configuredPath = string.IsNullOrWhiteSpace(options.Value.StoragePath)
            ? "App_Data/prompt-settings.json"
            : options.Value.StoragePath.Trim();
        _storagePath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
        _maxPromptLength = Math.Clamp(options.Value.MaxPromptLength, 500, 20000);
        _timeProvider = timeProvider;
        _logger = logger;
        _current = LoadOrDefault();
    }

    public AiPromptSnapshot Current => Volatile.Read(ref _current);

    public async Task<AiPromptSnapshot> UpdateAsync(
        string ingredientRecognitionPrompt,
        string recipeRecommendationPrompt,
        CancellationToken cancellationToken)
    {
        var snapshot = CreateSnapshot(
            NormalizePrompt(ingredientRecognitionPrompt, "Ingredient recognition prompt"),
            NormalizePrompt(recipeRecommendationPrompt, "Recipe recommendation prompt"),
            _timeProvider.GetUtcNow());

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await PersistAsync(snapshot, cancellationToken);
            Volatile.Write(ref _current, snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AiPromptSnapshot> ResetAsync(CancellationToken cancellationToken) =>
        UpdateAsync(
            AiPromptDefaults.IngredientRecognition,
            AiPromptDefaults.RecipeRecommendation,
            cancellationToken);

    public static AiPromptSettingsResponse ToResponse(AiPromptSnapshot snapshot, int maxPromptLength) => new(
        snapshot.IngredientRecognitionPrompt,
        snapshot.RecipeRecommendationPrompt,
        snapshot.UsingDefaults,
        snapshot.UpdatedAtUtc,
        maxPromptLength);

    private AiPromptSnapshot LoadOrDefault()
    {
        if (!File.Exists(_storagePath))
        {
            return CreateSnapshot(
                AiPromptDefaults.IngredientRecognition,
                AiPromptDefaults.RecipeRecommendation,
                null);
        }

        try
        {
            var document = JsonSerializer.Deserialize<PersistedPrompts>(
                File.ReadAllText(_storagePath),
                JsonOptions);
            if (document is null)
            {
                throw new JsonException("The prompt settings file is incomplete.");
            }

            return CreateSnapshot(
                NormalizePrompt(document.IngredientRecognitionPrompt, "Ingredient recognition prompt"),
                NormalizePrompt(document.RecipeRecommendationPrompt, "Recipe recommendation prompt"),
                document.UpdatedAtUtc);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            _logger.LogWarning(exception, "Prompt settings could not be loaded; built-in defaults will be used.");
            return CreateSnapshot(
                AiPromptDefaults.IngredientRecognition,
                AiPromptDefaults.RecipeRecommendation,
                null);
        }
    }

    private async Task PersistAsync(AiPromptSnapshot snapshot, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storagePath)
            ?? throw new IOException("The prompt storage path has no parent directory.");
        Directory.CreateDirectory(directory);
        var document = new PersistedPrompts(
            snapshot.IngredientRecognitionPrompt,
            snapshot.RecipeRecommendationPrompt,
            snapshot.UpdatedAtUtc);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_storagePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, Encoding.UTF8, cancellationToken);
            File.Move(temporaryPath, _storagePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static AiPromptSnapshot CreateSnapshot(
        string ingredientRecognitionPrompt,
        string recipeRecommendationPrompt,
        DateTimeOffset? updatedAtUtc)
    {
        var usingDefaults =
            ingredientRecognitionPrompt.Equals(AiPromptDefaults.IngredientRecognition, StringComparison.Ordinal) &&
            recipeRecommendationPrompt.Equals(AiPromptDefaults.RecipeRecommendation, StringComparison.Ordinal);
        var revisionBytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{ingredientRecognitionPrompt}\0{recipeRecommendationPrompt}"));
        return new AiPromptSnapshot(
            ingredientRecognitionPrompt,
            recipeRecommendationPrompt,
            usingDefaults,
            updatedAtUtc,
            Convert.ToHexString(revisionBytes));
    }

    private string NormalizePrompt(string value, string label)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length < 20)
        {
            throw new ArgumentException($"{label} must contain at least 20 characters.", nameof(value));
        }
        if (trimmed.Length > _maxPromptLength)
        {
            throw new ArgumentException($"{label} cannot exceed {_maxPromptLength} characters.", nameof(value));
        }

        return trimmed;
    }

    private sealed record PersistedPrompts(
        string IngredientRecognitionPrompt,
        string RecipeRecommendationPrompt,
        DateTimeOffset? UpdatedAtUtc);
}
