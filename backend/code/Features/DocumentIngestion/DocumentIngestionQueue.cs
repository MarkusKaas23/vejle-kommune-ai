using System.Collections.Concurrent;
using System.Threading.Channels;

namespace VejleKommune.Code.Features.DocumentIngestion;

/// <summary>
/// Phase 7 – Generative Pipeline (Document → Content).
/// Singleton in-memory queue for document ingestion jobs.
/// Uses System.Threading.Channels for backpressure-safe producer/consumer handoff.
/// </summary>
public sealed class DocumentIngestionQueue
{
    private readonly Channel<DocumentIngestionJob> _channel =
        Channel.CreateUnbounded<DocumentIngestionJob>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, DocumentIngestionJobState> _states = new();

    public void Enqueue(DocumentIngestionJob job)
    {
        _states[job.JobId] = new DocumentIngestionJobState(job.JobId, DocumentIngestionStatus.Queued);
        _channel.Writer.TryWrite(job);
    }

    public ChannelReader<DocumentIngestionJob> Reader => _channel.Reader;

    public DocumentIngestionJobState? GetState(string jobId) =>
        _states.TryGetValue(jobId, out DocumentIngestionJobState? state) ? state : null;

    public void SetState(string jobId, DocumentIngestionJobState state) =>
        _states[jobId] = state;
}
