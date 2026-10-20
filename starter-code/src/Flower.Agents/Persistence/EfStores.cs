using Flower.Agents.KnowledgeBase;
using Flower.Agents.Runs;
using Microsoft.EntityFrameworkCore;

namespace Flower.Agents.Persistence;

/// <summary>EF bilgi deposu. Aday kayıtlar SQL'de durumla daraltılır, puanlama C#'ta (<see cref="KnowledgeScoring"/>).</summary>
public sealed class EfKnowledgeStore(AgentsDbContext db) : IKnowledgeStore
{
    public async Task<KnowledgeEntry?> GetAsync(Guid id, CancellationToken ct) =>
        await db.KnowledgeEntries.FindAsync([id], ct).ConfigureAwait(false);

    public async Task AddAsync(KnowledgeEntry entry, CancellationToken ct)
    {
        if (entry.Id == Guid.Empty)
        {
            entry.Id = Guid.NewGuid();
        }

        db.KnowledgeEntries.Add(entry);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(KnowledgeEntry entry, CancellationToken ct)
    {
        if (db.Entry(entry).State == EntityState.Detached)
        {
            db.KnowledgeEntries.Update(entry);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<(KnowledgeEntry Entry, double Score)>> SearchAsync(KnowledgeSearch search, CancellationToken ct)
    {
        // shortcut: tüm aktif kayıtlar çekilip C#'ta puanlanır; ≥ 10 bin kayıtta ILIKE ön eleme veya pgvector'e geçilmeli.
        var active = await db.KnowledgeEntries
            .Where(e => e.Status == KnowledgeStatus.Confirmed || e.Status == KnowledgeStatus.Verified)
            .ToListAsync(ct).ConfigureAwait(false);
        return KnowledgeScoring.Rank(active, search);
    }
}

/// <summary>EF run deposu.</summary>
public sealed class EfAgentRunStore(AgentsDbContext db) : IAgentRunStore
{
    public async Task AddAsync(AgentRun run, CancellationToken ct)
    {
        if (run.Id == Guid.Empty)
        {
            run.Id = Guid.NewGuid();
        }

        db.AgentRuns.Add(run);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(AgentRun run, CancellationToken ct)
    {
        if (db.Entry(run).State == EntityState.Detached)
        {
            db.AgentRuns.Update(run);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<AgentRun?> GetAsync(Guid id, CancellationToken ct) =>
        await db.AgentRuns.FindAsync([id], ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<AgentRun>> GetRecentForUserAsync(string userEmail, DateTime sinceUtc, int take, CancellationToken ct)
    {
        var email = userEmail.Trim().ToLowerInvariant();
        return await db.AgentRuns
            .Where(r => r.UserEmail == email && r.CreatedAt >= sinceUtc)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct).ConfigureAwait(false);
    }
}
