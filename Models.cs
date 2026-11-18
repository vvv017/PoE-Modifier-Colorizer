namespace GgpkColorizer;

public sealed record Modifier
{
    public string Language { get; init; } = "English";
    public string Key { get; init; } = "";
    public string ResourcePath { get; init; } = "";
    public string[] StatIds { get; init; } = [];
    public string VariantKey { get; init; } = "";
    public string Conditions { get; init; } = "";
    public string Template { get; init; } = "";
    public string Group { get; init; } = "";
    public string? LegacySharedKey { get; init; }
    public List<AltarUse> AltarUses { get; init; } = [];
}

public sealed record AltarUse
{
    public string ModId { get; init; } = "";
    public string Faction { get; init; } = "";
    public string Target { get; init; } = "";
    public string Side { get; init; } = "";
}

public sealed record GameColor(string Name, string Hex);

public sealed record ModifierCatalog
{
    public string SnapshotId { get; init; } = Guid.NewGuid().ToString("N");
    public string ArchivePath { get; init; } = "";
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<Modifier> Items { get; init; } = [];
    public List<GameColor> Colors { get; init; } = [];
    public Dictionary<string, string> ResourceHashes { get; init; } = [];
    public List<string> Notices { get; init; } = [];
}

public sealed class UserSettings
{
    public string GameLanguage { get; set; } = "Traditional Chinese";
    public bool TraditionalChineseRulesImported { get; set; }
    public string ArchivePath { get; set; } = "";
    public string CodecPath { get; set; } = "";
    public string Language { get; set; } = "zh-TW";
    public Dictionary<string, string> Rules { get; set; } = [];
}

public sealed record ComparisonItem
{
    public string ModifierKey { get; init; } = "";
    public string Status { get; init; } = "unchecked";
    public string LocalText { get; init; } = "";
    public string WebsiteText { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public string Detail { get; init; } = "";
}

public sealed record ComparisonReport
{
    public string SnapshotId { get; init; } = "";
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<ComparisonItem> Items { get; init; } = [];
    public List<string> Notices { get; init; } = [];
}

public sealed class ChangeRecord
{
    public List<RuleChange> RuleChanges { get; set; } = [];
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BatchId { get; set; } = "";
    public string? UndoOf { get; set; }
    public bool RulesSynchronized { get; set; }
    public string ArchivePath { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Modifier Modifier { get; set; } = new();
    public string Before { get; set; } = "";
    public string After { get; set; } = "";
    public string? BeforeBlock { get; set; }
    public string? AfterBlock { get; set; }
    public List<string> AlsoUndoes { get; set; } = [];
    public string BeforeFileHash { get; set; } = "";
    public string AfterFileHash { get; set; } = "";
    public string Status { get; set; } = "prepared";
    public string Detail { get; set; } = "";
}

public sealed record OperationResult(int Applied, int Skipped, List<string> Notices);

public sealed record RuleChange(
    string Key,
    string Before,
    string After,
    string Language = "",
    string Conditions = "");
