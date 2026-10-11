using Microsoft.EntityFrameworkCore;
using RepoAI.Domain.Entities;

namespace RepoAI.Infrastructure.Persistence;

public sealed class RepoAIDbContext(DbContextOptions<RepoAIDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();

    public DbSet<RepositorySnapshot> RepositorySnapshots => Set<RepositorySnapshot>();

    public DbSet<RepositoryFile> RepositoryFiles => Set<RepositoryFile>();

    public DbSet<RepositoryArchitectureAnalysis> RepositoryArchitectureAnalyses => Set<RepositoryArchitectureAnalysis>();

    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public DbSet<RepositoryStoryboardDocument> RepositoryStoryboards => Set<RepositoryStoryboardDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RepoAIDbContext).Assembly);
    }
}
