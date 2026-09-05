using Microsoft.AspNetCore.Mvc;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/ingredients")]
public sealed class IngredientsController(
    IRecipeAiService recipeAi,
    IngredientScanCache scanCache,
    AdminSessionService adminSessions,
    AiUsageGuard usageGuard) : ControllerBase
{
    private const int MaxPhotoCount = 50;
    private const long MaxPhotoBytes = 5 * 1024 * 1024;
    private const long MaxRequestBodyBytes = (MaxPhotoCount * MaxPhotoBytes) + (1024 * 1024);

    [HttpPost("analyze")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBodyBytes)]
    public async Task<ActionResult<IngredientAnalysisResponse>> Analyze(
        [FromForm] List<IFormFile> photos,
        CancellationToken cancellationToken)
    {
        if (photos.Count is < 1 or > MaxPhotoCount)
        {
            return BadRequest(new ProblemDetails
            {
                Title = $"Choose between 1 and {MaxPhotoCount} photos.",
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
                Detail = "Use JPEG, PNG, GIF, or WebP files no larger than 5 MB each."
            });
        }

        var uploadedPhotos = new List<UploadedPhoto>(photos.Count);
        foreach (var photo in photos)
        {
            await using var stream = new MemoryStream();
            await photo.CopyToAsync(stream, cancellationToken);
            var content = stream.ToArray();
            if (!ImageFileValidator.TryDetectContentType(content, out var detectedContentType))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "A photo's contents do not match a supported image format.",
                    Detail = "Use an original JPEG, PNG, GIF, or WebP image."
                });
            }

            uploadedPhotos.Add(new UploadedPhoto(
                Path.GetFileName(photo.FileName),
                detectedContentType,
                content));
        }

        var clientKey = ClientIdentity.Resolve(HttpContext);
        if (scanCache.TryGet(clientKey, uploadedPhotos, out var cached) && cached is not null)
        {
            var cachedStatus = usageGuard.GetStatus(
                clientKey,
                adminSessions.IsAuthenticated(HttpContext));
            Response.Headers["X-Plate-Scans-Remaining"] = cachedStatus.ScansRemaining.ToString();
            return Ok(cached with
            {
                Notice = AppendNotice(cached.Notice, "Loaded from your seven-day scan cache; Azure was not called.")
            });
        }

        var admission = usageGuard.TryAcquire(
            clientKey,
            AiOperation.IngredientScan,
            adminSessions.IsAuthenticated(HttpContext));
        if (!admission.Allowed)
        {
            return StatusCode(admission.Rejection!.StatusCode, new ProblemDetails
            {
                Status = admission.Rejection.StatusCode,
                Title = admission.Rejection.Title,
                Detail = admission.Rejection.Detail
            });
        }

        using var usageLease = admission.Lease!;
        Response.Headers["X-Plate-Scans-Remaining"] = admission.Status.ScansRemaining.ToString();

        var result = await recipeAi.AnalyzeIngredientsAsync(uploadedPhotos, cancellationToken);
        scanCache.Store(clientKey, uploadedPhotos, result);
        return Ok(result);
    }

    private static string AppendNotice(string? current, string message) =>
        string.IsNullOrWhiteSpace(current) ? message : $"{current} {message}";
}
