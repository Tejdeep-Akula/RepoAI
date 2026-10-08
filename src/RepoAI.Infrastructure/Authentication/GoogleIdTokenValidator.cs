using Google.Apis.Auth;
using Microsoft.Extensions.Configuration;
using RepoAI.Application.Authentication;

namespace RepoAI.Infrastructure.Authentication;

public sealed class GoogleIdTokenValidator(IConfiguration configuration) : IGoogleIdTokenValidator
{
    public async Task<GoogleIdentity?> ValidateAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var clientId = configuration["Authentication:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Google sign-in is not configured. Set Authentication:Google:ClientId.");
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [clientId]
                });

            if (string.IsNullOrWhiteSpace(payload.Subject))
            {
                return null;
            }

            var displayName = payload.Name;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = payload.Email;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = "Google user";
            }

            return new GoogleIdentity(
                payload.Subject,
                displayName,
                string.IsNullOrWhiteSpace(payload.Email) ? null : payload.Email);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
