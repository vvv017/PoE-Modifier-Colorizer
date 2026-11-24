namespace GgpkColorizer.Archive;

internal static class ColorRemoval
{
    public static string RestorePlainText(
        Modifier current,
        string expected,
        string archivePath,
        ChangeJournal journal)
    {
        string plain = ColorMarkup.Unwrap(expected);
        if (ColorMarkup.Unwrap(current.Template) != plain)
            throw new InvalidDataException(
                "The local text changed; refresh the catalog before clearing: " +
                current.Template);
        if (current.Template == plain) return plain;
        if (!ColorMarkup.Palette.Any(color => current.Template.StartsWith(
            "<colour:" + color.Name + ">{{", StringComparison.Ordinal)))
            throw new InvalidDataException(
                "This color is not supported by the app; it was not cleared: " +
                current.Template);

        string previous = current.Template;
        foreach (ChangeRecord change in journal.Entries.AsEnumerable().Reverse())
        {
            // Successful undos and their originals both belong to the recorded timeline.
            if (change.Status is not ("applied" or "undone") ||
                !PatchWriter.SamePath(change.ArchivePath, archivePath))
                continue;
            RuleChange? rule = change.RuleChanges.SingleOrDefault(
                rule => rule.Key == current.Key);
            if (rule is null)
            {
                if (change.RuleChanges.Count != 0 ||
                    change.Modifier.Key != current.Key)
                    continue;
                rule = new RuleChange(current.Key, change.Before, change.After);
            }
            if (rule.After != previous || ColorMarkup.Unwrap(rule.Before) != plain)
                throw new InvalidDataException(
                    "The color does not match its saved history; it was not cleared: " +
                    current.Template);
            previous = rule.Before;
            if (previous == plain) return plain;
        }
        throw new InvalidDataException(
            "Cannot clear this color without matching saved history. " +
            "Use the data folder from the installation that applied it: " +
            current.Template);
    }
}
