using System.ComponentModel.DataAnnotations;
using Application.Interfaces;
using Domain.Exceptions;
using Domain.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.Controllers;

[ApiController]
[AllowAnonymous]
[Route("auth")]
public sealed class AuthController(IUserService userService, IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegistrationRequest request, CancellationToken cancellationToken)
    {
        if (request.Role == UserRole.Admin && !environment.IsDevelopment())
            throw new ForbiddenOperationException();

        await userService.RegisterAsync(request.Login, request.Password, request.Role, cancellationToken);
        return NoContent();
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TokenResponse>> Login(Credentials credentials, CancellationToken cancellationToken)
    {
        var token = await userService.LoginAsync(credentials.Login, credentials.Password, cancellationToken);
        return new TokenResponse(token);
    }

    public sealed record Credentials([Required] string Login, [Required] string Password);
    public sealed record RegistrationRequest(
        [Required] string Login, [Required] string Password, UserRole Role = UserRole.User);
    public sealed record TokenResponse(string Token);
}
