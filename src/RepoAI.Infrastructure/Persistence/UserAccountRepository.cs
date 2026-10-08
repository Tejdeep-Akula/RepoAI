using Microsoft.EntityFrameworkCore;
using Npgsql;
using RepoAI.Application.Authentication;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class UserAccountRepository(RepoAIDbContext dbContext) : IUserAccountRepository
{
    public async Task<User> FindOrCreateFromGoogleAsync(
        GoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        var existingUser = await dbContext.Users.SingleOrDefaultAsync(
            user => user.GoogleSubject == identity.Subject,
            cancellationToken);

        if (existingUser is not null)
        {
            UpdateProfile(existingUser, identity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return existingUser;
        }

        var newUser = new User
        {
            GoogleSubject = identity.Subject,
            DisplayName = Limit(identity.DisplayName, 200),
            Email = identity.Email is null ? null : Limit(identity.Email, 320)
        };

        dbContext.Users.Add(newUser);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return newUser;
        }
        catch (DbUpdateException exception) when (IsGoogleSubjectUniquenessConflict(exception))
        {
            dbContext.Entry(newUser).State = EntityState.Detached;

            var concurrentlyCreatedUser = await dbContext.Users.SingleOrDefaultAsync(
                user => user.GoogleSubject == identity.Subject,
                cancellationToken);

            if (concurrentlyCreatedUser is null)
            {
                throw;
            }

            UpdateProfile(concurrentlyCreatedUser, identity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return concurrentlyCreatedUser;
        }
    }

    private static bool IsGoogleSubjectUniquenessConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_users_GoogleSubject"
        };

    private static void UpdateProfile(User user, GoogleIdentity identity)
    {
        user.DisplayName = Limit(identity.DisplayName, 200);
        user.Email = identity.Email is null ? null : Limit(identity.Email, 320);
    }

    private static string Limit(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
