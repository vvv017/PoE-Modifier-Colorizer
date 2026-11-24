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
        if (undo.Status != "applied") return;
        var restoredIds = undo.AlsoUndoes.ToHashSet(StringComparer.Ordinal);
        if (undo.UndoOf is not null) restoredIds.Add(undo.UndoOf);
        foreach (ChangeRecord original in Entries.Where(item => restoredIds.Contains(item.Id)))
        {
            // A linked undo can confirm that a failed companion never changed the file.
            if (original.Status == "applied") original.Status = "undone";
        }
    }
}
