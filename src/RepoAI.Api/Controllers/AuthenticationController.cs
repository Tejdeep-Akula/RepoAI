using System.Text;
using Microsoft.AspNetCore.Mvc;
using RepoAI.Api.Contracts;
using RepoAI.Application.Authentication;

namespace RepoAI.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Tags("Authentication")]
public sealed class AuthenticationController(
    GoogleSignInService googleSignInService,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("google")]
    [EndpointSummary("Sign in with Google")]
    [EndpointDescription(
        "Verifies a Google ID token, finds or creates the local user, and returns a short-lived RepoAI bearer token.")]
    [ProducesResponseType<GoogleSignInResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GoogleSignInResponse>> SignInWithGoogle(
        GoogleSignInRequest request,
        CancellationToken cancellationToken)
    {
        var googleClientId = configuration["Authentication:Google:ClientId"];
        var jwtSigningKey = configuration["Authentication:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(googleClientId)
            || string.IsNullOrWhiteSpace(jwtSigningKey)
            || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Google sign-in is not configured.",
                detail: "Configure a Google client ID and a JWT signing key before signing in.");
        }

        var result = await googleSignInService.SignInAsync(request.IdToken, cancellationToken);
        if (result is null)
        {
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "The Google ID token is invalid or expired.");
        }

        return Ok(new GoogleSignInResponse(
            result.UserId,
            result.DisplayName,
            result.Email,
            result.AccessToken.Value,
            "Bearer",
            result.AccessToken.ExpiresAtUtc));
    }
}
