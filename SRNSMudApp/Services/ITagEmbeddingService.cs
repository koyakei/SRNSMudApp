namespace SRNSMudApp.Services;

public interface ITagEmbeddingService
{
    Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text);
}