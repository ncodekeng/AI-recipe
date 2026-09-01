namespace Recipe.Api.Models;

public sealed record UploadedPhoto(string FileName, string ContentType, byte[] Content);

public sealed record DetectedIngredient(
    Guid Id,
    string Name,
    string Quantity,
    int Confidence,
    string SourceImage,
    string Kind = "Ingredient");

public sealed record IngredientAnalysisResponse(
    IReadOnlyList<DetectedIngredient> Ingredients,
    string Provider,
    string? Notice = null,
    IReadOnlyList<string>? IgnoredPhotos = null);

public sealed record IngredientInput(string Name, string Quantity);
