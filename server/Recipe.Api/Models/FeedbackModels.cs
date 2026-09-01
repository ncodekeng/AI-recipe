using System.ComponentModel.DataAnnotations;

namespace Recipe.Api.Models;

public sealed class FeedbackRequest
{
    [Range(1, 5)]
    public int Rating { get; init; }

    [MaxLength(800)]
    public string Message { get; init; } = string.Empty;
}

public sealed record FeedbackResponse(string Status);
