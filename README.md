# Plugin.NotesImporter

A cross-platform .NET 10 library that imports notes from **Apple Notes**, **Google Keep**, and
**Microsoft OneNote** into a single, unified, serializable data object — text, attached images
(embedded as Base64), and created/modified timestamps. Every operation has both a **synchronous**
and an **asynchronous** (`...Async`) entry point.

```
Plugin.NotesImporter/             # the library (MAUI class library, net10.0 + platform heads)
Plugin.NotesImporter.Tests/       # xUnit test suite (30 tests)
Plugin.NotesImporter.SampleApp/   # .NET MAUI sample app that drives the library
docs/FORMATS.md            # how each export format is read
```

## Why exports (and a native reader), not live APIs

The three services share no common live API, and live access (OAuth, credentials, platform SDKs)
belongs in the consuming app rather than a shared library. So each importer reads the service's
**official export format**, which keeps the library dependency-light, deterministic and testable.
For Apple there is additionally a **native reader** for the on-device `NoteStore.sqlite` database.
The provider model (`INoteImporter`) lets you add live-API providers later without changing the
data model.

| Source | Importer | Reads |
|---|---|---|
| Google Keep | `GoogleKeepImporter` | A [Google Takeout](https://takeout.google.com) folder: one `.json` per note + attachment files |
| Apple Notes | `AppleNotesImporter` | A folder of notes exported as `.html` (inline or linked images) |
| Apple Notes (native) | `AppleNotesSqliteImporter` | macOS/iOS `NoteStore.sqlite` directly (gzipped-protobuf bodies + Media attachments) |
| Microsoft OneNote | `MicrosoftOneNoteImporter` | A folder of pages exported as `.html` / `.mht` (also matches Graph API page HTML) |

See [docs/FORMATS.md](docs/FORMATS.md) for the details of each format and the Apple SQLite schema.

## The data object

```csharp
public sealed class Note
{
    string                     Id;
    string?                    Title;
    string                     PlainText;     // markup stripped
    string?                    Html;          // original HTML (HTML sources only)
    List<NoteImage>            Images;        // raw bytes; serialize to Base64
    List<string>               Labels;
    DateTimeOffset?            CreatedAt;
    DateTimeOffset?            ModifiedAt;
    NoteSource                 Source;        // AppleNotes | GoogleKeep | MicrosoftOneNote
    bool                       IsPinned, IsArchived, IsTrashed;
    Dictionary<string,string>  Metadata;      // source-specific extras (color, raw ids, ...)
}
```

`Note.ToJson()` / `Note.ToJson(notes)` serialize with images inlined as Base64, so a serialized
note is fully self-contained — no external files needed to round-trip an image. `NoteImage` also
exposes `.Base64` and `.DataUri` helpers and a `NoteImage.FromDataUri(...)` parser.

## Usage

```csharp
using Plugin.NotesImporter;
using Plugin.NotesImporter.Models;

var service = new NotesImportService();

// --- Synchronous ---
IReadOnlyList<Note> keep  = service.ImportGoogleKeep("/path/to/Takeout/Keep");
IReadOnlyList<Note> apple = service.ImportAppleNotes("/path/to/AppleExport");
IReadOnlyList<Note> one   = service.ImportOneNote("/path/to/OneNoteExport");

// --- Asynchronous (real async file / database I/O) ---
var keepAsync = await service.ImportGoogleKeepAsync("/path/to/Takeout/Keep");

// --- Native Apple Notes database ---
var native = await service.ImportAppleNotesDatabaseAsync();          // macOS default location
var native2 = service.ImportAppleNotesDatabase("/path/to/NoteStore.sqlite");

// --- Auto-detect the export type in a folder ---
var notes = await service.ImportAutoAsync("/some/export/folder");

// --- Serialize everything (images embedded) ---
string json = Note.ToJson(notes);
```

### Streaming vs. materializing

`INoteImporter.Import(path)` returns a lazily-streamed `IEnumerable<Note>` (good for large exports
you want to process one note at a time). `ImportAsync(path, ct)` returns a materialized
`IReadOnlyList<Note>` and supports cancellation. The `NotesImportService` convenience methods wrap
both.

## Extending

Implement `INoteImporter` (for example a live `GoogleKeepApiImporter`) and register it:

```csharp
service.Importers[NoteSource.GoogleKeep] = new MyLiveKeepImporter();
```

## Building, testing, running

```bash
# Build the library (portable target)
dotnet build Plugin.NotesImporter/Plugin.NotesImporter.csproj -f net10.0

# Run the unit tests
dotnet test Plugin.NotesImporter.Tests/Plugin.NotesImporter.Tests.csproj

# Run the sample app (pick the target for your machine)
dotnet build Plugin.NotesImporter.SampleApp -f net10.0-maccatalyst
dotnet build Plugin.NotesImporter.SampleApp -f net10.0-windows10.0.19041.0   # on Windows
```

### Windows

The library is platform-neutral (BCL + `Microsoft.Data.Sqlite`, which ships Windows runtimes), so
it builds on Windows. To build its Windows target framework locally on a Windows machine:

```powershell
dotnet workload install maui
dotnet build Plugin.NotesImporter\Plugin.NotesImporter.csproj -f net10.0-windows10.0.19041.0
```

## Requirements

- .NET 10 SDK with the `maui` workload installed (`dotnet workload install maui`).
- The library depends only on `Microsoft.Data.Sqlite` (for the native Apple Notes reader). The
  bundled native SQLite is pinned to a build without advisory GHSA-2m69-gcr7-jv3q.
