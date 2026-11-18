using System.Diagnostics;
using GgpkColorizer.Text;

namespace GgpkColorizer.Archive;

internal sealed class PatchWriter(ChangeJournal journal)
{
    internal sealed record Edit(Modifier Modifier, string Expected, string Replacement,
        bool AllowOurColor, string? UndoOf = null, string? ExpectedBlock = null,
        string? ReplacementBlock = null, IReadOnlyList<string>? AlsoUndoes = null,
        IReadOnlyList<RuleChange>? RuleChanges = null);

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
            cancellationToken.ThrowIfCancellationRequested();
            bool strictScopedBatch = edits.Any(edit =>
                AltarValueScope.IsScoped(edit.Modifier));
            IReadOnlyList<Edit> preparedEdits =
                AltarValueScope.PrepareEdits(
                    archive,
                    ownerPath,
                    edits,
                    journal);
            cancellationToken.ThrowIfCancellationRequested();
            var writes = new List<(string Path, byte[] Bytes)>();
            foreach (var group in preparedEdits.GroupBy(edit => edit.Modifier.ResourcePath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Preparing {Path.GetFileName(group.Key)}");
                byte[]? bytes = archive.Read(group.Key);
                if (bytes is null)
                {
                    if (strictScopedBatch)
                        throw new InvalidDataException($"Missing resource: {group.Key}");
                    notices.Add($"Missing resource: {group.Key}");
                    skipped += group.Count();
                    continue;
                }
                var document = StatDescriptionDocument.Parse(group.Key, bytes);
                string beforeHash = GgpkArchive.Hash(bytes);
                var replacements = new Dictionary<string, string>();
                var blockReplacements = new List<Edit>();
                var resourceChanges = new List<ChangeRecord>();
                foreach (Edit edit in group)
                {
                    if (IsBlockEdit(edit))
                    {
                        string statId = edit.Modifier.StatIds.Single();
                        string currentBlock = document.GetSingleStatBlock(statId);
                        if (currentBlock == edit.ReplacementBlock)
                        {
                            if (edit.UndoOf is not null || edit.AlsoUndoes?.Count > 0)
                            {
                                changes.Add(CreateNoOpChange(
                                    edit,
                                    ownerPath,
                                    batchId,
                                    beforeHash,
                                    edit.ReplacementBlock));
                            }
                            else
                            {
                                skipped++;
                            }
                            continue;
                        }
                        if (currentBlock != edit.ExpectedBlock)
                        {
                            string message = "Local description block changed; refresh the catalog " +
                                $"before changing this modifier: {edit.Modifier.Template}";
                            if (strictScopedBatch) throw new InvalidDataException(message);
                            notices.Add(message);
                            skipped++;
                            continue;
                        }
                        blockReplacements.Add(edit);
                        resourceChanges.Add(CreateChange(
                            edit,
                            ownerPath,
                            batchId,
                            beforeHash));
                        continue;
                    }

                    Modifier? current = document.Find(edit.Modifier.Key);
                    if (current is null)
                    {
                        string message =
                            $"The local modifier was not found: {edit.Modifier.Template}";
                        if (strictScopedBatch) throw new InvalidDataException(message);
                        notices.Add(message);
                        skipped++;
                        continue;
                    }
                    if (current.Template == edit.Replacement)
                    {
                        if (edit.UndoOf is not null || edit.AlsoUndoes?.Count > 0)
                        {
                            changes.Add(CreateNoOpChange(
                                edit with { Modifier = current },
                                ownerPath,
                                batchId,
                                beforeHash));
                        }
                        else skipped++;
                        continue;
                    }
                    bool matches = current.Template == edit.Expected ||
                        (edit.AllowOurColor && ColorMarkup.Unwrap(current.Template) == ColorMarkup.Unwrap(edit.Expected));
                    if (!matches)
                    {
                        string message = "Local text changed; refresh the catalog before " +
                            $"changing this modifier: {edit.Modifier.Template}";
                        if (strictScopedBatch) throw new InvalidDataException(message);
                        notices.Add(message);
                        skipped++;
                        continue;
                    }
                    replacements.Add(current.Key, edit.Replacement);
                    resourceChanges.Add(CreateChange(
                        edit with
                        {
                            Modifier = current,
                            Expected = current.Template,
                        },
                        ownerPath,
                        batchId,
                        beforeHash));
                }
                if (replacements.Count == 0 && blockReplacements.Count == 0) continue;
                byte[] replacementBytes = replacements.Count == 0
                    ? bytes
                    : document.Replace(replacements);
                foreach (Edit edit in blockReplacements)
                {
                    document = StatDescriptionDocument.Parse(group.Key, replacementBytes);
                    replacementBytes = document.ReplaceSingleStatBlock(
                        edit.Modifier.StatIds.Single(),
                        edit.ExpectedBlock!,
                        edit.ReplacementBlock!);
                }
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
                    if (change.BeforeBlock is not null && change.AfterBlock is not null)
                    {
                        string statId = change.Modifier.StatIds.Single();
                        string currentBlock = document.GetSingleStatBlock(statId);
                        change.Status = currentBlock == change.AfterBlock ? "applied"
                            : currentBlock == change.BeforeBlock ? "not-applied"
                            : "conflict";
                        change.Detail = change.Status == "conflict"
                            ? "Current description block matches neither recorded version."
                            : "";
                    }
                    else
                    {
                        string? current = document.Find(change.Modifier.Key)?.Template;
                        change.Status = hash == change.AfterFileHash || current == change.After
                            ? "applied"
                            : hash == change.BeforeFileHash || current == change.Before
                                ? "not-applied"
                                : "conflict";
                        change.Detail = change.Status == "conflict"
                            ? "Current text matches neither recorded version."
                            : "";
                    }
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

    private static bool IsBlockEdit(Edit edit)
    {
        bool hasExpected = edit.ExpectedBlock is not null;
        bool hasReplacement = edit.ReplacementBlock is not null;
        if (hasExpected != hasReplacement)
            throw new InvalidDataException(
                "A description block edit requires both recorded block versions.");
        if (hasExpected && edit.Modifier.StatIds.Length != 1)
            throw new InvalidDataException(
                "A description block edit must target exactly one stat.");
        return hasExpected;
    }

    private static ChangeRecord CreateChange(
        Edit edit,
        string archivePath,
        string batchId,
        string beforeHash) => new()
        {
            BatchId = batchId,
            UndoOf = edit.UndoOf,
            ArchivePath = archivePath,
            Modifier = edit.Modifier,
            Before = edit.Expected,
            After = edit.Replacement,
            BeforeBlock = edit.ExpectedBlock,
            AfterBlock = edit.ReplacementBlock,
            AlsoUndoes = edit.AlsoUndoes?.ToList() ?? [],
            RuleChanges = edit.RuleChanges?.ToList() ?? [],
            BeforeFileHash = beforeHash,
        };

    private static ChangeRecord CreateNoOpChange(
        Edit edit,
        string archivePath,
        string batchId,
        string fileHash,
        string? currentBlock = null) => new()
        {
            BatchId = batchId,
            UndoOf = edit.UndoOf,
            ArchivePath = archivePath,
            Modifier = edit.Modifier,
            Before = edit.Replacement,
            After = edit.Replacement,
            BeforeBlock = currentBlock,
            AfterBlock = currentBlock,
            AlsoUndoes = edit.AlsoUndoes?.ToList() ?? [],
            RuleChanges = edit.RuleChanges?.Select(change =>
                change with { Before = change.After }).ToList() ?? [],
            BeforeFileHash = fileHash,
            AfterFileHash = fileHash,
        };

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
