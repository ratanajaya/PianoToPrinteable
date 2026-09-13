using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp;
using System.IO;
using System.Globalization;

namespace PianoGuide;

public static class PdfExports
{
    private const double PageWidth = 841.89;
    private const double PageHeight = 595.28;
    private static readonly XColor Ink = XColor.FromArgb(31, 42, 57);
    private static readonly XColor Muted = XColor.FromArgb(92, 106, 124);
    private static readonly XColor Rule = XColor.FromArgb(218, 225, 233);
    private static readonly XColor LeftNew = XColor.FromArgb(42, 105, 190);
    private static readonly XColor RightNew = XColor.FromArgb(223, 116, 36);
    private static readonly XColor UnassignedNew = XColor.FromArgb(129, 91, 162);
    private static readonly XColor LeftHeld = XColor.FromArgb(190, 215, 245);
    private static readonly XColor RightHeld = XColor.FromArgb(250, 216, 184);
    private static readonly XColor UnassignedHeld = XColor.FromArgb(224, 207, 237);
    private static readonly XFont Title = new("Arial", 16, XFontStyleEx.Bold);
    private static readonly XFont Heading = new("Arial", 9, XFontStyleEx.Bold);
    private static readonly XFont Body = new("Arial", 8, XFontStyleEx.Regular);
    private static readonly XFont Small = new("Arial", 6.6, XFontStyleEx.Regular);

    public static int ExportMidi(MidiScore score, string output, double startSeconds = 0,
        double? endSeconds = null)
    {
        var end = endSeconds ?? score.DurationSeconds;
        if (startSeconds < 0 || end <= startSeconds || startSeconds >= score.DurationSeconds)
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "Choose a valid start and end within the MIDI file.");

        var allSteps = score.Notes.GroupBy(n => n.StartTick).OrderBy(g => g.Key)
            .Select((g, i) => new MidiStep(i + 1, g.Key, g.First().StartSeconds, g.ToList()))
            .ToList();
        var steps = allSteps.Where(s => s.Seconds >= startSeconds - 0.000001 &&
            s.Seconds <= end + 0.000001).ToList();
        if (steps.Count == 0) throw new InvalidOperationException("No key presses occur in the selected time range.");

        using var document = NewDocument($"{score.Title} - piano guide");
        var minPitch = score.Notes.Min(n => n.Pitch);
        var maxPitch = score.Notes.Max(n => n.Pitch);
        var keyboardStart = Math.Max(21, minPitch / 12 * 12);
        var keyboardEnd = Math.Min(108, maxPitch / 12 * 12 + 11);
        if (keyboardEnd < maxPitch) keyboardEnd = 108;
        var totalPages = (steps.Count + 5) / 6;

        for (var pageNumber = 0; pageNumber < totalPages; pageNumber++)
        {
            var page = AddPage(document);
            using var gfx = XGraphics.FromPdfPage(page);
            DrawPageHeader(gfx, score.Title,
                $"MIDI practice guide  |  {TimeText.Format(startSeconds)} - {TimeText.Format(end)}",
                pageNumber + 1, totalPages);
            Text(gfx, "BLUE  Left hand     ORANGE  Right hand     PURPLE  Unassigned     Pale keys are still held",
                Small, Muted, 32, 28, 780, 12);

            for (var slot = 0; slot < 6; slot++)
            {
                var position = pageNumber * 6 + slot;
                if (position >= steps.Count) break;
                var step = steps[position];
                var next = allSteps.FirstOrDefault(s => s.Number == step.Number + 1);
                DrawMidiStep(gfx, score, step, next, 32, 43 + slot * 86,
                    PageWidth - 64, 82, keyboardStart, keyboardEnd);
            }
        }
        SaveAtomically(document, output);
        return steps.Count;
    }

    public static void ExportVideo(string sourcePath, IReadOnlyList<(double Seconds, string ImagePath)> frames,
        string output)
    {
        if (frames.Count == 0) throw new InvalidOperationException("Add at least one video frame before exporting.");
        using var document = NewDocument($"{Path.GetFileNameWithoutExtension(sourcePath)} - selected frames");
        var ordered = frames.OrderBy(f => f.Seconds).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var page = AddPage(document);
            using var gfx = XGraphics.FromPdfPage(page);
            DrawPageHeader(gfx, Path.GetFileNameWithoutExtension(sourcePath), "Selected video moments",
                i + 1, ordered.Count);
            Text(gfx, $"FRAME {i + 1:000}  |  {TimeText.Format(ordered[i].Seconds)}",
                Heading, Ink, 32, 45, 770, 20);
            using var image = XImage.FromFile(ordered[i].ImagePath);
            var maxWidth = PageWidth - 64;
            var maxHeight = PageHeight - 105;
            var scale = Math.Min(maxWidth / image.PixelWidth, maxHeight / image.PixelHeight);
            var width = image.PixelWidth * scale;
            var height = image.PixelHeight * scale;
            var x = (PageWidth - width) / 2;
            var y = 75 + (maxHeight - height) / 2;
            gfx.DrawRectangle(new XPen(Rule, 0.8), x - 1, y - 1, width + 2, height + 2);
            gfx.DrawImage(image, x, y, width, height);
        }
        SaveAtomically(document, output);
    }

    private static void DrawMidiStep(XGraphics gfx, MidiScore score, MidiStep step, MidiStep? next,
        double x, double y, double width, double height, int keyboardStart, int keyboardEnd)
    {
        gfx.DrawRectangle(new XPen(Rule, 0.7), x, y, width, height);
        Text(gfx, $"STEP {step.Number:000}   {TimeText.Format(step.Seconds)}", Heading, Ink,
            x + 7, y + 3, 350, 14);
        var wait = next == null ? "Last press" :
            $"Next in {Number((next.Tick - step.Tick) / (double)score.TicksPerQuarterNote)} beats  /  {Number(next.Seconds - step.Seconds, "0.###")} s";
        Text(gfx, wait, Body, Muted, x + width - 245, y + 4, 237, 12, XStringFormats.TopRight);

        DrawNoteLine(gfx, score, step, Hand.Left, "LEFT", x + 7, y + 18, width - 14);
        DrawNoteLine(gfx, score, step, Hand.Right, "RIGHT", x + 7, y + 29, width - 14);
        if (step.NewNotes.Any(n => score.HandFor(n.TrackIndex) == Hand.Unassigned))
            DrawNoteLine(gfx, score, step, Hand.Unassigned, "OTHER", x + width / 2, y + 29, width / 2 - 7);

        DrawKeyboard(gfx, score, step, x + 7, y + 42, width - 14, 34, keyboardStart, keyboardEnd);
    }

    private static void DrawNoteLine(XGraphics gfx, MidiScore score, MidiStep step, Hand hand,
        string label, double x, double y, double width)
    {
        var notes = step.NewNotes.Where(n => score.HandFor(n.TrackIndex) == hand).OrderBy(n => n.Pitch)
            .Select(n => $"{PitchName(n.Pitch)} ({Number((n.EndTick - n.StartTick) / (double)score.TicksPerQuarterNote)}b)");
        var line = $"{label}:  {string.Join("   ", notes)}";
        if (line == $"{label}:  ") line += "-";
        Text(gfx, line, Body, Ink, x, y, width, 11);
    }

    private static void DrawKeyboard(XGraphics gfx, MidiScore score, MidiStep step, double x, double y,
        double width, double height, int start, int end)
    {
        var white = Enumerable.Range(start, end - start + 1).Where(IsWhite).ToList();
        var whiteWidth = width / white.Count;
        for (var i = 0; i < white.Count; i++)
        {
            var pitch = white[i];
            var fills = KeyFills(score, step, pitch);
            DrawKey(gfx, x + i * whiteWidth, y, whiteWidth, height, fills, false);
            if (pitch % 12 == 0)
                Text(gfx, PitchName(pitch), Small, Ink, x + i * whiteWidth + 1,
                    y + height - 10, whiteWidth - 2, 9, XStringFormats.BottomCenter);
        }
        for (var pitch = start; pitch <= end; pitch++)
        {
            if (IsWhite(pitch)) continue;
            var before = white.FindLastIndex(p => p < pitch);
            if (before < 0 || before >= white.Count - 1) continue;
            var blackWidth = whiteWidth * 0.57;
            var keyX = x + (before + 1) * whiteWidth - blackWidth / 2;
            DrawKey(gfx, keyX, y, blackWidth, height * 0.62,
                KeyFills(score, step, pitch), true);
        }
    }

    private static void DrawKey(XGraphics gfx, double x, double y, double width, double height,
        IReadOnlyList<XColor> fills, bool black)
    {
        if (fills.Count == 0)
            gfx.DrawRectangle(new XPen(black ? XColors.White : Ink, black ? 0.4 : 0.5),
                new XSolidBrush(black ? Ink : XColors.White), x, y, width, height);
        else
        {
            for (var i = 0; i < fills.Count; i++)
                gfx.DrawRectangle(new XSolidBrush(fills[i]), x + i * width / fills.Count, y,
                    width / fills.Count, height);
            gfx.DrawRectangle(new XPen(Ink, 0.55), x, y, width, height);
        }
    }

    private static IReadOnlyList<XColor> KeyFills(MidiScore score, MidiStep step, int pitch)
    {
        var active = score.Notes.Where(n => n.Pitch == pitch && n.StartTick <= step.Tick &&
            n.EndTick > step.Tick).ToList();
        var newNotes = active.Where(n => n.StartTick == step.Tick).ToList();
        var shown = newNotes.Count > 0 ? newNotes : active;
        return shown.Select(n => ColorFor(score.HandFor(n.TrackIndex), newNotes.Count > 0))
            .Distinct().OrderBy(c => c.ToString()).ToList();
    }

    private static XColor ColorFor(Hand hand, bool isNew) => (hand, isNew) switch
    {
        (Hand.Left, true) => LeftNew,
        (Hand.Left, false) => LeftHeld,
        (Hand.Right, true) => RightNew,
        (Hand.Right, false) => RightHeld,
        (Hand.Unassigned, true) => UnassignedNew,
        _ => UnassignedHeld
    };

    private static bool IsWhite(int pitch) => pitch % 12 is 0 or 2 or 4 or 5 or 7 or 9 or 11;
    public static string PitchName(int pitch) =>
        new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" }[pitch % 12]
        + (pitch / 12 - 1);

    private static string Number(double value, string format = "0.##") =>
        value.ToString(format, CultureInfo.InvariantCulture);

    private static PdfDocument NewDocument(string title)
    {
        var document = new PdfDocument();
        document.Info.Title = title;
        document.Info.Creator = "Printable Piano Guide";
        return document;
    }

    private static PdfPage AddPage(PdfDocument document)
    {
        var page = document.AddPage();
        page.Size = PageSize.A4;
        page.Orientation = PageOrientation.Landscape;
        return page;
    }

    private static void DrawPageHeader(XGraphics gfx, string title, string subtitle, int page, int pages)
    {
        Text(gfx, title, Title, Ink, 32, 7, 600, 21);
        Text(gfx, subtitle, Body, Muted, 32, PageHeight - 22, 650, 12);
        Text(gfx, $"{page} / {pages}", Body, Muted, PageWidth - 94,
            PageHeight - 22, 62, 12, XStringFormats.TopRight);
    }

    private static void Text(XGraphics gfx, string value, XFont font, XColor color, double x,
        double y, double width, double height, XStringFormat? format = null) =>
        gfx.DrawString(value, font, new XSolidBrush(color), new XRect(x, y, width, height),
            format ?? XStringFormats.TopLeft);

    private static void SaveAtomically(PdfDocument document, string output)
    {
        var fullPath = Path.GetFullPath(output);
        var directory = Path.GetDirectoryName(fullPath)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Output folder not found: {directory}");
        var temp = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(fullPath)}.{Guid.NewGuid():N}.tmp.pdf");
        try
        {
            document.Save(temp);
            File.Move(temp, fullPath, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private sealed record MidiStep(int Number, long Tick, double Seconds, IReadOnlyList<PianoNote> NewNotes);
}
