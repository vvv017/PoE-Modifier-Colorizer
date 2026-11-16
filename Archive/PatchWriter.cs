using System.Diagnostics;
using GgpkColorizer.Text;

namespace GgpkColorizer.Archive;

internal sealed class PatchWriter(ChangeJournal journal)
{
    internal sealed record Edit(Modifier Modifier, string Expected, string Replacement,
        bool AllowOurColor, string? UndoOf = null);

    public OperationResult Apply(string archivePath, string codecPath, IReadOnlyList<Edit> edits,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        EnsureGameClosed();
        string ownerPath = Path.GetFullPath(archivePath);
        var notices = new List<string>();
        var changes = new List<ChangeRecord>();
        string batchId = Guid.NewGuid().ToString("N");
        int skipped = 0;
        bool journalPrepared = false;
        try
        {
            using var archive = new GgpkArchive(ownerPath, codecPath, writable: true);
            var writes = new List<(string Path, byte[] Bytes)>();
            foreach (var group in edits.GroupBy(edit => edit.Modifier.ResourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Preparing {Path.GetFileName(group.Key)}");
                byte[]? bytes = archive.Read(group.Key);
                if (bytes is null)
                {
                    notices.Add($"Missing resource: {group.Key}");
                    skipped += group.Count();
                    continue;
                }
                var document = StatDescriptionDocument.Parse(group.Key, bytes);
                var replacements = new Dictionary<string, string>();
                var resourceChanges = new List<ChangeRecord>();
                foreach (Edit edit in group)
                {
                    Modifier? current = document.Find(edit.Modifier.Key);
                    if (current is null)
                    {
                        notices.Add($"The local modifier was not found: {edit.Modifier.Template}");
                        skipped++;
                        continue;
                    }
                    if (current.Template == edit.Replacement)
                    {
                        if (edit.UndoOf is not null)
                        {
                            string currentHash = GgpkArchive.Hash(bytes);
                            changes.Add(new ChangeRecord
                            {
                                BatchId = batchId,
                                UndoOf = edit.UndoOf,
                                ArchivePath = ownerPath,
                                Modifier = current,
                                Before = current.Template,
                                After = current.Template,
                                BeforeFileHash = currentHash,
                                AfterFileHash = currentHash,
                            });
                        }
                        else skipped++;
                        continue;
                    }
                    bool matches = current.Template == edit.Expected ||
                        (edit.AllowOurColor && ColorMarkup.Unwrap(current.Template) == ColorMarkup.Unwrap(edit.Expected));
                    if (!matches)
                    {
                        notices.Add($"Local text changed; refresh the catalog before changing this modifier: {edit.Modifier.Template}");
                        skipped++;
                        continue;
                    }
                    replacements.Add(current.Key, edit.Replacement);
                    resourceChanges.Add(new ChangeRecord
                    {
                        BatchId = batchId,
                        UndoOf = edit.UndoOf,
                        ArchivePath = ownerPath,
                        Modifier = current,
                        Before = current.Template,
                        After = edit.Replacement,
                        BeforeFileHash = GgpkArchive.Hash(bytes),
                    });
                }
                if (replacements.Count == 0) continue;
                byte[] replacementBytes = document.Replace(replacements);
                string afterHash = GgpkArchive.Hash(replacementBytes);
                foreach (var change in resourceChanges) change.AfterFileHash = afterHash;
                changes.AddRange(resourceChanges);
                writes.Add((group.Key, replacementBytes));
            }
            cancellationToken.ThrowIfCancellationRequested();
            journal.Entries.AddRange(changes);
            journal.Save();
            journalPrepared = true;
            // Once writing starts, finish or reconcile it; cancellation is only safe before this point.
            foreach (var write in writes)
            {
                progress?.Report($"Writing {Path.GetFileName(write.Path)}");
                archive.Write(write.Path, write.Bytes);
            }
            if (writes.Count > 0) archive.Save();
        }
        catch (OperationCanceledException) when (changes.All(change => !journal.Entries.Contains(change)))
        {
            throw;
        }
        catch (Exception) when (!journalPrepared)
        {
            throw;
        }
        catch (Exception exception)
        {
            notices.Add(exception.Message);
            foreach (ChangeRecord change in changes)
            {
                change.Status = "unknown";
                change.Detail = "Write interrupted; the current archive must be read to determine the result.";
            }
        }

        if (changes.Count > 0)
        {
            progress?.Report("Checking written contents");
            Reconcile(ownerPath, codecPath, changes, notices);
        }
        journal.Save();
        int applied = changes.Count(change => change.Status == "applied");
        int unresolved = changes.Count(change => change.Status != "applied");
        return new OperationResult(applied, skipped + unresolved, notices);
    }

    public void ReconcilePending(string archivePath, string codecPath, List<string> notices)
    {
        var pending = journal.Entries.Where(change =>
            SamePath(change.ArchivePath, archivePath) && change.Status is "prepared" or "unknown").ToList();
        if (pending.Count == 0) return;
        Reconcile(archivePath, codecPath, pending, notices);
        journal.Save();
    }

    private void Reconcile(string path, string codecPath, List<ChangeRecord> changes, List<string> notices)
    {
        try
        {
            using var archive = new GgpkArchive(path, codecPath);
            foreach (var group in changes.GroupBy(change => change.Modifier.ResourcePath))
            {
                byte[]? bytes = archive.Read(group.Key);
                if (bytes is null)
                {
                    foreach (var change in group)
                    {
                        change.Status = "conflict";
                        change.Detail = "The resource no longer exists.";
                    }
                    continue;
                }
                string hash = GgpkArchive.Hash(bytes);
                var document = StatDescriptionDocument.Parse(group.Key, bytes);
                foreach (ChangeRecord change in group)
                {
                    string? current = document.Find(change.Modifier.Key)?.Template;
                    change.Status = hash == change.AfterFileHash || current == change.After ? "applied"
                        : hash == change.BeforeFileHash || current == change.Before ? "not-applied"
                        : "conflict";
                    change.Detail = change.Status == "conflict" ? "Current text matches neither recorded version." : "";
                    journal.MarkUndoCompleted(change);
                }
            }
        }
        catch (Exception exception)
        {
            notices.Add("Could not verify the archive: " + exception.Message);
            foreach (var change in changes.Where(item => item.Status == "prepared"))
                change.Status = "unknown";
        }
    }

    public static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static void EnsureGameClosed()
    {
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Close Path of Exile and its launcher before applying or undoing changes.");
            }
        }
    }
}
