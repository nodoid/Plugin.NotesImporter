# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-06-26

### Added
- Initial release.
- Unified, serializable `Note` model (text, Base64-embedded images, created/modified timestamps).
- Importers via the `INoteImporter` provider model:
  - `GoogleKeepImporter` — Google Takeout JSON exports with attachments.
  - `AppleNotesImporter` — Apple Notes exported as HTML (inline or linked images).
  - `AppleNotesSqliteImporter` — native macOS/iOS `NoteStore.sqlite` reader (gunzip + protobuf
    body decode, plus best-effort Media attachment extraction).
  - `MicrosoftOneNoteImporter` — OneNote pages exported as HTML/MHT.
- `NotesImportService` facade with source auto-detection.
- Synchronous and asynchronous (`...Async`, with cancellation) APIs throughout.
- xUnit test suite (30 tests).
- .NET MAUI sample app demonstrating import and inline image rendering.
- Documentation (`README.md`, `docs/FORMATS.md`).

[1.0.0]: https://github.com/nodoid/Plugin.NotesImporter/releases/tag/v1.0.0
