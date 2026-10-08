using RepoAI.Domain.Entities;

namespace RepoAI.Application.Authentication;

public interface IUserAccountRepository
{
    Task<User> FindOrCreateFromGoogleAsync(
        GoogleIdentity identity,
        CancellationToken cancellationToken);
}
