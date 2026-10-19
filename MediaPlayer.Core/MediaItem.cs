namespace MediaPlayer.Core;

public enum MediaKind { Image, Video }

public sealed record MediaItem(string Path, MediaKind Kind, string SourceCollection);

public static class MediaTypes
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".bmp", ".gif", ".heic", ".heif", ".ico", ".jfif", ".jpeg", ".jpg", ".png", ".tif", ".tiff", ".webp" };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".3gp", ".avi", ".m2ts", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts", ".ts", ".webm", ".wmv" };

    public static bool TryGetKind(string path, out MediaKind kind)
    {
        var extension = System.IO.Path.GetExtension(path);
        if (ImageExtensions.Contains(extension)) { kind = MediaKind.Image; return true; }
        if (VideoExtensions.Contains(extension)) { kind = MediaKind.Video; return true; }
        kind = default;
        return false;
    }
}
