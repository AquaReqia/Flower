using Flower.Agents.Abstractions;
using Flower.Agents.Configuration;
using Microsoft.Extensions.Options;

namespace Flower.Agents.ModelApi;

/// <summary>
/// Model çağrılarını iki öncelik sınıfına ayırır. Arka plan (proaktif) işleri ayrı ve dar bir
/// eşzamanlılık limitiyle çalışır; böylece aynı GPU'yu paylaşan reaktif işler gecikmez.
/// </summary>
public sealed class ModelCallScheduler : IDisposable
{
    private readonly SemaphoreSlim _interactive;
    private readonly SemaphoreSlim _background;

    public ModelCallScheduler(IOptions<AgentOptions> options)
    {
        var o = options.Value.Scheduler;
        _interactive = new SemaphoreSlim(Math.Max(1, o.InteractiveConcurrency));
        _background = new SemaphoreSlim(Math.Max(1, o.BackgroundConcurrency));
    }

    public async Task<T> RunAsync<T>(ModelPriority priority, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        var gate = priority == ModelPriority.Background ? _background : _interactive;
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await call(ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        _interactive.Dispose();
        _background.Dispose();
    }
}
