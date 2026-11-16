using GgpkColorizer.Archive;
using GgpkColorizer.Comparison;

namespace GgpkColorizer;

public sealed class AppController
{
    private readonly AppStorage storage;
    private readonly ChangeJournal journal;
    private readonly SemaphoreSlim archiveGate = new(1, 1);
    public UserSettings Settings { get; }
    public ModifierCatalog? Catalog { get; private set; }
    public ComparisonReport? Comparison { get; private set; }
    public IReadOnlyList<ChangeRecord> History => journal.Entries;
    public string DataDirectory { get; }

    public AppController(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data");
        storage = new AppStorage(DataDirectory);
        Settings = storage.Read<UserSettings>("settings.json") ?? new UserSettings();
        Catalog = storage.Read<ModifierCatalog>("catalog.json");
        Comparison = storage.Read<ComparisonReport>("comparison.json");
        journal = new ChangeJournal(storage);
        if (string.IsNullOrWhiteSpace(Settings.ArchivePath))
            Settings.ArchivePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Grinding Gear Games", "Path of Exile", "Content.ggpk");
        SynchronizeUndoRules();
        MigrateResistanceRules(Catalog);
        ImportTraditionalChineseRules(Catalog);
    }

    public void SaveSettings() => storage.Save("settings.json", Settings);

    public void SetRule(string key, string? colorName)
    {
        if (string.IsNullOrEmpty(colorName)) Settings.Rules.Remove(key);
        else
        {
            if (!ColorMarkup.Palette.Any(color => color.Name == colorName))
                throw new ArgumentException("Choose a supported game color.", nameof(colorName));
            Settings.Rules[key] = colorName;
        }
    }

    public async Task<ModifierCatalog> ImportAsync(string archivePath, IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(archivePath);
        string codec = Settings.CodecPath;
        await archiveGate.WaitAsync(cancellationToken);
        try
        {
            ModifierCatalog catalog = await Task.Run(() =>
            {
                var result = CatalogReader.Read(path, codec, progress, cancellationToken);
                new PatchWriter(journal).ReconcilePending(path, codec, result.Notices);
                return result;
            }, cancellationToken);
            SynchronizeUndoRules();
            storage.Save("catalog.json", catalog);
            Catalog = catalog;
            MigrateResistanceRules(catalog);
            ImportTraditionalChineseRules(catalog);
            Settings.ArchivePath = path;
            SaveSettings();
            return catalog;
        }
        finally { archiveGate.Release(); }
    }

    public async Task<ComparisonReport> CompareAsync(CancellationToken cancellationToken)
    {
        ModifierCatalog snapshot = Catalog ?? throw new InvalidOperationException("Import the local catalog first.");
        ComparisonReport report = await new PoeDbComparison().CompareAsync(snapshot, cancellationToken);
        if (Catalog?.SnapshotId == report.SnapshotId)
        {
            storage.Save("comparison.json", report);
            Comparison = report;
        }
        return report;
    }

    public async Task<OperationResult> ApplyAsync(IReadOnlyCollection<string> keys,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        ModifierCatalog snapshot = Catalog ?? throw new InvalidOperationException("Import the local catalog first.");
        string path = Settings.ArchivePath;
        string codec = Settings.CodecPath;
        if (!PatchWriter.SamePath(snapshot.ArchivePath, path))
            throw new InvalidOperationException("Update the local catalog for the selected GGPK before applying changes.");
        var selected = keys.ToHashSet(StringComparer.Ordinal);
        var edits = snapshot.Items.Where(item => selected.Contains(item.Key) && Settings.Rules.ContainsKey(item.Key))
            .Select(item => new PatchWriter.Edit(item, item.Template,
                ColorMarkup.Apply(item.Template, Settings.Rules[item.Key]), AllowOurColor: true)).ToList();
        if (edits.Any(edit => AltarResistanceScope.IsShared(edit.Modifier)))
            throw new InvalidOperationException("Refresh the local catalog to use altar-only resistance colors.");
        if (edits.Count == 0) return new OperationResult(0, 0, ["No color rules were selected."]);
        await archiveGate.WaitAsync(cancellationToken);
        try
        {
            var writer = new PatchWriter(journal);
            var notices = new List<string>();
            await Task.Run(() => writer.ReconcilePending(path, codec, notices), cancellationToken);
            SynchronizeUndoRules();
            // A recovered undo can clear a pending color rule before this write.
            edits.RemoveAll(edit => !Settings.Rules.TryGetValue(edit.Modifier.Key, out string? color) ||
                edit.Replacement != ColorMarkup.Apply(edit.Modifier.Template, color));
            if (edits.Count == 0) return new OperationResult(0, keys.Count, notices);
            OperationResult result = await Task.Run(() => writer.Apply(path, codec, edits, progress, cancellationToken));
            SynchronizeUndoRules();
            result.Notices.AddRange(notices);
            return result;
        }
        finally { archiveGate.Release(); }
    }

    public async Task<OperationResult> UndoAsync(IReadOnlyCollection<string> changeIds,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(Settings.ArchivePath);
        string codec = Settings.CodecPath;
        var selectedIds = changeIds.ToHashSet(StringComparer.Ordinal);
        await archiveGate.WaitAsync(cancellationToken);
        try
        {
            var notices = new List<string>();
            var writer = new PatchWriter(journal);
            await Task.Run(() => writer.ReconcilePending(path, codec, notices), cancellationToken);
            SynchronizeUndoRules();
            var applied = journal.Entries.Where(change => change.Status == "applied" && change.UndoOf is null &&
                PatchWriter.SamePath(change.ArchivePath, path)).ToList();
            var latest = applied.GroupBy(UndoTarget)
                .ToDictionary(group => group.Key, group => group.Last().Id);
            var targets = applied.Where(change => selectedIds.Contains(change.Id)).ToList();
            var edits = new List<PatchWriter.Edit>();
            foreach (var change in targets)
            {
                if (latest[UndoTarget(change)] != change.Id)
                {
                    notices.Add("Undo the most recent change to this modifier first: " + change.Modifier.Template);
                    continue;
                }
                edits.Add(new PatchWriter.Edit(change.Modifier, change.After, change.Before,
                    AllowOurColor: false, UndoOf: change.Id,
                    ExpectedBlock: change.AfterBlock, ReplacementBlock: change.BeforeBlock,
                    RuleChanges: change.RuleChanges.Select(rule =>
                        new RuleChange(rule.Key, rule.After, rule.Before)).ToList()));
            }
            OperationResult result = edits.Count == 0 ? new OperationResult(0, targets.Count, [])
                : await Task.Run(() => writer.Apply(path, codec, edits, progress, cancellationToken));
            SynchronizeUndoRules();
            result.Notices.AddRange(notices);
            return result;
        }
        finally { archiveGate.Release(); }
    }

    private void MigrateResistanceRules(ModifierCatalog? catalog)
    {
        if (catalog is null) return;
        bool changed = false;
        foreach (Modifier item in catalog.Items.Where(AltarResistanceScope.IsScoped))
        {
            if (item.LegacySharedKey is not null &&
                Settings.Rules.TryGetValue(item.LegacySharedKey, out string? color))
            {
                Settings.Rules.TryAdd(item.Key, color);
                Settings.Rules.Remove(item.LegacySharedKey);
                changed = true;
            }
        }
        if (changed) SaveSettings();
    }

    private static string UndoTarget(ChangeRecord change) =>
        change.BeforeBlock is not null && change.AfterBlock is not null
            ? change.Modifier.ResourcePath + "\n" + string.Join('\n', change.Modifier.StatIds)
            : change.Modifier.Key;

    private void ImportTraditionalChineseRules(ModifierCatalog? catalog)
    {
        if (catalog is null || Settings.TraditionalChineseRulesImported ||
            !catalog.Items.Any(item => item.Language == "Traditional Chinese")) return;
        var english = catalog.Items.Where(item => item.Language == "English")
            .GroupBy(LanguagePairIdentity).ToDictionary(group => group.Key, group => group.ToList());
        var colors = new Dictionary<string, string>(Settings.Rules);
        var englishItems = english.Values.SelectMany(items => items).ToList();
        var englishKeys = englishItems.Select(item => item.Key).ToHashSet();
        foreach (var saved in Settings.Rules.Where(pair => !englishKeys.Contains(pair.Key)))
        {
            ChangeRecord? recorded = journal.Entries.LastOrDefault(change => change.Modifier.Key == saved.Key &&
                change.Modifier.Language == "English" && change.Status == "applied" && change.UndoOf is null &&
                change.BeforeBlock is null && PatchWriter.SamePath(change.ArchivePath, catalog.ArchivePath));
            if (recorded is null) continue;
            var matches = englishItems.Where(item => item.ResourcePath == recorded.Modifier.ResourcePath &&
                item.StatIds.SequenceEqual(recorded.Modifier.StatIds) && item.Template == recorded.After).ToList();
            // Early catalogs used different keys; live text and the journal must both identify one entry.
            if (matches.Count == 1) colors.TryAdd(matches[0].Key, saved.Value);
        }
        int copied = 0;
        int ambiguous = 0;
        foreach (var group in catalog.Items.Where(item => item.Language == "Traditional Chinese")
            .GroupBy(LanguagePairIdentity))
        {
            if (!english.TryGetValue(group.Key, out var candidates)) continue;
            var configured = candidates.Where(item => colors.ContainsKey(item.Key)).ToList();
            if (configured.Count == 0) continue;
            if (candidates.Count != 1 || group.Count() != 1)
            {
                ambiguous++;
                continue;
            }
            if (Settings.Rules.TryAdd(group.Single().Key, colors[configured[0].Key])) copied++;
        }
        Settings.TraditionalChineseRulesImported = true;
        SaveSettings();
        catalog.Notices.Add($"Copied {copied} existing colors to Traditional Chinese. The two languages now have separate rules; {ambiguous} ambiguous groups were left unchanged.");
    }

    private static string LanguagePairIdentity(Modifier item) =>
        item.ResourcePath + "\n" + string.Join('\n', item.StatIds) + "\n" + item.Conditions;

    private void SynchronizeUndoRules()
    {
        var pending = journal.Entries.Where(change => change.UndoOf is not null &&
            change.Status == "applied" && !change.RulesSynchronized).ToList();
        if (pending.Count == 0) return;
        foreach (ChangeRecord undo in pending)
        {
            if (undo.RuleChanges.Count == 0)
                SetRuleFromTemplate(undo.Modifier.Key, undo.After);
            else
                foreach (RuleChange rule in undo.RuleChanges)
                    SetRuleFromTemplate(rule.Key, rule.After);
        }
        SaveSettings();
        foreach (ChangeRecord undo in pending) undo.RulesSynchronized = true;
        journal.Save();
    }

    private void SetRuleFromTemplate(string key, string template)
    {
        GameColor? color = ColorMarkup.Palette.FirstOrDefault(item =>
            template.StartsWith("<colour:" + item.Name + ">{{", StringComparison.Ordinal));
        SetRule(key, color?.Name);
    }
}
