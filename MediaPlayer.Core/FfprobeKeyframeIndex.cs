using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace MediaPlayer.Core;

public interface IKeyframeIndex
{
    Task<IReadOnlyList<TimeSpan>> ReadAsync(string videoPath, CancellationToken cancellationToken = default);
}

public sealed class FfprobeKeyframeIndex(string executablePath = "ffprobe") : IKeyframeIndex
{
    public async Task<IReadOnlyList<TimeSpan>> ReadAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo { FileName = executablePath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-v", "error", "-select_streams", "v:0", "-skip_frame", "nokey", "-show_entries", "frame=best_effort_timestamp_time", "-of", "json", videoPath })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start ffprobe.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidOperationException($"ffprobe failed: {error.Trim()}");

        using var document = JsonDocument.Parse(output);
        var result = new List<TimeSpan>();
        foreach (var frame in document.RootElement.GetProperty("frames").EnumerateArray())
            if (frame.TryGetProperty("best_effort_timestamp_time", out var timestamp) && double.TryParse(timestamp.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
                result.Add(TimeSpan.FromSeconds(seconds));
        return result;
    }
}
