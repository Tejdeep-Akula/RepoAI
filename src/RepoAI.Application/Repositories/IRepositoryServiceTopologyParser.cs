namespace RepoAI.Application.Repositories;

/// <summary>Extracts explicit service-to-service relationships from deployment manifests.</summary>
public interface IRepositoryServiceTopologyParser
{
    bool CanParse(string repositoryPath);

    IReadOnlyList<DeclaredServiceRelationship> Parse(string repositoryPath, string content);
}
