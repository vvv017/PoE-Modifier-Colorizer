using System.Globalization;

namespace GgpkColorizer.Archive;

internal static class AltarCatalog
{
    public static List<Modifier> AttachUses(IReadOnlyList<Modifier> modifiers,
        IReadOnlyList<AltarChoice> choices, List<string> notices)
    {
        var byStat = choices.SelectMany(choice => choice.Stats.Keys.Select(stat => (stat, choice)))
            .GroupBy(item => item.stat, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.choice).ToArray(),
                StringComparer.Ordinal);
        var result = new List<Modifier>(modifiers.Count);
        foreach (Modifier modifier in modifiers)
        {
            // Altars resolve their own description file and its include, not the map-only file.
            if (modifier.Group == "map")
            {
                result.Add(modifier);
                continue;
            }
            var candidates = modifier.StatIds.Where(byStat.ContainsKey)
                .SelectMany(stat => byStat[stat]).Distinct();
            var uses = candidates.Where(choice => Applies(modifier, choice))
                .Select(choice => choice.Use).Distinct().ToList();
            result.Add(modifier with { AltarUses = uses });
        }

        // The altar file overrides shared translations for the same stat group.
        var dedicated = result.Where(item => item.Group == "altar")
            .SelectMany(item => item.AltarUses.Select(use => (use.ModId, item.Language, Stats: StatGroup(item))))
            .ToHashSet();
        result = result.Select(item => item.Group != "shared" ? item : item with
        {
            AltarUses = item.AltarUses.Where(use => !dedicated.Contains((use.ModId, item.Language, StatGroup(item)))).ToList(),
        }).ToList();

        foreach (var language in result.GroupBy(item => item.Language))
        {
            var covered = language.SelectMany(item => item.AltarUses).Select(use => use.ModId)
                .ToHashSet(StringComparer.Ordinal);
            var missing = choices.Where(choice => !covered.Contains(choice.Use.ModId)).ToList();
            notices.Add($"Altar source ({language.Key}): {covered.Count}/{choices.Count} local choices have matching descriptions.");
            if (missing.Count > 0)
                notices.Add($"Altar choices without matching descriptions ({language.Key}): " +
                    string.Join(", ", missing.Select(choice => choice.Use.ModId)));
        }
        return result;
    }

    private static string StatGroup(Modifier modifier) => string.Join('\n', modifier.StatIds);

    private static bool Applies(Modifier modifier, AltarChoice choice)
    {
        string[] conditions = modifier.Conditions.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (conditions.Length != modifier.StatIds.Length)
            throw new InvalidDataException("Unsupported altar description conditions: " + modifier.Conditions);
        for (int i = 0; i < conditions.Length; i++)
        {
            // Description helper stats absent from a mod have the neutral value zero.
            var range = choice.Stats.TryGetValue(modifier.StatIds[i], out var value) ? value : (Min: 0, Max: 0);
            if (!Overlaps(conditions[i], range.Min, range.Max)) return false;
        }
        return true;
    }

    private static bool Overlaps(string condition, int minimum, int maximum)
    {
        if (condition == "#") return true;
        string[] bounds = condition.Split('|');
        if (bounds.Length is < 1 or > 2)
            throw new InvalidDataException("Unsupported stat condition: " + condition);
        int lower = ParseBound(bounds[0], int.MinValue);
        int upper = bounds.Length == 1 ? lower : ParseBound(bounds[1], int.MaxValue);
        if (lower > upper) throw new InvalidDataException("Reversed stat condition: " + condition);
        return minimum <= upper && maximum >= lower;
    }

    private static int ParseBound(string value, int unbounded) => value == "#" ? unbounded :
        int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int number)
            ? number
            : throw new InvalidDataException("Unsupported stat range bound: " + value);
}
