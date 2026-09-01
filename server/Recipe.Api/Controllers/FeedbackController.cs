using Microsoft.AspNetCore.Mvc;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/feedback")]
public sealed class FeedbackController(FeedbackService feedback) : ControllerBase
{
    [HttpPost]
    public ActionResult<FeedbackResponse> Submit([FromBody] FeedbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message) && request.Rating == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Add a rating or comment.",
                Detail = "Tell us briefly what worked or what should improve."
            });
        }

        if (!feedback.TrySubmit(ClientIdentity.Resolve(HttpContext), request))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Feedback was already sent.",
                Detail = "Please wait a minute before sending another response."
            });
        }

        return Accepted(new FeedbackResponse("received"));
    }
}
