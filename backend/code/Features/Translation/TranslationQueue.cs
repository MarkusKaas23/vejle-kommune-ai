using System.Collections.Concurrent;
using System.Threading.Channels;

namespace VejleKommune.Code.Features.Translation;

/// <summary>
/// Singleton in-memory queue for translation jobs.
/// Uses System.Threading.Channels for backpressure-safe producer/consumer handoff.
/// </summary>
public sealed class TranslationQueue
{
    private readonly Channel<TranslationJob> _channel =
        Channel.CreateUnbounded<TranslationJob>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, TranslationJobState> _states = new();

    public void Enqueue(TranslationJob job)
    {
        _states[job.JobId] = new TranslationJobState(job.JobId, TranslationStatus.Queued);
        _channel.Writer.TryWrite(job);
    }

    public ChannelReader<TranslationJob> Reader => _channel.Reader;

    public TranslationJobState? GetState(string jobId) =>
        _states.TryGetValue(jobId, out TranslationJobState? state) ? state : null;

    public void SetState(string jobId, TranslationJobState state) =>
        _states[jobId] = state;
}
