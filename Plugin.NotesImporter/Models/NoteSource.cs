namespace Plugin.NotesImporter.Models;

/// <summary>
/// Identifies which note-taking service a <see cref="Note"/> was imported from.
/// </summary>
public enum NoteSource
{
    Unknown = 0,

    /// <summary>Apple Notes (iOS / macOS), imported from exported HTML.</summary>
    AppleNotes,

    /// <summary>Google Keep, imported from a Google Takeout export.</summary>
    GoogleKeep,

    /// <summary>Microsoft OneNote / Sticky Notes, imported from exported HTML.</summary>
    MicrosoftOneNote,
}
