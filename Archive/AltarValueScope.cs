using System.Text;
using GgpkColorizer.Text;

namespace GgpkColorizer.Archive;

internal static class AltarValueScope
{
    private const string NegativeCondition = "#|-1";
    private const string PositiveCondition = "1|#";
    private const string Prefix = "metadata/statdescriptions/";
    private static readonly string[] Languages = ["English", "Traditional Chinese"];
    // These reviewed stat families need split branches; other penalties may use positive values.
    // shortcut: current altar semantic list, review when game data adds a mixed-sign altar stat.
    private static readonly ValueScope[] Scopes =
    [
        new("base_fire_damage_resistance_%"),
        new("base_cold_damage_resistance_%"),
        new("base_lightning_damage_resistance_%"),
        new("base_chaos_damage_resistance_%"),
        new("base_additional_physical_damage_reduction_%", ExposePositive: true),
        new("base_physical_damage_reduction_rating", ExposePositive: true),
        new("base_evasion_rating"),
        new("critical_strike_multiplier_+_per_power_charge"),
    ];

    public static bool IsShared(Modifier modifier) => FindScope(modifier) is not null &&
        IsResource(modifier.ResourcePath, "stat_descriptions");

    public static bool IsScoped(Modifier modifier)
    {
        ValueScope? scope = FindScope(modifier);
        return scope is not null &&
            IsResource(modifier.ResourcePath, "primordial_altar_stat_descriptions") &&
            (modifier.Conditions == NegativeCondition ||
                scope.ExposePositive && modifier.Conditions == PositiveCondition);
    }

    public static List<Modifier> Project(List<Modifier> items,
        IReadOnlyDictionary<string, StatDescriptionDocument> documents)
    {
        string sharedPath = documents.Keys.Single(path => IsResource(path, "stat_descriptions"));
        string altarPath = documents.Keys.Single(path =>
            IsResource(path, "primordial_altar_stat_descriptions"));
        StatDescriptionDocument shared = documents[sharedPath];
        StatDescriptionDocument altar = documents[altarPath];
        var projected = new List<Modifier>();
        foreach (ValueScope scope in Scopes)
        {
            BranchValues values = ReadValues(shared, altar, scope);
            string projectedBlock = CreateBlock(shared, scope, values, Languages);
            StatDescriptionDocument projection = ParseBlock(altarPath, projectedBlock);
            foreach (string language in Languages)
            {
                Modifier source = GetSource(shared, scope.StatId, language);
                Modifier negative = GetScoped(
                    projection,
                    scope.StatId,
                    language,
                    NegativeCondition);
                projected.Add(negative with { LegacySharedKey = source.Key });
                if (scope.ExposePositive)
                    projected.Add(GetScoped(
                        projection,
                        scope.StatId,
                        language,
                        PositiveCondition));
            }
        }
        return items.Where(item => !IsShared(item) &&
            !(FindScope(item) is not null &&
                IsResource(item.ResourcePath, "primordial_altar_stat_descriptions")))
            .Concat(projected).ToList();
    }

    public static IReadOnlyList<PatchWriter.Edit> PrepareEdits(GgpkArchive archive,
        string archivePath, IReadOnlyList<PatchWriter.Edit> edits, ChangeJournal journal)
    {
        if (edits.Any(edit => edit.UndoOf is null && IsShared(edit.Modifier)))
            throw new InvalidOperationException(
                "Refresh the local catalog to use altar-only value colors.");
        var scoped = edits.Where(edit => edit.UndoOf is null && IsScoped(edit.Modifier)).ToList();
        var prepared = edits.Except(scoped).ToList();
        if (scoped.Count == 0) return prepared;
        StatDescriptionDocument shared = Read(archive, FindSharedPath(archive));
        foreach (var group in scoped.GroupBy(edit =>
            (edit.Modifier.ResourcePath, StatId: edit.Modifier.StatIds.Single())))
        {
            ValueScope scope = Scopes.Single(item => item.StatId == group.Key.StatId);
            StatDescriptionDocument altar = Read(archive, group.Key.ResourcePath);
            BranchValues values = ReadValues(shared, altar, scope);
            string currentBlock = altar.GetSingleStatBlock(scope.StatId);
            var rules = new List<RuleChange>();
            var inheritedLanguages = new HashSet<string>(StringComparer.Ordinal);
            foreach (string language in Languages)
            {
                Modifier source = GetSource(shared, scope.StatId, language);
                PatchWriter.Edit? restore = RestoreOwnedColor(
                    source,
                    archivePath,
                    journal);
                if (restore is null) continue;
                prepared.Add(restore);
                if (!values.LocalNegativeLanguages.Contains(language))
                {
                    values.Negative[language] = source.Template;
                    inheritedLanguages.Add(language);
                }
            }
            foreach (PatchWriter.Edit edit in group)
            {
                string language = edit.Modifier.Language;
                string condition = edit.Modifier.Conditions;
                string plain = ColorMarkup.Unwrap(
                    GetSource(shared, scope.StatId, language).Template);
                if (ColorMarkup.Unwrap(edit.Expected) != plain ||
                    ColorMarkup.Unwrap(edit.Replacement) != plain)
                    throw new InvalidDataException(
                        "Altar value text changed; refresh the catalog: " + scope.StatId);
                bool replacesInherited = condition == NegativeCondition &&
                    inheritedLanguages.Remove(language);
                string before = replacesInherited
                    ? plain
                    : GetValue(values, language, condition);
                rules.Add(new RuleChange(
                    edit.Modifier.Key,
                    before,
                    edit.Replacement,
                    language,
                    condition));
                SetValue(values, language, condition, edit.Replacement);
            }
            string replacementBlock = CreateBlock(shared, scope, values, Languages);
            StatDescriptionDocument replacement = ParseBlock(
                group.Key.ResourcePath,
                replacementBlock);
            if (inheritedLanguages.Count > 0)
            {
                foreach (string language in Languages.Where(inheritedLanguages.Contains))
                {
                    Modifier source = GetSource(shared, scope.StatId, language);
                    Modifier negative = GetScoped(
                        replacement,
                        scope.StatId,
                        language,
                        NegativeCondition);
                    rules.Add(new RuleChange(
                        negative.Key,
                        ColorMarkup.Unwrap(source.Template),
                        values.Negative[language],
                        language,
                        NegativeCondition));
                }
            }
            var ruleKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RuleChange rule in rules)
            {
                Modifier? recorded = replacement.Find(rule.Key);
                if (!ruleKeys.Add(rule.Key) || recorded is null ||
                    recorded.Language != rule.Language ||
                    recorded.Conditions != rule.Conditions ||
                    recorded.Template != rule.After)
                    throw new InvalidDataException(
                        "Generated altar value rule failed validation: " + rule.Key);
            }
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

    private static string GetValue(BranchValues values, string language, string condition) =>
        condition switch
        {
            NegativeCondition => values.Negative[language],
            PositiveCondition => values.Positive[language],
            _ => throw new InvalidDataException("Unsupported scoped altar condition: " + condition),
        };

    private static void SetValue(
        BranchValues values,
        string language,
        string condition,
        string value)
    {
        switch (condition)
        {
            case NegativeCondition:
                values.Negative[language] = value;
                break;
            case PositiveCondition:
                values.Positive[language] = value;
                break;
            default:
                throw new InvalidDataException("Unsupported scoped altar condition: " + condition);
        }
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
            change.Modifier.Key == source.Key &&
            PatchWriter.SamePath(change.ArchivePath, archivePath)))
        {
            if (change.After != previous || ColorMarkup.Unwrap(change.Before) != plain)
                throw new InvalidDataException(
                    "Shared value color does not match its saved history: " + source.StatIds[0]);
            restored.Add(change.Id);
            previous = change.Before;
            if (previous == plain) break;
        }
        if (restored.Count == 0 || previous != plain)
            throw new InvalidDataException(
                "Cannot safely restore a shared value color without matching history: " +
                source.StatIds[0]);
        return new PatchWriter.Edit(source, source.Template, plain, AllowOurColor: false,
            UndoOf: restored[0], AlsoUndoes: restored.Skip(1).ToArray());
    }

    private static BranchValues ReadValues(StatDescriptionDocument source,
        StatDescriptionDocument altar, ValueScope scope)
    {
        var values = new BranchValues(
            Languages.ToDictionary(
                language => language,
                language => ColorMarkup.Unwrap(
                    GetSource(source, scope.StatId, language).Template)),
            Languages.ToDictionary(
                language => language,
                language => ColorMarkup.Unwrap(
                    GetSource(source, scope.StatId, language).Template)),
            new HashSet<string>(StringComparer.Ordinal));
        string existing = altar.GetSingleStatBlock(scope.StatId);
        if (existing.Length == 0) return values;
        var splitLanguages = new List<string>();
        foreach (string language in Languages)
        {
            string plain = ColorMarkup.Unwrap(GetSource(source, scope.StatId, language).Template);
            Modifier? negative = altar.Modifiers.SingleOrDefault(item =>
                item.Language == language &&
                item.StatIds.SequenceEqual(new[] { scope.StatId }) &&
                item.Conditions == NegativeCondition);
            Modifier? positive = altar.Modifiers.SingleOrDefault(item =>
                item.Language == language &&
                item.StatIds.SequenceEqual(new[] { scope.StatId }) &&
                item.Conditions == PositiveCondition);
            if (negative is null)
            {
                if (positive is not null)
                    throw new InvalidDataException(
                        "The altar value block has a positive branch without a negative branch: " +
                        scope.StatId);
                continue;
            }
            if (ColorMarkup.Unwrap(negative.Template) != plain ||
                positive is not null && ColorMarkup.Unwrap(positive.Template) != plain)
                throw new InvalidDataException("The altar value text has changed: " + scope.StatId);
            if (scope.ExposePositive != (positive is not null))
                throw new InvalidDataException(
                    "The altar value block has an unexpected branch shape: " + scope.StatId);
            values.Negative[language] = negative.Template;
            if (positive is not null) values.Positive[language] = positive.Template;
            values.LocalNegativeLanguages.Add(language);
            splitLanguages.Add(language);
        }
        // The previous version split English only; preserve that exact shape until a write.
        if (splitLanguages.Count == 0 ||
            existing != CreateBlock(source, scope, values, splitLanguages))
            throw new InvalidDataException(
                "The altar value block has changed; preserve it before updating: " + scope.StatId);
        return values;
    }

    private static Modifier GetSource(StatDescriptionDocument document,
        string statId, string language)
    {
        Modifier[] matches = document.Modifiers.Where(item =>
            item.Language == language && item.StatIds.SequenceEqual(new[] { statId })).ToArray();
        if (matches.Length != 1 || matches[0].Conditions != "#")
            throw new InvalidDataException(
                "Expected one unrestricted shared value description: " + statId);
        return matches[0];
    }

    private static Modifier GetScoped(StatDescriptionDocument document,
        string statId, string language, string condition) =>
        document.Modifiers.Single(item => item.Language == language &&
            item.StatIds.SequenceEqual(new[] { statId }) && item.Conditions == condition);

    private static StatDescriptionDocument ParseBlock(string path, string block) =>
        StatDescriptionDocument.Parse(path, Encoding.UTF8.GetBytes(block));

    private static string CreateBlock(StatDescriptionDocument source, ValueScope scope,
        BranchValues values, IEnumerable<string> splitLanguages)
    {
        string block = source.GetSingleStatBlock(scope.StatId);
        foreach (string language in splitLanguages)
        {
            string plain = ColorMarkup.Unwrap(GetSource(source, scope.StatId, language).Template);
            block = ParseBlock(Prefix + "stat_descriptions.csd", block)
                .CreateSignedValueBlock(
                    scope.StatId,
                    values.Negative[language],
                    plain,
                    language,
                    scope.ExposePositive ? values.Positive[language] : null);
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

    private static ValueScope? FindScope(Modifier modifier)
    {
        if (modifier.StatIds.Length != 1) return null;
        return Scopes.SingleOrDefault(scope => scope.StatId == modifier.StatIds[0]);
    }

    private static bool IsResource(string path, string name) =>
        path.Equals(Prefix + name + ".csd", StringComparison.OrdinalIgnoreCase) ||
        path.Equals(Prefix + name + ".txt", StringComparison.OrdinalIgnoreCase);

    private sealed record BranchValues(
        Dictionary<string, string> Negative,
        Dictionary<string, string> Positive,
        HashSet<string> LocalNegativeLanguages);

    private sealed record ValueScope(string StatId, bool ExposePositive = false);
}
