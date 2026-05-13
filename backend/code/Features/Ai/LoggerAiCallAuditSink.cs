using Microsoft.Extensions.Logging;

namespace VejleKommune.Code.Features.Ai;

public sealed class LoggerAiCallAuditSink : IAiCallAuditSink
{
    private readonly ILogger<LoggerAiCallAuditSink> _logger;

    public LoggerAiCallAuditSink(ILogger<LoggerAiCallAuditSink> logger)
    {
        _logger = logger;
    }

    public Task EmitAsync(AiCallAuditRecord record, CancellationToken cancellationToken = default)
    {
        if (record.Success)
        {
            _logger.LogInformation(
                "AI_CALL_AUDIT pattern={Pattern} node={NodeId} provider={Provider}/{Model} contentChars={ContentChars} latencyMs={LatencyMs} costUsd={CostUsd} inTokens={InTokens} outTokens={OutTokens} ts={Timestamp:O}",
                record.PatternName, record.NodeId ?? "-", record.ProviderName, record.ModelName,
                record.ContentLengthChars, record.LatencyMs, record.CostUsd,
                record.InputTokens, record.OutputTokens, record.TimestampUtc);
        }
        else
        {
            _logger.LogWarning(
                "AI_CALL_AUDIT_FAILED pattern={Pattern} node={NodeId} provider={Provider}/{Model} contentChars={ContentChars} latencyMs={LatencyMs} ts={Timestamp:O} error={Error}",
                record.PatternName, record.NodeId ?? "-", record.ProviderName, record.ModelName,
                record.ContentLengthChars, record.LatencyMs, record.TimestampUtc, record.ErrorMessage);
        }

        return Task.CompletedTask;
    }
}
