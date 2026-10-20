using System.Collections.Concurrent;

namespace Flower.Agents.Runs;

/// <summary>AgentRun kayıtları.</summary>
public interface IAgentRunStore
{
    Task AddAsync(AgentRun run, CancellationToken ct);
    Task UpdateAsync(AgentRun run, CancellationToken ct);
    Task<AgentRun?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Kullanıcının <paramref name="sinceUtc"/> sonrası run'ları, en yeniden eskiye.</summary>
    Task<IReadOnlyList<AgentRun>> GetRecentForUserAsync(string userEmail, DateTime sinceUtc, int take, CancellationToken ct);
}

/// <summary>Bellek içi run deposu (test ve persistence'sız çalışma için).</summary>
public sealed class InMemoryAgentRunStore : IAgentRunStore
{
    private readonly ConcurrentDictionary<Guid, AgentRun> _runs = new();

    public Task AddAsync(AgentRun run, CancellationToken ct)
    {
        if (run.Id == Guid.Empty)
        {
            run.Id = Guid.NewGuid();
        }

        _runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AgentRun run, CancellationToken ct)
    {
        _runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task<AgentRun?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(_runs.GetValueOrDefault(id));

    public Task<IReadOnlyList<AgentRun>> GetRecentForUserAsync(string userEmail, DateTime sinceUtc, int take, CancellationToken ct)
    {
        IReadOnlyList<AgentRun> list = _runs.Values
            .Where(r => string.Equals(r.UserEmail, userEmail, StringComparison.OrdinalIgnoreCase) && r.CreatedAt >= sinceUtc)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToList();
        return Task.FromResult(list);
    }

    /// <summary>Test yardımcısı.</summary>
    public IReadOnlyList<AgentRun> All => _runs.Values.OrderBy(r => r.CreatedAt).ToList();
}
