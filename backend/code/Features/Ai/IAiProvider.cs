namespace VejleKommune.Code.Features.Ai;

public interface IAiProvider
{
    string ProviderName { get; }
    string ModelName { get; }
    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
}
