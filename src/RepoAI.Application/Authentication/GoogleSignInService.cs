namespace RepoAI.Application.Authentication;

public sealed class GoogleSignInService(
    IGoogleIdTokenValidator googleIdTokenValidator,
    IUserAccountRepository userAccountRepository,
    IAccessTokenIssuer accessTokenIssuer)
{
    public async Task<GoogleSignInResult?> SignInAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        var identity = await googleIdTokenValidator.ValidateAsync(idToken, cancellationToken);
        if (identity is null)
        {
            return null;
        }

        var user = await userAccountRepository.FindOrCreateFromGoogleAsync(
            identity,
            cancellationToken);
        var accessToken = accessTokenIssuer.Issue(user);

        return new GoogleSignInResult(
            user.Id,
            user.DisplayName,
            user.Email,
            accessToken);
    }
}
