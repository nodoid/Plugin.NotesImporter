# Export formats

How each importer reads its source, and how to produce the export.

## Google Keep — `GoogleKeepImporter`

**Get the export:** [Google Takeout](https://takeout.google.com) → select *Keep* → download. The
archive's `Takeout/Keep` folder contains one `.json` file per note plus the referenced attachment
files (images, audio) alongside them.

**Note JSON shape (relevant fields):**

```jsonc
{
  "title": "Shopping list",
  "textContent": "Remember the milk",          // free-text body
  "listContent": [                              // OR a checklist
    { "text": "Bread", "isChecked": false },
    { "text": "Eggs",  "isChecked": true }
  ],
  "labels": [ { "name": "Errands" } ],
  "attachments": [ { "filePath": "photo.png", "mimetype": "image/png" } ],
  "color": "RED",
  "isPinned": true, "isArchived": false, "isTrashed": false,
  "createdTimestampUsec": 1552332433953000,     // microseconds since Unix epoch
  "userEditedTimestampUsec": 1552332530467000
}
```

**Mapping:** `textContent` and `listContent` are rendered into `PlainText` (checklist items become
`[ ]` / `[x]` lines). `labels` → `Labels`. `attachments` are read from disk and embedded as bytes;
a missing attachment file is recorded under `Metadata["missingAttachment:<path>"]`. `color` (when
not `DEFAULT`) is preserved in `Metadata`. Timestamps are microseconds and become `CreatedAt` /
`ModifiedAt`.

## Apple Notes (HTML) — `AppleNotesImporter`

**Get the export:** Apple Notes has no built-in bulk export. Notes are typically exported to a
folder of `.html` files (one per note) via the macOS share sheet or a third-party exporter. Images
are either inlined as `data:` URIs or saved as files referenced from the HTML.

**Mapping:** title from `<title>` or the first heading; `PlainText` from the stripped body (block
elements become line breaks); the original markup is kept in `Html`. `data:` image URIs are decoded
inline; relative image `src`s are read from disk next to the HTML; remote (`http(s)`) images are
skipped. Dates come from a `<meta name="created">` / `<meta name="modified">` element when present,
otherwise from the file's creation/modification timestamps.

## Apple Notes (native database) — `AppleNotesSqliteImporter`

**Source:** the on-device Core Data SQLite store. On macOS:

```
~/Library/Group Containers/group.com.apple.notes/NoteStore.sqlite
```

Pass `null` to use that default path, or an explicit path to the `.sqlite` file or its folder.

**How it works:**

- Notes live in `ZICCLOUDSYNCINGOBJECT`; the body is in `ZICNOTEDATA.ZDATA`, joined via
  `ZICCLOUDSYNCINGOBJECT.ZNOTEDATA → ZICNOTEDATA.Z_PK`.
- Column names drift across OS versions (`ZTITLE1` vs `ZTITLE`, `ZCREATIONDATE1/2/3`, …), so the
  schema is probed at runtime (`NoteSchema.Discover`) and the query is built from whatever columns
  exist.
- `ZDATA` is **gzip-compressed protobuf**. The plain text is at
  `NoteStoreProto.document(2).note(3).note_text(2)`; `AppleNotesProto` gunzips the blob and walks
  those three nested length-delimited fields to recover the UTF-8 text — no protobuf dependency.
- Dates are Core Data timestamps (seconds since `2001-01-01 UTC`).
- **Attachments (best-effort):** media rows are resolved to files under
  `Accounts/<accountUuid>/Media/<mediaUuid>/<filename>` next to the database and embedded as bytes.
  If the schema lacks the needed columns, attachments are skipped rather than failing the import.

The database is opened **read-only**.

## Microsoft OneNote — `MicrosoftOneNoteImporter`

**Get the export:** in OneNote, *File → Export* a page as `.html`, or save the HTML a Microsoft
Graph `pages/{id}/content` call returns. Either way you get one HTML file per page; `.mht` / `.mhtml`
are also accepted.

**Mapping:** identical to the Apple Notes HTML importer, but notes are tagged
`Source = MicrosoftOneNote`.

## Unified output

Every importer produces `Plugin.NotesImporter.Models.Note` objects. Serialize with `Note.ToJson(notes)` —
images are embedded as Base64 so the JSON is fully self-contained.
