# FrameFlow media player

FrameFlow is a Windows media viewer prototype built with C#/.NET and WinUI 3. It keeps transport controls below the media, supports images and videos in one collection, zooms either media type, steps video in both directions, and visualizes indexed key frames.

## Architecture

- `MediaPlayer`: WinUI 3 interface and Windows playback adapter.
- `MediaPlayer.Core`: media detection, collection graph expansion, symbolic-link resolution, random ordering, and FFmpeg keyframe indexing.
- `MediaPlayer.Core.Tests`: dependency-free executable tests for the collection engine.
- `examples/sample.collection.json`: collection format example.

The complete recursive collection is materialized before Fisher-Yates shuffling. Cycles are reported with their dependency path. Directory traversal resolves symbolic links and tracks resolved directories to prevent link loops.

## Build

Requirements: Windows 10 1809 or newer, .NET 10 SDK, the Windows SDK, and the WinUI application development workload.

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet-home"
$env:NUGET_PACKAGES = "$PWD\.nuget-packages"
dotnet restore MediaPlayer.slnx
dotnet build MediaPlayer.slnx
dotnet run --project MediaPlayer
```

Run the core tests with `dotnet run --project MediaPlayer.Core.Tests`.

Put `ffprobe.exe` on `PATH` to display keyframe ticks. Playback works without it. The intended production backend is FFmpeg loaded in-process behind playback and keyframe interfaces; the Windows adapter keeps the first slice usable while that backend is developed.

## Shortcuts

- Left / Right: previous or next item
- Space: play or pause
- Comma / Period: previous or next video frame
- Ctrl + mouse wheel: zoom
