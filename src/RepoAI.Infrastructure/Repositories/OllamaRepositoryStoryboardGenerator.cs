using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Application.Storyboards;

namespace RepoAI.Infrastructure.Repositories;

public sealed class OllamaRepositoryStoryboardGenerator(
    IChatClient chatClient,
    IOptions<OllamaChatOptions> options) : IRepositoryStoryboardGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public string ModelId => options.Value.Model;

    public async Task<RepositoryStoryboard> GenerateAsync(
        StoryboardGenerationInput input,
        CancellationToken cancellationToken)
    {
        var evidenceById = input.Evidence.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var evidenceJson = JsonSerializer.Serialize(input.Evidence, JsonOptions);
        var architectureJson = JsonSerializer.Serialize(new
        {
            input.Architecture.Summary,
            Languages = input.Architecture.LanguagesAndFormats,
            Components = input.Architecture.Components,
            EntryPoints = input.Architecture.LikelyEntryPoints,
            BuildFiles = input.Architecture.BuildAndDependencyFiles,
            Findings = input.Architecture.Findings,
            Graph = input.Architecture.Graph
        }, JsonOptions);
        var prompt = $$"""
            Create a concise, technically accurate video storyboard for this public repository.
            Treat the architecture report as the only source of factual claims. Do not invent runtime behavior.
            Distinguish observed facts from inferences in narration. Use 4 to 7 scenes in a useful walkthrough order.
            Every scene must cite one or more evidence IDs from the supplied whitelist. Never create citations.
            Respond with JSON only, matching this schema exactly:
            {"title":"...","summary":"...","scenes":[{"title":"...","purpose":"...","narration":"...","visualInstructions":"...","estimatedDurationSeconds":12,"evidenceIds":["E1"]}]}
            Keep each scene narration to 1-3 sentences and visual directions suitable for animated diagrams.
            Repository: {{input.RepositoryUrl}}
            Commit: {{input.CommitSha}} (ref {{input.ResolvedRef}})
            Architecture report: {{architectureJson}}
            Allowed evidence: {{evidenceJson}}
            """;
        var answer = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions { ModelId = ModelId, Temperature = 0.15f, MaxOutputTokens = 4096 },
            cancellationToken);
        var json = answer.Text.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            json = json.Trim('`').Trim();
            if (json.StartsWith("json", StringComparison.OrdinalIgnoreCase)) json = json[4..].Trim();
        }

        GeneratedStoryboard? generated;
        try
        {
            generated = JsonSerializer.Deserialize<GeneratedStoryboard>(json, JsonOptions);
        }
        catch (JsonException)
        {
            throw new BackgroundJobFailureException("storyboard_invalid_model_response", "The model returned an invalid storyboard. Retry generation.");
        }

        if (generated is null || string.IsNullOrWhiteSpace(generated.Title)
            || string.IsNullOrWhiteSpace(generated.Summary) || generated.Scenes is not { Count: >= 3 and <= 8 })
        {
            throw new BackgroundJobFailureException("storyboard_incomplete_model_response", "The model returned an incomplete storyboard. Retry generation.");
        }

        var scenes = new List<RepositoryStoryboardScene>(generated.Scenes.Count);
        for (var index = 0; index < generated.Scenes.Count; index++)
        {
            var scene = generated.Scenes[index];
            if (string.IsNullOrWhiteSpace(scene.Title) || string.IsNullOrWhiteSpace(scene.Narration)
                || string.IsNullOrWhiteSpace(scene.Purpose) || string.IsNullOrWhiteSpace(scene.VisualInstructions)
                || scene.EvidenceIds is not { Count: > 0 })
            {
                throw new BackgroundJobFailureException("storyboard_incomplete_scene", "The model returned a scene without required text or evidence.");
            }

            var references = new List<RepositoryArchitectureSourceReference>();
            foreach (var id in scene.EvidenceIds.Distinct(StringComparer.Ordinal))
            {
                if (!evidenceById.TryGetValue(id, out var source))
                    throw new BackgroundJobFailureException("storyboard_unknown_evidence", "The model cited evidence that is not part of this repository analysis.");
                references.Add(new RepositoryArchitectureSourceReference(source.Path, source.StartLine, source.EndLine));
            }

            scenes.Add(new RepositoryStoryboardScene(index + 1, scene.Title, scene.Purpose, scene.Narration,
                scene.VisualInstructions, Math.Clamp(scene.EstimatedDurationSeconds, 5, 45), references));
        }

        return new RepositoryStoryboard(generated.Title.Trim(), generated.Summary.Trim(),
            string.Join("\n\n", scenes.Select(scene => scene.Narration)), scenes);
    }

    private sealed record GeneratedStoryboard(string Title, string Summary, IReadOnlyList<GeneratedScene> Scenes);
    private sealed record GeneratedScene(string Title, string Purpose, string Narration,
        string VisualInstructions, int EstimatedDurationSeconds, IReadOnlyList<string> EvidenceIds);
}
