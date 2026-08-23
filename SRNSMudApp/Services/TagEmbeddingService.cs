#region

using SmartComponents.LocalEmbeddings;

#endregion

namespace SRNSMudApp.Services;

public class TagEmbeddingService(LocalEmbedder embedder) : ITagEmbeddingService
{
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text)
    {
        EmbeddingF32 embedding = await Task.Run(() => embedder.Embed(text));
        return embedding.Values;
    }
}