using System.Collections.Concurrent;
using System.Threading.Channels;

namespace VejleKommune.Code.Features.Accessibility;

/// <summary>
/// Singleton in-memory queue for accessibility audit jobs.
/// Findings are stored here — never written back to the CMS.
/// Dismiss = delete from this dictionary (trivial rollback).
/// </summary>
public sealed class AccessibilityQueue
{
    private readonly Channel<AccessibilityJob> _channel =
        Channel.CreateUnbounded<AccessibilityJob>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly ConcurrentDictionary<string, AccessibilityJobState> _states = new();

    public void Enqueue(AccessibilityJob job)
    {
        _states[job.JobId] = new AccessibilityJobState(job.JobId, AccessibilityStatus.Queued);
        _channel.Writer.TryWrite(job);
    }

    public ChannelReader<AccessibilityJob> Reader => _channel.Reader;

    public AccessibilityJobState? GetState(string jobId) =>
        _states.TryGetValue(jobId, out AccessibilityJobState? state) ? state : null;

    public void SetState(string jobId, AccessibilityJobState state) =>
        _states[jobId] = state;

    /// <summary>
    /// Dismiss all findings for a job. Returns false if the job was not found.
    /// This is the "rollback" for the Async analyze Pattern — trivially reversible.
    /// </summary>
    public bool Dismiss(string jobId) => _states.TryRemove(jobId, out _);
}
