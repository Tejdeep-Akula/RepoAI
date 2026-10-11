namespace RepoAI.Application.Chat;

public interface IRepositoryEvidenceRetriever
{
    Task<IReadOnlyList<RepositoryEvidenceChunk>> RetrieveAsync(
        Guid snapshotId,
        string question,
        CancellationToken cancellationToken);
}
