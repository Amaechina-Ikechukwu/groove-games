using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    [HttpPost("login")]
    public ActionResult<AuthResponse> Login([FromBody] LoginRequest request)
    {
        try
        {
            var response = _authService.Login(request.Email, request.Password);
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpGet("me")]
    public ActionResult GetCurrentUser()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            var role = User.FindFirst("role")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "Guest";
            var name = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? email;
            return Ok(new { isAuthenticated = true, email, role, name });
        }
        return Ok(new { isAuthenticated = false, role = "Guest" });
    }

    [HttpGet("users")]
    public ActionResult<List<UserDto>> GetUsers()
    {
        // Allow in admin view
        return Ok(_authService.GetAllUsers());
    }

    [HttpPost("assign-role")]
    public ActionResult AssignRole([FromBody] RoleAssignRequest request)
    {
        var success = _authService.AssignRole(request.Email, request.Role);
        if (success)
        {
            return Ok(new { message = $"Role '{request.Role}' assigned to {request.Email}." });
        }
        return NotFound(new { message = "User not found." });
    }
}
