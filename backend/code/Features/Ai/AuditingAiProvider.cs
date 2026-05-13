namespace VejleKommune.Code.Features.Ai;

public sealed class AuditingAiProvider : IAiProvider
{
    private const string UnknownPattern = "unknown";

    private readonly IAiProvider _inner;
    private readonly IAiCallAuditSink _sink;
    private readonly TimeProvider _timeProvider;

    public AuditingAiProvider(IAiProvider inner, IAiCallAuditSink sink, TimeProvider timeProvider)
    {
        _inner = inner;
        _sink = sink;
        _timeProvider = timeProvider;
    }

    public string ProviderName => _inner.ProviderName;
    public string ModelName => _inner.ModelName;

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        var contentLengthChars = ComputeContentLength(request);
        var patternName = request.CallContext?.PatternName ?? UnknownPattern;
        var nodeId = request.CallContext?.NodeId;
        var startedAt = _timeProvider.GetUtcNow();

        try
        {
            var response = await _inner.CompleteAsync(request, cancellationToken);
            await _sink.EmitAsync(new AiCallAuditRecord(
                PatternName: patternName,
                NodeId: nodeId,
                ProviderName: _inner.ProviderName,
                ModelName: _inner.ModelName,
                ContentLengthChars: contentLengthChars,
                LatencyMs: (long)response.Latency.TotalMilliseconds,
                CostUsd: response.CostUsd,
                InputTokens: response.InputTokens,
                OutputTokens: response.OutputTokens,
                TimestampUtc: startedAt,
                Success: true), cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            var elapsed = _timeProvider.GetUtcNow() - startedAt;
            await _sink.EmitAsync(new AiCallAuditRecord(
                PatternName: patternName,
                NodeId: nodeId,
                ProviderName: _inner.ProviderName,
                ModelName: _inner.ModelName,
                ContentLengthChars: contentLengthChars,
                LatencyMs: (long)elapsed.TotalMilliseconds,
                CostUsd: 0m,
                InputTokens: 0,
                OutputTokens: 0,
                TimestampUtc: startedAt,
                Success: false,
                ErrorMessage: ex.Message), CancellationToken.None);
            throw;
        }
    }

    private static int ComputeContentLength(AiRequest request)
    {
        var promptLength = request.Prompt.Length;
        var systemLength = request.SystemPrompt?.Length ?? 0;
        var imageBytes = request.Images?.Sum(i => i.Bytes.Length) ?? 0;
        return promptLength + systemLength + imageBytes;
    }
}
