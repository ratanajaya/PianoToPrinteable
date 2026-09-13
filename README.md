# Printable Piano Guide

A Windows desktop app that turns selected video frames or MIDI key presses into printable A4 landscape PDFs. It runs locally and does not upload source files.

## Use the app

1. Open `PianoGuide.exe` from the portable app folder. The app is built for Windows ARM64 and includes its .NET runtime.
2. For video, open a file, play or scrub with sound, pause at a useful moment, and select **Add frame**. You can edit timestamps in the list. Drag on the video to select one crop rectangle for every frame, then export the PDF. Windows must be able to play the video's codec; FFmpeg and FFprobe must be on `PATH` for frame extraction.
3. For MIDI, open a `.mid` or `.midi` file. Check the left/right hand assignments for each note track, set the start and end of the practice range, then export. The PDF shows six steps per page. Solid color means press now; pale color means the key is still held. `C4` is middle C, and `b` means beats.

The video workflow copies chosen moments; it does not detect notes automatically. The MIDI workflow uses note-on and note-off timing and does not interpret sustain-pedal controls or suggest fingering.

## Build and verify

Requirements for building: .NET 10 SDK with WPF support. FFmpeg and FFprobe must be on `PATH` to export video frames.

```powershell
dotnet restore PrintablePianoGuide.slnx
dotnet build PrintablePianoGuide.slnx -c Release
dotnet run --project PianoGuide.Smoke/PianoGuide.Smoke.csproj
dotnet run --project PianoGuide.Smoke/PianoGuide.Smoke.csproj -- --media
dotnet publish PianoGuide/PianoGuide.csproj -c Release -r win-arm64 --self-contained true -o output/app/PrintablePianoGuide
Compress-Archive -LiteralPath output/app/PrintablePianoGuide -DestinationPath output/app/PrintablePianoGuide-win-arm64.zip -Force
```

The smoke project uses the supplied samples. It checks note grouping, hand detection, time parsing, page counts, practice-range filtering, and three timestamped video frames. The optional `--media` run also checks that Windows opens and seeks the sample MP4. It writes example PDFs to `output/pdf/`.

The portable publish output is a folder, not an installer. Copy the whole folder to another Windows ARM64 computer. FFmpeg is a separate prerequisite for video export.
