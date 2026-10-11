namespace RepoAI.Application.Repositories;

/// <summary>Parses declared dependency records from a supported repository manifest.</summary>
public interface IRepositoryManifestDependencyParser
{
    bool CanParse(string repositoryPath);

    IReadOnlyList<DeclaredRepositoryDependency> Parse(string repositoryPath, string content);
}
