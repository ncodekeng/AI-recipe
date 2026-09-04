using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Recipe.Api.Models;
using Recipe.Api.Options;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/admin/prompts")]
public sealed class PromptAdminController(
    PromptConfigurationStore prompts,
    IOptions<PromptAdminOptions> options,
    AdminSessionService adminSessions,
    ILogger<PromptAdminController> logger) : ControllerBase
{
    private const string AdminKeyHeader = "X-Plate-Admin-Key";
    private readonly PromptAdminOptions _options = options.Value;

    [HttpGet]
    public ActionResult<AiPromptSettingsResponse> Get()
    {
        if (AuthorizeRequest() is { } failure)
        {
            return failure;
        }

        return Ok(ToResponse(prompts.Current));
    }

    [HttpPut]
    [RequestSizeLimit(50000)]
    public async Task<ActionResult<AiPromptSettingsResponse>> Update(
        [FromBody] UpdateAiPromptsRequest request,
        CancellationToken cancellationToken)
    {
        if (AuthorizeRequest() is { } failure)
        {
            return failure;
        }

        if (ValidatePrompt(request.IngredientRecognitionPrompt, "Ingredient recognition prompt") is { } scanError)
        {
            return BadRequestProblem(scanError);
        }
        if (ValidatePrompt(request.RecipeRecommendationPrompt, "Recipe recommendation prompt") is { } recipeError)
        {
            return BadRequestProblem(recipeError);
        }

        try
        {
            var updated = await prompts.UpdateAsync(
                request.IngredientRecognitionPrompt,
                request.RecipeRecommendationPrompt,
                cancellationToken);
            return Ok(ToResponse(updated));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Prompt settings could not be persisted.");
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Prompt settings could not be saved.",
                detail: "Check that PromptAdmin:StoragePath points to a writable persistent directory.");
        }
    }

    [HttpPost("reset")]
    public async Task<ActionResult<AiPromptSettingsResponse>> Reset(CancellationToken cancellationToken)
    {
        if (AuthorizeRequest() is { } failure)
        {
            return failure;
        }

        try
        {
            return Ok(ToResponse(await prompts.ResetAsync(cancellationToken)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Default prompt settings could not be persisted.");
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Default prompts could not be restored.",
                detail: "Check that PromptAdmin:StoragePath points to a writable persistent directory.");
        }
    }

    private ActionResult? AuthorizeRequest()
    {
        if (!adminSessions.IsConfigured)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Prompt administration is not configured.",
                Detail = "Enable PromptAdmin and configure a secret admin key on the API."
            });
        }

        if (adminSessions.IsAuthenticated(HttpContext))
        {
            return null;
        }

        var supplied = Request.Headers[AdminKeyHeader].FirstOrDefault() ?? string.Empty;
        return adminSessions.TryAuthenticate(HttpContext, supplied)
            ? null
            : Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Admin access denied.",
                Detail = "Enter the configured PLATE prompt-admin key."
            });
    }

    private string? ValidatePrompt(string value, string label)
    {
        var maxLength = Math.Clamp(_options.MaxPromptLength, 500, 20000);
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{label} is required.";
        }
        if (value.Trim().Length < 20)
        {
            return $"{label} must contain at least 20 characters.";
        }
        if (value.Length > maxLength)
        {
            return $"{label} cannot exceed {maxLength} characters.";
        }

        return null;
    }

    private BadRequestObjectResult BadRequestProblem(string detail) => BadRequest(new ProblemDetails
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "Prompt settings are invalid.",
        Detail = detail
    });

    private AiPromptSettingsResponse ToResponse(AiPromptSnapshot snapshot) =>
        PromptConfigurationStore.ToResponse(
            snapshot,
            Math.Clamp(_options.MaxPromptLength, 500, 20000));
}
