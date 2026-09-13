using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using System.IO;

namespace PianoGuide;

public static class MidiReader
{
    public static MidiScore Read(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("MIDI file not found.", path);
        var file = MidiFile.Read(path);
        if (file.TimeDivision is not TicksPerQuarterNoteTimeDivision division)
            throw new NotSupportedException("This MIDI file uses SMPTE timing; beat labels require ticks-per-quarter-note timing.");

        var tempoMap = file.GetTempoMap();
        var title = file.GetTrackChunks().SelectMany(c => c.Events)
            .OfType<SequenceTrackNameEvent>().Select(e => e.Text?.Trim())
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))
            ?? Path.GetFileNameWithoutExtension(path);
        var tracks = new List<MidiTrackInfo>();
        var notes = new List<PianoNote>();
        var index = 0;
        foreach (var chunk in file.GetTrackChunks())
        {
            var trackNotes = chunk.GetNotes().ToList();
            if (trackNotes.Count > 0)
            {
                var name = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text;
                name = string.IsNullOrWhiteSpace(name) ? $"Track {index + 1}" : name.Trim();
                var hand = name.Contains("right", StringComparison.OrdinalIgnoreCase) ? Hand.Right
                    : name.Contains("left", StringComparison.OrdinalIgnoreCase) ? Hand.Left : Hand.Unassigned;
                tracks.Add(new MidiTrackInfo { Index = index, Name = name, NoteCount = trackNotes.Count, Hand = hand });
                foreach (var note in trackNotes)
                {
                    double ToSeconds(long tick) => TimeConverter.ConvertTo<MetricTimeSpan>(tick, tempoMap).TotalMicroseconds / 1_000_000.0;
                    notes.Add(new PianoNote(note.NoteNumber, note.Time, note.EndTime,
                        ToSeconds(note.Time), ToSeconds(note.EndTime), index));
                }
            }
            index++;
        }

        if (notes.Count == 0) throw new InvalidDataException("The MIDI file contains no piano notes.");
        return new MidiScore
        {
            SourcePath = path,
            Title = title,
            TicksPerQuarterNote = division.TicksPerQuarterNote,
            Tracks = tracks,
            Notes = notes.OrderBy(n => n.StartTick).ThenBy(n => n.Pitch).ToList()
        };
    }
}
