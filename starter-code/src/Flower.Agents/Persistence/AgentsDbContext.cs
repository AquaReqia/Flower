using Flower.Agents.KnowledgeBase;
using Flower.Agents.Runs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flower.Agents.Persistence;

/// <summary>Agent tabloları. Flower ile aynı veritabanı, <c>agents</c> şeması.</summary>
public sealed class AgentsDbContext(DbContextOptions<AgentsDbContext> options) : DbContext(options)
{
    public const string Schema = "agents";

    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<KnowledgeEntry> KnowledgeEntries => Set<KnowledgeEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AgentRunConfiguration());
        modelBuilder.ApplyConfiguration(new KnowledgeEntryConfiguration());
    }
}

internal sealed class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    public void Configure(EntityTypeBuilder<AgentRun> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.UserEmail).HasMaxLength(320).IsRequired();
        b.Property(x => x.Question).IsRequired();
        b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Intent).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Verdict).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.Feedback).HasConversion<string>().HasMaxLength(32);
        b.HasIndex(x => new { x.UserEmail, x.CreatedAt });
        b.HasIndex(x => x.RequestId);
        b.HasIndex(x => x.Feedback);
    }
}

internal sealed class KnowledgeEntryConfiguration : IEntityTypeConfiguration<KnowledgeEntry>
{
    public void Configure(EntityTypeBuilder<KnowledgeEntry> b)
    {
        b.HasKey(x => x.Id);
        b.Ignore(x => x.IsActive);
        b.Property(x => x.Title).HasMaxLength(500).IsRequired();
        b.Property(x => x.CanonicalQuestion).IsRequired();
        b.Property(x => x.Answer).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.CreatedByEmail).HasMaxLength(320);
        b.Property(x => x.EmbeddingModel).HasMaxLength(200);
        b.HasIndex(x => x.Status);
    }
}

/// <summary><c>dotnet ef</c> için. Bağlantı: <c>FLOWER_AGENTS_DB</c> ortam değişkeni (migration üretmek için bağlantı gerekmez).</summary>
public sealed class AgentsDbContextFactory : IDesignTimeDbContextFactory<AgentsDbContext>
{
    public AgentsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("FLOWER_AGENTS_DB") ?? "Host=localhost;Database=flower";
        var options = new DbContextOptionsBuilder<AgentsDbContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AgentsDbContext.Schema))
            .Options;
        return new AgentsDbContext(options);
    }
}
