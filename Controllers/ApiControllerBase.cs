using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PragmaticBot.Server.Contracts;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Id of the authenticated bot user. Only valid on [Authorize] actions.</summary>
    protected int UserId =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    protected string? SessionId => User.FindFirstValue(TokenService.SessionIdClaim);

    /// <summary>The client parses error bodies as {"message": "..."} — always reply in that shape.</summary>
    protected ObjectResult Fail(int status, string message) =>
        StatusCode(status, new ApiErrorResponse(message));
}
