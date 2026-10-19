using System.Text.Json.Serialization;

namespace MediaPlayer.Core;

public sealed class CollectionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Untitled collection";

    [JsonPropertyName("entries")]
    public List<CollectionEntry> Entries { get; init; } = [];
}

public sealed class CollectionEntry
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("collection")]
    public string? Collection { get; init; }

    [JsonPropertyName("recursive")]
    public bool Recursive { get; init; } = true;
}

public sealed record CollectionNode(string ManifestPath, string Name, IReadOnlyList<string> Dependencies);

public sealed record CollectionSnapshot(IReadOnlyList<MediaItem> Items, IReadOnlyDictionary<string, CollectionNode> Graph, bool IsRandomized);

public sealed class CollectionCycleException(IReadOnlyList<string> cycle)
    : Exception($"Collection cycle detected: {string.Join(" -> ", cycle)}")
{
    public IReadOnlyList<string> Cycle { get; } = cycle;
}
