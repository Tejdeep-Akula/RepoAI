using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RepoAI.Application.Jobs;
using RepoAI.Application.Repositories;
using RepoAI.Domain.Entities;
using RepoAI.Infrastructure.Persistence;

namespace RepoAI.Infrastructure.Repositories;

public sealed class GitHubRepositoryImportService(
    IHttpClientFactory httpClientFactory,
    RepoAIDbContext dbContext,
    IRepositoryContentStore contentStore,
    IOptions<RepositoryImportOptions> options,
    ILogger<GitHubRepositoryImportService> logger) : IRepositoryImportService
{
    public const string HttpClientName = "RepoAI.GitHubImport";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> KnownTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".fs", ".vb", ".csx", ".fsx", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".cjs",
        ".py", ".pyi", ".java", ".kt", ".kts", ".go", ".rs", ".c", ".h", ".cc", ".cpp", ".hpp",
        ".swift", ".m", ".mm", ".php", ".rb", ".rake", ".ex", ".exs", ".erl", ".hrl", ".hs",
        ".lhs", ".scala", ".sc", ".clj", ".cljs", ".cljc", ".edn", ".dart", ".lua", ".pl", ".pm",
        ".r", ".R", ".jl", ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1", ".bat", ".cmd",
        ".html", ".htm", ".css", ".scss", ".sass", ".less", ".xml", ".xsd", ".xsl", ".json",
        ".jsonc", ".json5", ".yaml", ".yml", ".toml", ".ini", ".conf", ".config", ".properties",
        ".sql", ".graphql", ".gql", ".proto", ".tf", ".hcl", ".dockerfile", ".md", ".mdx", ".rst",
        ".txt", ".csv", ".make", ".mk", ".gradle", ".cmake", ".lock", ".editorconfig", ".gitignore"
    };

    public async Task ImportAsync(
        Guid projectId,
        RepositoryImportJobPayload payload,
        IBackgroundJobExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        var importOptions = options.Value;
        ValidateOptions(importOptions);
        if (!GitHubRepositoryReference.TryParse(payload.RepositoryUrl, out var repository)
            || repository is null)
        {
            throw new BackgroundJobFailureException(
                "unsupported_repository_url",
                "Only public github.com owner/repository URLs can be imported.");
        }

        var requestedRef = string.IsNullOrWhiteSpace(payload.RequestedRef)
            ? null
            : payload.RequestedRef.Trim();
        logger.LogInformation(
            "Starting GitHub import for {RepositoryOwner}/{RepositoryName} at ref {RequestedRef}",
            repository.Owner,
            repository.Name,
            requestedRef ?? "default branch");
        if (requestedRef?.Length > importOptions.MaxRefLength || requestedRef?.Any(char.IsControl) == true)
        {
            throw new BackgroundJobFailureException("invalid_repository_ref", "The repository ref is invalid or too long.");
        }

        var heartbeat = new JobProgressHeartbeat(executionContext, cancellationToken);
        await heartbeat.ReportAsync(3, force: true);
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var metadata = await GetJsonAsync(
            client,
            $"https://api.github.com/repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}",
            "github_repository_not_found",
            cancellationToken);
        var defaultBranch = metadata.RootElement.TryGetProperty("default_branch", out var branchElement)
            ? branchElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(defaultBranch))
        {
            throw new BackgroundJobFailureException("github_metadata_invalid", "GitHub did not return a default branch.");
        }

        var resolvedRef = requestedRef ?? defaultBranch;
        logger.LogInformation(
            "GitHub repository metadata loaded; resolving ref {ResolvedRef} for {RepositoryOwner}/{RepositoryName}",
            resolvedRef,
            repository.Owner,
            repository.Name);
        await heartbeat.ReportAsync(8, force: true);
        using var commit = await GetJsonAsync(
            client,
            $"https://api.github.com/repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/commits/{Uri.EscapeDataString(resolvedRef)}",
            "github_ref_not_found",
            cancellationToken);
        var commitSha = commit.RootElement.TryGetProperty("sha", out var shaElement)
            ? shaElement.GetString()?.ToLowerInvariant()
            : null;
        if (commitSha is not { Length: 40 } || !commitSha.All(Uri.IsHexDigit))
        {
            throw new BackgroundJobFailureException("github_commit_invalid", "GitHub returned an invalid commit SHA.");
        }

        logger.LogInformation("Resolved repository ref to commit {CommitSha}", commitSha);
        await heartbeat.ReportAsync(15, force: true);
        var snapshotId = Guid.NewGuid();
        var archivePath = Path.Combine(Path.GetTempPath(), $"repoai-import-{Guid.NewGuid():N}.zip");
        var snapshotPersisted = false;
        try
        {
            await DownloadArchiveAsync(
                client,
                repository,
                commitSha,
                archivePath,
                importOptions.MaxArchiveBytes,
                heartbeat,
                cancellationToken);
            logger.LogInformation("Downloaded GitHub archive for commit {CommitSha}", commitSha);
            await heartbeat.ReportAsync(25, force: true);

            var files = await InventoryArchiveAsync(
                archivePath,
                snapshotId,
                importOptions,
                heartbeat,
                cancellationToken);
            logger.LogInformation("Inventoried {FileCount} files from repository archive", files.Count);
            if (files.Count == 0)
            {
                throw new BackgroundJobFailureException("repository_empty", "The selected commit contains no files.");
            }

            var snapshot = new RepositorySnapshot
            {
                Id = snapshotId,
                ProjectId = projectId,
                RepositoryUrl = payload.RepositoryUrl,
                CommitSha = commitSha,
                ResolvedRef = resolvedRef,
                DefaultBranch = defaultBranch,
                FileCount = files.Count,
                TextFileCount = files.Count(file => !file.IsBinary),
                BinaryFileCount = files.Count(file => file.IsBinary),
                SkippedFileCount = files.Count(file => file.SkipReason is not null)
            };

            await heartbeat.ReportAsync(98, force: true);
            dbContext.RepositorySnapshots.Add(snapshot);
            dbContext.RepositoryFiles.AddRange(files);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                dbContext.Entry(snapshot).State = EntityState.Detached;
                foreach (var file in files)
                {
                    dbContext.Entry(file).State = EntityState.Detached;
                }

                throw;
            }

            snapshotPersisted = true;
            logger.LogInformation(
                "Saved repository snapshot {SnapshotId} with {FileCount} files ({TextFileCount} text, {BinaryFileCount} binary)",
                snapshot.Id,
                snapshot.FileCount,
                snapshot.TextFileCount,
                snapshot.BinaryFileCount);
        }
        catch (HttpRequestException exception)
        {
            throw new BackgroundJobFailureException("github_request_failed", exception.Message);
        }
        finally
        {
            if (!snapshotPersisted)
            {
                contentStore.DeleteSnapshot(snapshotId);
            }

            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
    }

    private static async Task<JsonDocument> GetJsonAsync(
        HttpClient client,
        string address,
        string notFoundCode,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var requestToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, requestToken.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new BackgroundJobFailureException("github_request_timed_out", "The GitHub API request timed out.");
        }

        using (response)
        {
        if (!response.IsSuccessStatusCode)
        {
            var code = response.StatusCode switch
            {
                HttpStatusCode.NotFound or HttpStatusCode.Gone => notFoundCode,
                HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests => "github_rate_limited",
                HttpStatusCode.Unauthorized => "github_access_denied",
                _ when (int)response.StatusCode is >= 300 and < 400 => "github_redirect_rejected",
                _ => "github_request_failed"
            };
            throw new BackgroundJobFailureException(code, $"GitHub returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(requestToken.Token);
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: requestToken.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new BackgroundJobFailureException("github_request_timed_out", "The GitHub API request timed out.");
        }
        }
    }

    private static async Task DownloadArchiveAsync(
        HttpClient client,
        GitHubRepositoryReference repository,
        string commitSha,
        string archivePath,
        long maxArchiveBytes,
        JobProgressHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        await heartbeat.ReportAsync(20, force: true);
        var address = $"https://codeload.github.com/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/zip/{commitSha}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var requestToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, requestToken.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new BackgroundJobFailureException("github_request_timed_out", "The GitHub archive request timed out.");
        }

        using (response)
        {
        if (!response.IsSuccessStatusCode)
        {
            var code = (int)response.StatusCode is >= 300 and < 400
                ? "github_redirect_rejected"
                : response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
                    ? "github_archive_not_found"
                    : "github_archive_download_failed";
            throw new BackgroundJobFailureException(code, $"GitHub archive download returned HTTP {(int)response.StatusCode}.");
        }

        if (response.Content.Headers.ContentLength > maxArchiveBytes)
        {
            throw new BackgroundJobFailureException("repository_archive_too_large", "The repository archive exceeds the configured size limit.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(requestToken.Token);
        await using var destination = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            await heartbeat.ReportIfDueAsync(20);
            int read;
            try
            {
                read = await source.ReadAsync(buffer, requestToken.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new BackgroundJobFailureException("github_request_timed_out", "The GitHub archive request timed out.");
            }
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > maxArchiveBytes)
            {
                throw new BackgroundJobFailureException("repository_archive_too_large", "The repository archive exceeds the configured size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        }
    }

    private async Task<List<RepositoryFile>> InventoryArchiveAsync(
        string archivePath,
        Guid snapshotId,
        RepositoryImportOptions importOptions,
        JobProgressHeartbeat heartbeat,
        CancellationToken cancellationToken)
    {
        await using var archiveStream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
        var files = new List<RepositoryFile>(Math.Min(archive.Entries.Count, importOptions.MaxFiles));
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var rootPrefix = archive.Entries
            .Select(entry => entry.FullName.Split('/')[0])
            .FirstOrDefault(prefix => prefix.Length > 0);
        long declaredExpandedBytes = 0;
        for (var index = 0; index < archive.Entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.Entries[index];
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (files.Count >= importOptions.MaxFiles)
            {
                throw new BackgroundJobFailureException("repository_file_count_limit", "The repository exceeds the configured file-count limit.");
            }

            var relativePath = RepositoryArchivePath.NormalizeGitHubEntry(entry.FullName, rootPrefix);
            if (relativePath is null)
            {
                continue;
            }

            if (relativePath.Length > 4096 || !paths.Add(relativePath))
            {
                throw new BackgroundJobFailureException("repository_archive_invalid", "The archive contains a duplicate or overlong file path.");
            }

            declaredExpandedBytes += entry.Length;
            if (declaredExpandedBytes > importOptions.MaxExpandedBytes)
            {
                throw new BackgroundJobFailureException("repository_expanded_size_limit", "The repository exceeds the configured expanded-size limit.");
            }

            var isSymbolicLink = IsSymbolicLink(entry);
            var progress = 25 + (int)(60d * (index + 1) / Math.Max(1, archive.Entries.Count));
            var fileData = await ReadEntryAsync(
                entry,
                importOptions.MaxTextFileBytes,
                () => heartbeat.ReportIfDueAsync(progress),
                cancellationToken);
            var file = new RepositoryFile
            {
                SnapshotId = snapshotId,
                RelativePath = relativePath,
                DetectedLanguage = DetectLanguage(relativePath),
                SizeBytes = fileData.Size,
                Sha256 = fileData.Sha256,
                IsBinary = fileData.IsBinary,
                SkipReason = isSymbolicLink
                    ? "symbolic_link_not_followed"
                    : fileData.Size > importOptions.MaxTextFileBytes
                        ? "file_size_limit"
                        : null
            };

            if (file.SkipReason is null && !file.IsBinary && fileData.Content is not null)
            {
                file = new RepositoryFile
                {
                    SnapshotId = file.SnapshotId,
                    RelativePath = file.RelativePath,
                    DetectedLanguage = file.DetectedLanguage,
                    SizeBytes = file.SizeBytes,
                    Sha256 = file.Sha256,
                    IsBinary = file.IsBinary,
                    ContentStorageKey = await contentStore.SaveTextFileAsync(
                        snapshotId,
                        fileData.Sha256,
                        fileData.Content,
                        cancellationToken)
                };
            }

            files.Add(file);
            await heartbeat.ReportIfDueAsync(progress);
        }

        await heartbeat.ReportAsync(92, force: true);
        return files;
    }

    private static async Task<EntryData> ReadEntryAsync(
        ZipArchiveEntry entry,
        long maxTextFileBytes,
        Func<Task> heartbeat,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = entry.Open();
        using var content = entry.Length <= maxTextFileBytes
            ? new MemoryStream((int)Math.Min(entry.Length, int.MaxValue))
            : null;
        var sample = new byte[8192];
        var sampleLength = 0;
        var buffer = new byte[81920];
        long size = 0;
        while (true)
        {
            await heartbeat();
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            size += read;
            if (size > entry.Length)
            {
                throw new BackgroundJobFailureException("repository_archive_invalid", "An archive entry exceeded its declared size.");
            }

            hash.AppendData(buffer, 0, read);
            if (sampleLength < sample.Length)
            {
                var sampleBytes = Math.Min(read, sample.Length - sampleLength);
                buffer.AsSpan(0, sampleBytes).CopyTo(sample.AsSpan(sampleLength));
                sampleLength += sampleBytes;
            }

            if (content is not null)
            {
                await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        if (size != entry.Length)
        {
            throw new BackgroundJobFailureException("repository_archive_invalid", "An archive entry did not match its declared size.");
        }

        var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        var isBinary = LooksBinary(sample.AsSpan(0, sampleLength));
        return new EntryData(size, sha256, isBinary, content?.ToArray());
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        if (sample.Contains((byte)0))
        {
            return true;
        }

        if (sample.IsEmpty)
        {
            return false;
        }

        var controlCount = 0;
        foreach (var value in sample)
        {
            if (value < 0x20 && value != (byte)'\t' && value != (byte)'\n'
                && value != (byte)'\r' && value != (byte)'\f')
            {
                controlCount++;
            }
        }

        return controlCount > sample.Length / 100;
    }

    private static string DetectLanguage(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        var lowerName = fileName.ToLowerInvariant();
        if (lowerName is "dockerfile" or "makefile" or "justfile" or "cmakelists.txt")
        {
            return lowerName switch
            {
                "dockerfile" => "Dockerfile",
                "makefile" or "justfile" => "Makefile",
                _ => "CMake"
            };
        }

        var extension = Path.GetExtension(fileName);
        var language = extension.ToLowerInvariant() switch
        {
            ".cs" or ".csx" => "C#",
            ".fs" or ".fsx" => "F#",
            ".vb" => "Visual Basic",
            ".ts" or ".tsx" => "TypeScript",
            ".js" or ".jsx" or ".mjs" or ".cjs" => "JavaScript",
            ".py" or ".pyi" => "Python",
            ".java" => "Java",
            ".kt" or ".kts" => "Kotlin",
            ".go" => "Go",
            ".rs" => "Rust",
            ".c" or ".h" => "C",
            ".cc" or ".cpp" or ".hpp" => "C++",
            ".swift" => "Swift",
            ".php" => "PHP",
            ".rb" or ".rake" => "Ruby",
            ".ex" or ".exs" => "Elixir",
            ".erl" or ".hrl" => "Erlang",
            ".hs" or ".lhs" => "Haskell",
            ".scala" or ".sc" => "Scala",
            ".clj" or ".cljs" or ".cljc" => "Clojure",
            ".dart" => "Dart",
            ".lua" => "Lua",
            ".pl" or ".pm" => "Perl",
            ".r" => "R",
            ".jl" => "Julia",
            ".sh" or ".bash" or ".zsh" or ".fish" => "Shell",
            ".ps1" or ".psm1" => "PowerShell",
            ".sql" => "SQL",
            ".html" or ".htm" => "HTML",
            ".css" or ".scss" or ".sass" or ".less" => "CSS",
            ".json" or ".jsonc" or ".json5" => "JSON",
            ".yaml" or ".yml" => "YAML",
            ".toml" => "TOML",
            ".xml" or ".xsd" or ".xsl" => "XML",
            ".md" or ".mdx" or ".rst" => "Documentation",
            ".graphql" or ".gql" => "GraphQL",
            ".proto" => "Protocol Buffers",
            ".tf" or ".hcl" => "HCL",
            ".gradle" => "Gradle",
            ".make" or ".mk" => "Makefile",
            _ => null
        };

        if (language is not null)
        {
            return language;
        }

        if (KnownTextExtensions.Contains(extension))
        {
            return extension.TrimStart('.').ToLowerInvariant();
        }

        return extension.Length > 1 ? extension[1..].ToLowerInvariant() : "unknown";
    }

    private static void ValidateOptions(RepositoryImportOptions importOptions)
    {
        if (string.IsNullOrWhiteSpace(importOptions.StorageRoot)
            || importOptions.MaxArchiveBytes <= 0
            || importOptions.MaxExpandedBytes <= 0
            || importOptions.MaxTextFileBytes <= 0
            || importOptions.MaxFiles <= 0
            || importOptions.MaxRefLength <= 0)
        {
            throw new InvalidOperationException("Repository import limits and storage root must be configured with positive values.");
        }
    }

    private sealed class JobProgressHeartbeat(
        IBackgroundJobExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        private DateTimeOffset lastReportedAt = DateTimeOffset.MinValue;
        private int lastProgress = -1;

        public Task ReportAsync(int progressPercentage, bool force = false) =>
            ReportIfDueAsync(progressPercentage, force);

        public async Task ReportIfDueAsync(int progressPercentage, bool force = false)
        {
            var now = DateTimeOffset.UtcNow;
            if (force || progressPercentage >= lastProgress + 2 || now - lastReportedAt >= TimeSpan.FromSeconds(5))
            {
                await executionContext.ReportProgressAsync(progressPercentage, cancellationToken);
                lastProgress = Math.Max(lastProgress, progressPercentage);
                lastReportedAt = now;
            }
        }
    }

    private sealed record EntryData(long Size, string Sha256, bool IsBinary, byte[]? Content);
}
