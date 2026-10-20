using System.Collections.Concurrent;

namespace Flower.Agents.KnowledgeBase;

/// <summary>Bellek içi bilgi deposu (test ve persistence'sız çalışma için).</summary>
public sealed class InMemoryKnowledgeStore : IKnowledgeStore
{
    private readonly ConcurrentDictionary<Guid, KnowledgeEntry> _entries = new();

    public Task<KnowledgeEntry?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(_entries.GetValueOrDefault(id));

    public Task AddAsync(KnowledgeEntry entry, CancellationToken ct)
    {
        if (entry.Id == Guid.Empty)
        {
            entry.Id = Guid.NewGuid();
        }

        _entries[entry.Id] = entry;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(KnowledgeEntry entry, CancellationToken ct)
    {
        _entries[entry.Id] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(KnowledgeEntry Entry, double Score)>> SearchAsync(KnowledgeSearch search, CancellationToken ct) =>
        Task.FromResult(KnowledgeScoring.Rank(_entries.Values, search));

    /// <summary>Test yardımcısı.</summary>
    public IReadOnlyList<KnowledgeEntry> All => _entries.Values.OrderBy(e => e.CreatedAt).ToList();
}
