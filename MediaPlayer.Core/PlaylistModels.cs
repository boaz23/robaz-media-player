using System.Text.Json.Serialization;

namespace MediaPlayer.Core;

public sealed class PlaylistDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "Untitled playlist";

    [JsonPropertyName("entries")]
    public List<PlaylistEntry> Entries { get; init; } = [];
}

public sealed class PlaylistEntry
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("playlist")]
    public string? Playlist { get; init; }

    [JsonPropertyName("recursive")]
    public bool Recursive { get; init; } = true;
}

public sealed record PlaylistNode(string ManifestPath, string Name, IReadOnlyList<string> Dependencies);

public sealed record PlaylistSnapshot(IReadOnlyList<MediaItem> Items, IReadOnlyDictionary<string, PlaylistNode> Graph, bool IsRandomized);

public sealed class PlaylistCycleException(IReadOnlyList<string> cycle)
    : Exception($"Playlist cycle detected: {string.Join(" -> ", cycle)}")
{
    public IReadOnlyList<string> Cycle { get; } = cycle;
}
