using MediaPlayer.Core;

var root = Path.Combine(Path.GetTempPath(), $"media-player-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
try
{
    File.WriteAllText(Path.Combine(root, "one.jpg"), "test");
    File.WriteAllText(Path.Combine(root, "two.mp4"), "test");
    File.WriteAllText(Path.Combine(root, "ignored.txt"), "test");
    File.WriteAllText(Path.Combine(root, "nested.playlist.json"), "{ \"name\": \"Nested\", \"entries\": [{ \"path\": \"two.mp4\" }] }");
    File.WriteAllText(Path.Combine(root, "root.playlist.json"), "{ \"name\": \"Root\", \"entries\": [{ \"path\": \"one.jpg\" }, { \"playlist\": \"nested.playlist.json\" }] }");

    var resolver = new PlaylistResolver();
    var snapshot = await resolver.MaterializeAsync(Path.Combine(root, "root.playlist.json"), false);
    Assert(snapshot.Items.Count == 2, "Recursive playlist should materialize two media items.");
    Assert(snapshot.Items[0].Kind == MediaKind.Image && snapshot.Items[1].Kind == MediaKind.Video, "Media types should be detected.");
    Assert(snapshot.Graph.Count == 2, "Dependency graph should contain both playlists.");
    var randomized = await resolver.MaterializeAsync(Path.Combine(root, "root.playlist.json"), true, 42);
    Assert(randomized.Items.Count == 2 && randomized.IsRandomized, "Random order must retain the full flattened list.");

    File.WriteAllText(Path.Combine(root, "cycle.playlist.json"), "{ \"name\": \"Cycle\", \"entries\": [{ \"playlist\": \"cycle.playlist.json\" }] }");
    try { await resolver.MaterializeAsync(Path.Combine(root, "cycle.playlist.json"), false); throw new Exception("Expected a cycle exception."); }
    catch (PlaylistCycleException) { }
    Console.WriteLine("All core tests passed.");
}
finally { Directory.Delete(root, recursive: true); }

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
