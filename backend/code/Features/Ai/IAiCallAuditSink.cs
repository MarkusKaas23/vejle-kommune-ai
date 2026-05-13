namespace VejleKommune.Code.Features.Ai;

public interface IAiCallAuditSink
{
    Task EmitAsync(AiCallAuditRecord record, CancellationToken cancellationToken = default);
}
