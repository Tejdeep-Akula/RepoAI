using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Application.Storyboards;

namespace RepoAI.Infrastructure.Repositories;

public sealed class OllamaRepositoryStoryboardGenerator(
    IChatClient chatClient,
    IOptions<OllamaChatOptions> options,
    ILogger<OllamaRepositoryStoryboardGenerator> logger) : IRepositoryStoryboardGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonDocument OutputSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string" },
            "summary": { "type": "string" },
            "scenes": {
              "type": "array",
              "minItems": 3,
              "maxItems": 8,
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "purpose": { "type": "string" },
                  "narration": { "type": "string" },
                  "visualInstructions": { "type": "string" },
                  "estimatedDurationSeconds": { "type": "integer", "minimum": 5, "maximum": 45 },
                  "evidenceIds": { "type": "array", "minItems": 1, "items": { "type": "string" } }
                },
                "required": ["title", "purpose", "narration", "visualInstructions", "estimatedDurationSeconds", "evidenceIds"],
                "additionalProperties": false
              }
            }
          },
          "required": ["title", "summary", "scenes"],
          "additionalProperties": false
        }
        """);
    private const int MaximumEvidenceItems = 80;
    public string ModelId => options.Value.Model;

    public async Task<RepositoryStoryboard> GenerateAsync(
        StoryboardGenerationInput input,
        CancellationToken cancellationToken)
    {
        var evidence = input.Evidence.Take(MaximumEvidenceItems)
            .Select(item => item with { Description = Truncate(item.Description, 320) })
            .ToArray();
        var evidenceById = evidence.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var evidenceJson = JsonSerializer.Serialize(evidence, JsonOptions);
        var architectureJson = JsonSerializer.Serialize(new
        {
            Summary = Truncate(input.Architecture.Summary, 3000),
            Languages = input.Architecture.LanguagesAndFormats.Take(20),
            Components = input.Architecture.Components.Take(30).Select(component => new
            {
                component.Name,
                component.Path,
                component.FileCount,
                Languages = component.LanguagesAndFormats.Take(10),
                SourcePaths = component.SourcePaths.Take(8)
            }),
            EntryPoints = input.Architecture.LikelyEntryPoints.Take(20),
            BuildFiles = input.Architecture.BuildAndDependencyFiles.Take(20),
            Findings = input.Architecture.Findings.Take(60).Select(finding => new
            {
                finding.Category,
                Claim = Truncate(finding.Claim, 400),
                finding.Kind,
                finding.Confidence,
                finding.Method,
                Evidence = finding.Evidence.Take(4)
            }),
            Graph = input.Architecture.Graph is { } graph ? new
            {
                Nodes = graph.Nodes.Take(80).Select(node => new
                {
                    node.Id,
                    node.Label,
                    node.Kind,
                    Detail = Truncate(node.Detail, 240),
                    node.Path
                }),
                Edges = graph.Edges.Take(100).Select(edge => new
                {
                    edge.SourceNodeId,
                    edge.TargetNodeId,
                    edge.RelationshipType,
                    edge.Confidence,
                    Evidence = edge.Evidence.Take(3)
                }),
                graph.IsTruncated,
                graph.OmittedEdgeCount
            } : null
        }, JsonOptions);
        var prompt = $$"""
            Create a concise, technically accurate architecture explainer storyboard for this public repository.
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
            new ChatOptions
            {
                ModelId = ModelId,
                Temperature = 0.15f,
                MaxOutputTokens = 4096,
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    OutputSchema.RootElement,
                    "repoai_storyboard",
                    "A repository explainer storyboard with evidence IDs from the supplied whitelist.")
            },
            cancellationToken);
        var json = ExtractJsonObject(answer.Text);
        if (answer.FinishReason == ChatFinishReason.Length)
            throw new BackgroundJobFailureException("storyboard_response_truncated", "The model reached its output limit before completing the storyboard. Retry generation.");
        if (json is null)
        {
            logger.LogWarning("Ollama returned no JSON object for storyboard generation (finish reason {FinishReason}, {CharacterCount} characters)",
                answer.FinishReason?.ToString() ?? "unknown", answer.Text.Length);
            throw new BackgroundJobFailureException("storyboard_invalid_model_response", "The model did not return a JSON storyboard. Retry generation.");
        }

        GeneratedStoryboard? generated;
        try
        {
            generated = JsonSerializer.Deserialize<GeneratedStoryboard>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception,
                "Ollama storyboard JSON could not be parsed (finish reason {FinishReason}, {CharacterCount} characters)",
                answer.FinishReason?.ToString() ?? "unknown", answer.Text.Length);
            throw new BackgroundJobFailureException("storyboard_invalid_model_response", "The model returned an invalid storyboard. Retry generation.");
        }

        if (generated is null || string.IsNullOrWhiteSpace(generated.Title)
            || string.IsNullOrWhiteSpace(generated.Summary) || generated.Scenes is not { Count: >= 3 and <= 8 })
        {
            logger.LogWarning(
                "Ollama storyboard JSON had an invalid top-level shape (title present: {HasTitle}, summary present: {HasSummary}, scene count: {SceneCount}, finish reason: {FinishReason})",
                !string.IsNullOrWhiteSpace(generated?.Title),
                !string.IsNullOrWhiteSpace(generated?.Summary),
                generated?.Scenes?.Count ?? 0,
                answer.FinishReason?.ToString() ?? "unknown");
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

    private static string? ExtractJsonObject(string response)
    {
        var start = response.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < response.Length; index++)
        {
            var character = response[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') inString = false;
                continue;
            }

            if (character == '"') inString = true;
            else if (character == '{') depth++;
            else if (character == '}' && --depth == 0) return response[start..(index + 1)];
        }

        return response[start..].Trim();
    }

    private static string Truncate(string? value, int maximumLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maximumLength
            ? value ?? string.Empty
            : value[..maximumLength];

    private sealed record GeneratedStoryboard(string Title, string Summary, IReadOnlyList<GeneratedScene> Scenes);
    private sealed record GeneratedScene(string Title, string Purpose, string Narration,
        string VisualInstructions, int EstimatedDurationSeconds, IReadOnlyList<string> EvidenceIds);
}
