using PdfSharp.Pdf.IO;
using PianoGuide;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", ".."));
            var midiPath = Directory.GetFiles(Path.Combine(root, "_sampleMidi"), "*.mid").Single();
            var videoPath = Directory.GetFiles(Path.Combine(root, "_sampleVideo"), "*.mp4").Single();
            var output = Path.Combine(root, "output", "pdf");
            Directory.CreateDirectory(output);

            var score = MidiReader.Read(midiPath);
            Require(score.Notes.Count == 327, "Sample MIDI note count");
            Require(score.StepCount == 205, "Simultaneous notes group into 205 steps");
            Require(score.Tracks.Any(t => t.Hand == Hand.Left), "Left hand detection");
            Require(score.Tracks.Any(t => t.Hand == Hand.Right), "Right hand detection");
            var rightTrack = score.Tracks.Single(t => t.Hand == Hand.Right);
            rightTrack.Hand = Hand.Unassigned;
            Require(score.HandFor(rightTrack.Index) == Hand.Unassigned, "Hand assignment can be corrected");
            rightTrack.Hand = Hand.Right;
            Require(PdfExports.PitchName(60) == "C4", "Middle C label");
            Require(Math.Abs(TimeText.Parse("00:12.500") - 12.5) < 0.0001, "Time parsing");

            var midiPdf = Path.Combine(output, "chopin-prelude-printable-keys.pdf");
            var count = PdfExports.ExportMidi(score, midiPdf);
            Require(count == 205, "Full MIDI export step count");
            Require(PdfReader.Open(midiPdf, PdfDocumentOpenMode.Import).PageCount == 35,
                "Six MIDI steps per page");

            var sectionPdf = Path.Combine(output, "chopin-prelude-practice-section.pdf");
            var sectionCount = PdfExports.ExportMidi(score, sectionPdf, 5, 10);
            Require(sectionCount > 0 && sectionCount < count, "Section filters steps");
            Require(PdfReader.Open(sectionPdf, PdfDocumentOpenMode.Import).PageCount ==
                (sectionCount + 5) / 6, "Section PDF page count");
            ExpectFailure<ArgumentException>(() => PdfExports.ExportMidi(score, sectionPdf, 20, 10),
                "Invalid MIDI range");
            ExpectFailure<InvalidOperationException>(() => PdfExports.ExportMidi(score, sectionPdf, 28, 29),
                "Range without note onsets");
            ExpectFailure<DirectoryNotFoundException>(() => PdfExports.ExportMidi(score,
                Path.Combine(root, "missing-output-folder", "out.pdf")), "Missing output folder");
            ExpectFailure<ArgumentException>(() => TimeText.Parse("oops"), "Invalid timestamp");

            var info = VideoTools.ProbeAsync(videoPath).GetAwaiter().GetResult();
            Require(info.Width == 1920 && info.Height == 1080 && info.DurationSeconds > 64,
                "Video metadata");
            if (args.Contains("--media")) ProbeWindowsPlayback(videoPath);
            ExpectFailure<InvalidOperationException>(() => VideoTools.ExportAsync(videoPath, [],
                CropRect.Full, Path.Combine(output, "invalid.pdf")).GetAwaiter().GetResult(),
                "Empty video selection");
            ExpectFailure<ArgumentException>(() => VideoTools.ExportAsync(videoPath,
                [info.DurationSeconds + 1], CropRect.Full, Path.Combine(output, "invalid.pdf"))
                .GetAwaiter().GetResult(), "Out-of-range video timestamp");
            var videoPdf = Path.Combine(output, "vague-whispers-selected-frames.pdf");
            VideoTools.ExportAsync(videoPath, [12, 20, 35], new CropRect(0, 0.38, 1, 0.62),
                videoPdf).GetAwaiter().GetResult();
            Require(PdfReader.Open(videoPdf, PdfDocumentOpenMode.Import).PageCount == 3,
                "One selected frame per page");
            Console.WriteLine($"PASS: 205 MIDI steps, {sectionCount} section steps, 3 video frames");
            Console.WriteLine(output);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("Assertion failed: " + description);
    }

    private static void ExpectFailure<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected failure: " + description);
    }

    private static void ProbeWindowsPlayback(string path)
    {
        var player = new MediaPlayer { Volume = 0 };
        try
        {
            var frame = new DispatcherFrame();
            Exception? failed = null;
            var opened = false;
            player.MediaOpened += (_, _) => { opened = true; frame.Continue = false; };
            player.MediaFailed += (_, e) => { failed = e.ErrorException; frame.Continue = false; };
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timeout.Tick += (_, _) => { timeout.Stop(); frame.Continue = false; };
            timeout.Start();
            player.Open(new Uri(path));
            player.Play();
            Dispatcher.PushFrame(frame);
            timeout.Stop();
            if (failed is not null) throw new Exception("Windows video playback failed", failed);
            Require(opened && player.NaturalDuration.HasTimeSpan, "Windows audio/video codec opens");
            player.Position = TimeSpan.FromSeconds(20);
            player.Pause();
            Console.WriteLine("PASS: Windows media playback and seeking opened the sample MP4");
        }
        finally { player.Close(); }
    }
}
