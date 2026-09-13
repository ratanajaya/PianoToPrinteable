namespace PianoGuide;

public enum Hand { Unassigned, Left, Right }

public sealed class MidiTrackInfo
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required int NoteCount { get; init; }
    public Hand Hand { get; set; }
}

public sealed record PianoNote(int Pitch, long StartTick, long EndTick, double StartSeconds,
    double EndSeconds, int TrackIndex);

public sealed class MidiScore
{
    public required string SourcePath { get; init; }
    public required string Title { get; init; }
    public required int TicksPerQuarterNote { get; init; }
    public required IReadOnlyList<MidiTrackInfo> Tracks { get; init; }
    public required IReadOnlyList<PianoNote> Notes { get; init; }
    public double DurationSeconds => Notes.Count == 0 ? 0 : Notes.Max(n => n.EndSeconds);
    public int StepCount => Notes.Select(n => n.StartTick).Distinct().Count();
    public Hand HandFor(int trackIndex) => Tracks.First(t => t.Index == trackIndex).Hand;
}

public readonly record struct CropRect(double X, double Y, double Width, double Height)
{
    public static CropRect Full => new(0, 0, 1, 1);

    public CropRect Clamp()
    {
        var x = Math.Clamp(X, 0, 1);
        var y = Math.Clamp(Y, 0, 1);
        return new CropRect(x, y, Math.Clamp(Width, 0, 1 - x), Math.Clamp(Height, 0, 1 - y));
    }
}

public static class TimeText
{
    public static string Format(double seconds)
    {
        var value = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss\.fff")
            : value.ToString(@"mm\:ss\.fff");
    }

    public static double Parse(string text)
    {
        text = text.Trim();
        var parts = text.Split(':');
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (parts.Length is 2 or 3 &&
            double.TryParse(parts[^1], System.Globalization.NumberStyles.Float, culture, out var finalSeconds)
            && finalSeconds >= 0 && finalSeconds < 60 &&
            int.TryParse(parts[^2], out var minutes) && minutes >= 0)
        {
            if (parts.Length == 2) return minutes * 60.0 + finalSeconds;
            if (minutes < 60 && int.TryParse(parts[0], out var hours) && hours >= 0)
                return hours * 3600.0 + minutes * 60.0 + finalSeconds;
        }
        if (parts.Length == 1 && double.TryParse(text, System.Globalization.NumberStyles.Float,
                culture, out var seconds) && seconds >= 0 && double.IsFinite(seconds)) return seconds;
        throw new ArgumentException("Enter a non-negative time such as 00:12.500 or 12.5.");
    }
}
