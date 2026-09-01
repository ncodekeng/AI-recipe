using Microsoft.AspNetCore.Mvc;
using Recipe.Api.Models;
using Recipe.Api.Services;

namespace Recipe.Api.Controllers;

[ApiController]
[Route("api/usage")]
public sealed class UsageController(AiUsageGuard usageGuard) : ControllerBase
{
    [HttpGet]
    public ActionResult<UsageStatusResponse> Get() =>
        Ok(usageGuard.GetStatus(ClientIdentity.Resolve(HttpContext)));
}
