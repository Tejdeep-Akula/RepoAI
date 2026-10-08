using RepoAI.Domain.Entities;

namespace RepoAI.Application.Authentication;

public interface IAccessTokenIssuer
{
    IssuedAccessToken Issue(User user);
}
