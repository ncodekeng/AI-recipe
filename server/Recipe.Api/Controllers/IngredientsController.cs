using Microsoft.AspNetCore.Mvc;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/ingredients")]
public sealed class IngredientsController(IRecipeAiService recipeAi) : ControllerBase
{
    private const int MaxPhotoCount = 6;
    private const long MaxPhotoBytes = 8 * 1024 * 1024;

    [HttpPost("analyze")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<ActionResult<IngredientAnalysisResponse>> Analyze(
        [FromForm] List<IFormFile> photos,
        CancellationToken cancellationToken)
    {
        if (photos.Count is < 1 or > MaxPhotoCount)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Choose between 1 and 6 photos.",
                Detail = "A few clear, well-lit photos give the best result."
            });
        }

        if (photos.Any(photo =>
                photo.Length == 0 ||
                photo.Length > MaxPhotoBytes ||
                !photo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "One or more photos are not supported.",
                Detail = "Use image files no larger than 8 MB each."
            });
        }

        var uploadedPhotos = new List<UploadedPhoto>(photos.Count);
        foreach (var photo in photos)
        {
            await using var stream = new MemoryStream();
            await photo.CopyToAsync(stream, cancellationToken);
            uploadedPhotos.Add(new UploadedPhoto(
                Path.GetFileName(photo.FileName),
                photo.ContentType,
                stream.ToArray()));
        }

        return Ok(await recipeAi.AnalyzeIngredientsAsync(uploadedPhotos, cancellationToken));
    }
}
