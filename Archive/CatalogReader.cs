using GgpkColorizer.Text;

namespace GgpkColorizer.Archive;

internal static class CatalogReader
{
    private const string Prefix = "metadata/statdescriptions/";

    public static ModifierCatalog Read(string archivePath, string codecPath,
        IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var archive = new GgpkArchive(archivePath, codecPath);
        var pending = new Queue<string>();
        foreach (string name in new[] { "map_stat_descriptions", "primordial_altar_stat_descriptions" })
        {
            string path = Prefix + name + ".csd";
            if (archive.Read(path) is null) path = Prefix + name + ".txt";
            pending.Enqueue(path);
        }
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var choices = AltarDataReader.Read(archive, hashes);
        var notices = new List<string>();
        var items = new List<Modifier>();
        var documents = new Dictionary<string, StatDescriptionDocument>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out string? resourcePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = resourcePath.Replace('\\', '/').ToLowerInvariant();
            if (!path.StartsWith(Prefix, StringComparison.Ordinal) || path.Contains(".."))
                throw new InvalidDataException($"Unsupported description include: {path}");
            if (!visited.Add(path)) continue;
            progress?.Report($"Reading {Path.GetFileName(path)}");
            byte[] bytes = archive.Read(path)
                ?? throw new FileNotFoundException($"Description file is missing: {path}");
            hashes[path] = GgpkArchive.Hash(bytes);
            var document = StatDescriptionDocument.Parse(path, bytes);
            documents.Add(path, document);
            items.AddRange(document.Modifiers);
            foreach (string include in document.Includes)
                pending.Enqueue(include.Contains('/') || include.Contains('\\') ? include : Prefix + include);
        }
        items = AltarValueScope.Project(items, documents);
        items = AltarCatalog.AttachUses(items, choices, notices)
            .Where(item => item.Group != "shared" || item.AltarUses.Count > 0 ||
                item.StatIds.Any(id => id.StartsWith("map_", StringComparison.Ordinal)))
            .Select(item => item.Group != "shared" ? item : item with
            {
                Group = item.StatIds.Any(id => id.StartsWith("map_", StringComparison.Ordinal)) ? "map" : "altar",
            }).ToList();
        if (items.Count == 0) throw new InvalidDataException("No supported game descriptions were found.");
        notices.Add("Altar faction, target and upside/downside come from local altar choices and their Mod/Stat references. Shared text may appear in other game contexts.");
        notices.Add("The catalog can contain historical, hidden or Ruthless entries. It is not a list of currently rollable map modifiers.");
        notices.Add("Preview colors are approximate. Confirm applied colors in the game.");
        notices.Add("Eight reviewed altar values use dedicated numeric branches. " +
            "Player penalties and enemy Physical Damage Reduction or Armour bonuses have " +
            "independent color rules; zero values stay plain. Shared colors previously " +
            "written by this app are restored when applying these rules.");
        return new ModifierCatalog
        {
            ArchivePath = Path.GetFullPath(archivePath),
            Items = items.OrderBy(item => item.Language).ThenBy(item => item.Group).ThenBy(item => item.Template).ToList(),
            Colors = [.. ColorMarkup.Palette],
            ResourceHashes = hashes,
            Notices = notices,
        };
    }
}
