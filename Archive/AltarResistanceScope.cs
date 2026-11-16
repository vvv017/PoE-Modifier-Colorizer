using System.Text;
using GgpkColorizer.Text;

namespace GgpkColorizer.Archive;

internal static class AltarResistanceScope
{
    private const string Prefix = "metadata/statdescriptions/";
    private static readonly string[] Languages = ["English", "Traditional Chinese"];
    private static readonly string[] StatIds =
    [
        "base_fire_damage_resistance_%",
        "base_cold_damage_resistance_%",
        "base_lightning_damage_resistance_%",
        "base_chaos_damage_resistance_%",
    ];

    public static bool IsShared(Modifier modifier) => IsResistance(modifier) &&
        IsResource(modifier.ResourcePath, "stat_descriptions");

    public static bool IsScoped(Modifier modifier) => IsResistance(modifier) &&
        IsResource(modifier.ResourcePath, "primordial_altar_stat_descriptions") &&
        modifier.Conditions == "#|-1";

    public static List<Modifier> Project(List<Modifier> items,
        IReadOnlyDictionary<string, StatDescriptionDocument> documents)
    {
        string sharedPath = documents.Keys.Single(path => IsResource(path, "stat_descriptions"));
        string altarPath = documents.Keys.Single(path => IsResource(path, "primordial_altar_stat_descriptions"));
        StatDescriptionDocument shared = documents[sharedPath];
        StatDescriptionDocument altar = documents[altarPath];
        var projected = new List<Modifier>();
        foreach (string statId in StatIds)
        {
            var negatives = ReadNegatives(shared, altar, statId);
            string projectedBlock = CreateBlock(shared, statId, negatives, Languages);
            var projection = ParseBlock(altarPath, projectedBlock);
            foreach (string language in Languages)
            {
                Modifier source = GetSource(shared, statId, language);
                Modifier scoped = GetNegative(projection, statId, language);
                projected.Add(scoped with { LegacySharedKey = source.Key });
            }
        }
        return items.Where(item => !IsShared(item) &&
            !(IsResistance(item) && IsResource(item.ResourcePath, "primordial_altar_stat_descriptions")))
            .Concat(projected).ToList();
    }

    public static IReadOnlyList<PatchWriter.Edit> PrepareEdits(GgpkArchive archive,
        string archivePath, IReadOnlyList<PatchWriter.Edit> edits, ChangeJournal journal)
    {
        if (edits.Any(edit => edit.UndoOf is null && IsShared(edit.Modifier)))
            throw new InvalidOperationException("Refresh the local catalog to use altar-only resistance colors.");
        var scoped = edits.Where(edit => edit.UndoOf is null && IsScoped(edit.Modifier)).ToList();
        var prepared = edits.Except(scoped).ToList();
        if (scoped.Count == 0) return prepared;
        var shared = Read(archive, FindSharedPath(archive));
        foreach (var group in scoped.GroupBy(edit =>
            (edit.Modifier.ResourcePath, StatId: edit.Modifier.StatIds.Single())))
        {
            string statId = group.Key.StatId;
            var altar = Read(archive, group.Key.ResourcePath);
            var negatives = ReadNegatives(shared, altar, statId);
            string currentBlock = altar.GetSingleStatBlock(statId);
            var rules = new List<RuleChange>();
            foreach (string language in Languages)
            {
                var restore = RestoreOwnedColor(GetSource(shared, statId, language), archivePath, journal);
                if (restore is not null) prepared.Add(restore);
            }
            foreach (PatchWriter.Edit edit in group)
            {
                string language = edit.Modifier.Language;
                string plain = ColorMarkup.Unwrap(GetSource(shared, statId, language).Template);
                if (ColorMarkup.Unwrap(edit.Expected) != plain ||
                    ColorMarkup.Unwrap(edit.Replacement) != plain)
                    throw new InvalidDataException("Resistance text changed; refresh the catalog: " + statId);
                rules.Add(new RuleChange(edit.Modifier.Key, negatives[language], edit.Replacement));
                negatives[language] = edit.Replacement;
            }
            string replacementBlock = CreateBlock(shared, statId, negatives, Languages);
            prepared.Add(group.First() with
            {
                Expected = rules[0].Before,
                ExpectedBlock = currentBlock,
                ReplacementBlock = replacementBlock,
                AllowOurColor = false,
                RuleChanges = rules,
            });
        }
        return prepared;
    }

    private static PatchWriter.Edit? RestoreOwnedColor(Modifier source,
        string archivePath, ChangeJournal journal)
    {
        string plain = ColorMarkup.Unwrap(source.Template);
        if (plain == source.Template) return null;
        string previous = source.Template;
        var restored = new List<string>();
        foreach (ChangeRecord change in journal.Entries.AsEnumerable().Reverse().Where(change =>
            change.Status == "applied" && change.UndoOf is null &&
            change.Modifier.Key == source.Key && PatchWriter.SamePath(change.ArchivePath, archivePath)))
        {
            if (change.After != previous || ColorMarkup.Unwrap(change.Before) != plain)
                throw new InvalidDataException("Shared resistance color does not match its saved history: " + source.StatIds[0]);
            restored.Add(change.Id);
            previous = change.Before;
            if (previous == plain) break;
        }
        if (restored.Count == 0 || previous != plain)
            throw new InvalidDataException("Cannot safely restore a shared resistance color without matching history: " + source.StatIds[0]);
        return new PatchWriter.Edit(source, source.Template, plain, AllowOurColor: false,
            UndoOf: restored[0], AlsoUndoes: restored.Skip(1).ToArray());
    }

    private static Dictionary<string, string> ReadNegatives(StatDescriptionDocument source,
        StatDescriptionDocument altar, string statId)
    {
        var negatives = Languages.ToDictionary(language => language,
            language => ColorMarkup.Unwrap(GetSource(source, statId, language).Template));
        string existing = altar.GetSingleStatBlock(statId);
        if (existing.Length == 0) return negatives;
        var splitLanguages = new List<string>();
        foreach (string language in Languages)
        {
            Modifier? negative = altar.Modifiers.SingleOrDefault(item => item.Language == language &&
                item.StatIds.SequenceEqual(new[] { statId }) && item.Conditions == "#|-1");
            if (negative is null) continue;
            if (ColorMarkup.Unwrap(negative.Template) != negatives[language])
                throw new InvalidDataException("The altar resistance text has changed: " + statId);
            negatives[language] = negative.Template;
            splitLanguages.Add(language);
        }
        // The previous version split English only; preserve that exact shape until a write.
        if (splitLanguages.Count == 0 ||
            existing != CreateBlock(source, statId, negatives, splitLanguages))
            throw new InvalidDataException("The altar resistance block has changed; preserve it before updating: " + statId);
        return negatives;
    }

    private static Modifier GetSource(StatDescriptionDocument document, string statId, string language)
    {
        Modifier[] matches = document.Modifiers.Where(item =>
            item.Language == language && item.StatIds.SequenceEqual(new[] { statId })).ToArray();
        if (matches.Length != 1 || matches[0].Conditions != "#")
            throw new InvalidDataException("Expected one unrestricted shared resistance description: " + statId);
        return matches[0];
    }

    private static Modifier GetNegative(StatDescriptionDocument document, string statId, string language) =>
        document.Modifiers.Single(item => item.Language == language && item.StatIds.SequenceEqual(new[] { statId }) &&
            item.Conditions == "#|-1");

    private static StatDescriptionDocument ParseBlock(string path, string block) =>
        StatDescriptionDocument.Parse(path, Encoding.UTF8.GetBytes(block));

    private static string CreateBlock(StatDescriptionDocument source, string statId,
        IReadOnlyDictionary<string, string> negatives, IEnumerable<string> splitLanguages)
    {
        string block = source.GetSingleStatBlock(statId);
        foreach (string language in splitLanguages)
        {
            string plain = ColorMarkup.Unwrap(GetSource(source, statId, language).Template);
            block = ParseBlock(Prefix + "stat_descriptions.csd", block)
                .CreateSignedResistanceBlock(statId, negatives[language], plain, language);
        }
        return block;
    }

    private static StatDescriptionDocument Read(GgpkArchive archive, string path) =>
        StatDescriptionDocument.Parse(path, archive.Read(path) ??
            throw new FileNotFoundException("Description file is missing: " + path));

    private static string FindSharedPath(GgpkArchive archive)
    {
        string path = Prefix + "stat_descriptions.csd";
        return archive.Read(path) is not null ? path : Prefix + "stat_descriptions.txt";
    }

    private static bool IsResistance(Modifier modifier) => modifier.StatIds.Length == 1 &&
        StatIds.Contains(modifier.StatIds[0], StringComparer.Ordinal);

    private static bool IsResource(string path, string name) =>
        path.Equals(Prefix + name + ".csd", StringComparison.OrdinalIgnoreCase) ||
        path.Equals(Prefix + name + ".txt", StringComparison.OrdinalIgnoreCase);
}
