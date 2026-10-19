using System.Text.Json;

namespace MediaPlayer.Core;

public sealed class CollectionResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public async Task<CollectionSnapshot> MaterializeAsync(string manifestPath, bool randomize, int? randomSeed = null, CancellationToken cancellationToken = default)
    {
        var items = new List<MediaItem>();
        var graph = new Dictionary<string, CollectionNode>(StringComparer.OrdinalIgnoreCase);
        await ExpandManifestAsync(NormalizeExistingPath(manifestPath), items, graph, [], cancellationToken);
        if (randomize) Shuffle(items, randomSeed is null ? Random.Shared : new Random(randomSeed.Value));
        return new CollectionSnapshot(items, graph, randomize);
    }

    public CollectionSnapshot FromLooseFiles(IEnumerable<string> paths, bool randomize, int? randomSeed = null)
    {
        var items = paths.Select(ResolveSymbolicLink).Where(File.Exists)
            .Select(path => MediaTypes.TryGetKind(path, out var kind) ? new MediaItem(path, kind, "Loose files") : null)
            .Where(item => item is not null).Cast<MediaItem>().ToList();
        if (randomize) Shuffle(items, randomSeed is null ? Random.Shared : new Random(randomSeed.Value));
        return new CollectionSnapshot(items, new Dictionary<string, CollectionNode>(), randomize);
    }

    private static async Task ExpandManifestAsync(string manifestPath, List<MediaItem> output, Dictionary<string, CollectionNode> graph, List<string> stack, CancellationToken cancellationToken)
    {
        var cycleStart = stack.FindIndex(path => StringComparer.OrdinalIgnoreCase.Equals(path, manifestPath));
        if (cycleStart >= 0) throw new CollectionCycleException(stack.Skip(cycleStart).Append(manifestPath).ToArray());

        stack.Add(manifestPath);
        try
        {
            await using var stream = File.OpenRead(manifestPath);
            var definition = await JsonSerializer.DeserializeAsync<CollectionDefinition>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException($"Collection '{manifestPath}' is empty or invalid.");
            var baseDirectory = Path.GetDirectoryName(manifestPath)!;
            var dependencies = new List<string>();
            foreach (var entry in definition.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(entry.Collection))
                {
                    var dependency = NormalizeExistingPath(Combine(baseDirectory, entry.Collection));
                    dependencies.Add(dependency);
                    await ExpandManifestAsync(dependency, output, graph, stack, cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(entry.Path))
                {
                    ExpandPath(Combine(baseDirectory, entry.Path), entry.Recursive, manifestPath, output, cancellationToken);
                }
            }
            graph[manifestPath] = new CollectionNode(manifestPath, definition.Name, dependencies);
        }
        finally { stack.RemoveAt(stack.Count - 1); }
    }

    private static void ExpandPath(string rawPath, bool recursive, string sourceCollection, List<MediaItem> output, CancellationToken cancellationToken)
    {
        var path = ResolveSymbolicLink(Path.GetFullPath(rawPath));
        if (File.Exists(path))
        {
            if (MediaTypes.TryGetKind(path, out var kind)) output.Add(new MediaItem(path, kind, sourceCollection));
            return;
        }
        if (!Directory.Exists(path)) throw new FileNotFoundException($"Collection entry does not exist: {rawPath}", rawPath);
        WalkDirectory(path, recursive, sourceCollection, output, new HashSet<string>(StringComparer.OrdinalIgnoreCase), cancellationToken);
    }

    private static void WalkDirectory(string directory, bool recursive, string sourceCollection, List<MediaItem> output, HashSet<string> visitedDirectories, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolvedDirectory = ResolveSymbolicLink(directory);
        if (!visitedDirectories.Add(resolvedDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(resolvedDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            var resolvedFile = ResolveSymbolicLink(file);
            if (MediaTypes.TryGetKind(resolvedFile, out var kind)) output.Add(new MediaItem(resolvedFile, kind, sourceCollection));
        }
        if (!recursive) return;
        foreach (var child in Directory.EnumerateDirectories(resolvedDirectory).Order(StringComparer.OrdinalIgnoreCase))
            WalkDirectory(child, true, sourceCollection, output, visitedDirectories, cancellationToken);
    }

    private static string NormalizeExistingPath(string path)
    {
        var fullPath = ResolveSymbolicLink(Path.GetFullPath(path));
        return File.Exists(fullPath) ? fullPath : throw new FileNotFoundException($"Collection manifest does not exist: {fullPath}", fullPath);
    }

    private static string Combine(string baseDirectory, string path) => Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path);

    private static string ResolveSymbolicLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        try { return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? info.FullName; }
        catch (IOException) { return info.FullName; }
    }

    private static void Shuffle<T>(IList<T> items, Random random)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var other = random.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }
}
