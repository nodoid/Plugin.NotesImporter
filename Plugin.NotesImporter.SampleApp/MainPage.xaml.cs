using Plugin.NotesImporter;
using Plugin.NotesImporter.Models;

namespace Plugin.NotesImporter.SampleApp;

public partial class MainPage : ContentPage
{
    private readonly NotesImportService _service = new();

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnPickClicked(object? sender, EventArgs e)
    {
        // MAUI has no built-in folder picker; for the sample we let the user pick any file
        // inside the export folder and import that folder. (Add CommunityToolkit.Maui's
        // FolderPicker for a true folder dialog in a real app.)
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Pick any file in the export folder" });
            if (file is null) return;

            string folder = Path.GetDirectoryName(file.FullPath)!;
            await RunImportAsync(folder);
        }
        catch (Exception ex)
        {
            await ShowError(ex);
        }
    }

    private async void OnImportLocalAppleClicked(object? sender, EventArgs e)
    {
        try
        {
            SetBusy("Reading Apple Notes database…");
            var notes = await _service.ImportAppleNotesDatabaseAsync();
            Render(notes, "this Mac's Apple Notes database");
        }
        catch (Exception ex)
        {
            await ShowError(ex);
        }
    }

    private async Task RunImportAsync(string folder)
    {
        SetBusy($"Importing from {folder}…");

        IReadOnlyList<Note> notes = SourcePicker.SelectedIndex switch
        {
            1 => await _service.ImportGoogleKeepAsync(folder),
            2 => await _service.ImportAppleNotesAsync(folder),
            3 => await _service.ImportAppleNotesDatabaseAsync(folder),
            4 => await _service.ImportOneNoteAsync(folder),
            _ => await _service.ImportAutoAsync(folder),
        };

        Render(notes, folder);
    }

    private void Render(IReadOnlyList<Note> notes, string sourceLabel)
    {
        NotesView.ItemsSource = notes.Select(NoteView.From).ToList();
        StatusLabel.Text = $"Imported {notes.Count} note(s) from {sourceLabel}.";
    }

    private void SetBusy(string message)
    {
        StatusLabel.Text = message;
        NotesView.ItemsSource = null;
    }

    private async Task ShowError(Exception ex)
    {
        StatusLabel.Text = "Import failed.";
        await DisplayAlertAsync("Import failed", ex.Message, "OK");
    }

    /// <summary>A small view-model that adapts a <see cref="Note"/> for display.</summary>
    private sealed class NoteView
    {
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string Snippet { get; init; } = "";
        public ImageSource? FirstImage { get; init; }
        public bool HasImage => FirstImage is not null;

        public static NoteView From(Note note)
        {
            var image = note.Images.FirstOrDefault();
            return new NoteView
            {
                Title = string.IsNullOrWhiteSpace(note.Title) ? "(untitled)" : note.Title!,
                Subtitle = $"{note.Source} · {note.CreatedAt?.LocalDateTime.ToString("g") ?? "no date"} · {note.Images.Count} image(s)",
                Snippet = Truncate(note.PlainText, 240),
                // Images are embedded bytes — render straight from memory.
                FirstImage = image is null ? null : ImageSource.FromStream(() => new MemoryStream(image.Data)),
            };
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s[..max] + "…";
    }
}
