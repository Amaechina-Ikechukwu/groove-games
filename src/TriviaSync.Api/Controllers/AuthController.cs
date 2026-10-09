using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TriviaSync.Api.Models;
using TriviaSync.Api.Services;

namespace TriviaSync.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public ActionResult<AuthResponse> Login([FromBody] LoginRequest request)
    {
        try
        {
            return Ok(_authService.Login(request.Email, request.Password));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public ActionResult<AuthResponse> Register([FromBody] RegisterRequest request)
    {
        try
        {
            return Ok(_authService.Register(request));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    [HttpGet("me")]
    public ActionResult GetCurrentUser()
    {
        var email = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = email == null ? null : _authService.FindUser(email);
        if (user == null)
        {
            return Ok(new { isAuthenticated = false, role = "Guest" });
        }
        return Ok(new { isAuthenticated = true, email = user.Email, role = user.Role, name = user.DisplayName });
    }

    /// <summary>Lets a signed-in player account explicitly opt in to hosting games.</summary>
    [Authorize]
    [HttpPost("become-host")]
    public ActionResult<AuthResponse> BecomeHost()
    {
        var email = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (email == null)
        {
            return Unauthorized();
        }
        return Ok(_authService.BecomeHost(email));
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpGet("users")]
    public ActionResult<List<UserDto>> GetUsers()
    {
        return Ok(_authService.GetAllUsers());
    }

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("assign-role")]
    public ActionResult AssignRole([FromBody] RoleAssignRequest request)
    {
        return _authService.AssignRole(request.Email, request.Role) switch
        {
            RoleChangeResult.Ok => Ok(new { message = $"{request.Email} is now {request.Role}." }),
            RoleChangeResult.NotFound => NotFound(new { message = "No account exists with that email. They need to sign up first." }),
            RoleChangeResult.InvalidRole => BadRequest(new { message = "Role must be Player, Host, or Admin." }),
            RoleChangeResult.WouldRemoveLastAdmin => Conflict(new { message = "You can't remove the last admin. Promote someone else first." }),
            _ => StatusCode(500)
        };
    }
}
