#region

using SmartComponents.LocalEmbeddings;

#endregion

namespace SRNSMudApp.Services;

public class TagEmbeddingService(LocalEmbedder embedder) : ITagEmbeddingService
{
    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text)
    {
        EmbeddingF32 embedding = embedder.Embed(text);
        return Task.FromResult(embedding.Values);
    }
}