namespace GgpkColorizer.Archive;

internal sealed class ChangeJournal
{
    private readonly AppStorage storage;
    public List<ChangeRecord> Entries { get; }

    public ChangeJournal(AppStorage storage)
    {
        this.storage = storage;
        Entries = storage.Read<List<ChangeRecord>>("changes.json") ?? [];
    }

    public void Save() => storage.Save("changes.json", Entries);

    public void MarkUndoCompleted(ChangeRecord undo)
    {
        if (undo.UndoOf is null || undo.Status != "applied") return;
        ChangeRecord? original = Entries.FirstOrDefault(item => item.Id == undo.UndoOf);
        if (original is not null) original.Status = "undone";
    }
}
