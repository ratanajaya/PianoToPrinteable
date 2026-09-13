using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PianoGuide;

public sealed record VideoInfo(double DurationSeconds, int Width, int Height);

public static class VideoTools
{
    public static async Task<VideoInfo> ProbeAsync(string source, CancellationToken cancellation = default)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Video file not found.", source);
        var (code, output, error) = await RunAsync("ffprobe",
            ["-v", "error", "-select_streams", "v:0", "-show_entries",
                "format=duration:stream=width,height", "-of", "json", source], cancellation);
        if (code != 0) throw new InvalidDataException($"FFprobe could not read this video: {error.Trim()}");
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        var streams = root.GetProperty("streams");
        if (streams.GetArrayLength() == 0) throw new InvalidDataException("The file has no video stream.");
        var stream = streams[0];
        var durationText = root.GetProperty("format").GetProperty("duration").GetString();
        if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration)
            || duration <= 0)
            throw new InvalidDataException("The video duration could not be read.");
        return new VideoInfo(duration, stream.GetProperty("width").GetInt32(),
            stream.GetProperty("height").GetInt32());
    }

    public static async Task ExportAsync(string source, IEnumerable<double> selectedSeconds,
        CropRect crop, string output, IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancellation = default)
    {
        var info = await ProbeAsync(source, cancellation);
        var moments = selectedSeconds.OrderBy(x => x).ToList();
        if (moments.Count == 0) throw new InvalidOperationException("Add at least one frame before exporting.");
        if (moments.Any(t => t < 0 || t >= info.DurationSeconds || double.IsNaN(t)))
            throw new ArgumentOutOfRangeException(nameof(selectedSeconds), "A selected timestamp is outside the video.");
        if (moments.Zip(moments.Skip(1)).Any(pair => pair.First == pair.Second))
            throw new ArgumentException("Remove duplicate timestamps before exporting.");
        crop = crop.Clamp();
        var width = Math.Min(info.Width, (int)Math.Round(crop.Width * info.Width));
        var height = Math.Min(info.Height, (int)Math.Round(crop.Height * info.Height));
        var x = Math.Min(info.Width - width, (int)Math.Round(crop.X * info.Width));
        var y = Math.Min(info.Height - height, (int)Math.Round(crop.Y * info.Height));
        if (width < 32 || height < 32) throw new ArgumentException("The crop must be at least 32 pixels wide and high.");

        var tempFolder = Path.Combine(Path.GetTempPath(), "PianoGuide-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            var frames = new List<(double Seconds, string ImagePath)>();
            for (var i = 0; i < moments.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var image = Path.Combine(tempFolder, $"frame-{i + 1:0000}.png");
                var (code, _, error) = await RunAsync("ffmpeg",
                    ["-hide_banner", "-loglevel", "error", "-nostdin", "-ss",
                        moments[i].ToString("0.000000", CultureInfo.InvariantCulture), "-i", source,
                        "-frames:v", "1", "-vf", $"crop={width}:{height}:{x}:{y}", "-y", image],
                    cancellation);
                if (code != 0 || !File.Exists(image))
                    throw new InvalidDataException($"Could not extract the frame at {TimeText.Format(moments[i])}: {error.Trim()}");
                frames.Add((moments[i], image));
                progress?.Report((i + 1, moments.Count));
            }
            PdfExports.ExportVideo(source, frames, output);
        }
        finally
        {
            Directory.Delete(tempFolder, true);
        }
    }

    private static async Task<(int Code, string Output, string Error)> RunAsync(string program,
        IEnumerable<string> args, CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new FileNotFoundException($"{program} is required. Put FFmpeg and FFprobe on PATH.", ex);
        }
        var stdout = process.StandardOutput.ReadToEndAsync(cancellation);
        var stderr = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);
        return (process.ExitCode, await stdout, await stderr);
    }
}
