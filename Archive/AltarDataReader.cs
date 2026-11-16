using System.Buffers.Binary;
using System.Text;

namespace GgpkColorizer.Archive;

internal sealed record AltarChoice(
    AltarUse Use,
    IReadOnlyDictionary<string, (int Min, int Max)> Stats);

internal static class AltarDataReader
{
    private const int AltarDomain = 29;
    private const ulong NullKey = 0xFEFEFEFEFEFEFEFEUL;
    private static readonly int[] StatOffsets = [30, 46, 62, 78, 286, 391, 464, 480];
    private static readonly int[] MinOffsets = [126, 134, 142, 150, 278, 383, 456, 496];

    public static List<AltarChoice> Read(
        GgpkArchive archive,
        Dictionary<string, string> hashes)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(hashes);

        // shortcut: fixed PoE 1 schema at dat-schema commit 9f095ca; update these widths when GGG changes a table.
        DatTable choices = ReadTable(archive, hashes,
            "data/atlasprimordialaltarchoices.datc64", 33);
        DatTable choiceTypes = ReadTable(archive, hashes,
            "data/atlasprimordialaltarchoicetypes.datc64", 48);
        DatTable mods = ReadTable(archive, hashes, "data/mods.datc64", 692);
        DatTable stats = ReadTable(archive, hashes, "data/stats.datc64", 105);

        var typeTargets = new Dictionary<int, string>();
        for (int row = 0; row < choiceTypes.RowCount; row++)
        {
            string id = choiceTypes.ReadString(row, 0);
            string target = id switch
            {
                "Player" => "player",
                "InfluencedMonsters" => "minions",
                "MapBoss" => "boss",
                _ => throw new InvalidDataException($"Unknown altar choice type: {id}"),
            };
            typeTargets.Add(row, target);
        }

        var result = new List<AltarChoice>(choices.RowCount);
        for (int row = 0; row < choices.RowCount; row++)
        {
            int modRow = choices.ReadReference(row, 0, mods.RowCount)
                ?? throw new InvalidDataException("An altar choice has no mod.");
            int typeRow = choices.ReadReference(row, 16, choiceTypes.RowCount)
                ?? throw new InvalidDataException("An altar choice has no type.");
            byte choiceFlag = choices.ReadByte(row, 32);
            if (choiceFlag > 1)
                throw new InvalidDataException("An altar choice has an invalid flag.");
            if (mods.ReadInt32(modRow, 94) != AltarDomain)
                throw new InvalidDataException("An altar choice references a non-altar mod.");

            string modId = mods.ReadString(modRow, 0);
            (string faction, string side) = ClassifyMod(modId);
            var modStats = new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal);
            for (int slot = 0; slot < StatOffsets.Length; slot++)
            {
                int? statRow = mods.ReadReference(modRow, StatOffsets[slot], stats.RowCount);
                if (statRow is null) continue;
                string statId = stats.ReadString(statRow.Value, 0);
                int min = mods.ReadInt32(modRow, MinOffsets[slot]);
                int max = mods.ReadInt32(modRow, MinOffsets[slot] + 4);
                if (string.IsNullOrWhiteSpace(statId) || min > max ||
                    !modStats.TryAdd(statId, (min, max)))
                    throw new InvalidDataException($"Invalid stats for altar mod {modId}.");
            }
            if (modStats.Count == 0)
                throw new InvalidDataException($"Altar mod has no stats: {modId}");

            result.Add(new AltarChoice(
                new AltarUse
                {
                    ModId = modId,
                    Faction = faction,
                    Target = typeTargets[typeRow],
                    Side = side,
                },
                modStats));
        }

        ValidateCoverage(result);
        return result;
    }

    private static DatTable ReadTable(
        GgpkArchive archive,
        Dictionary<string, string> hashes,
        string path,
        int rowWidth)
    {
        byte[] bytes = archive.Read(path)
            ?? throw new FileNotFoundException($"Required altar data is missing: {path}");
        hashes[path] = GgpkArchive.Hash(bytes);
        return new DatTable(path, bytes, rowWidth);
    }

    private static (string Faction, string Side) ClassifyMod(string modId)
    {
        string id = modId.StartsWith("HardMode", StringComparison.Ordinal)
            ? modId["HardMode".Length..]
            : modId;
        (string faction, string suffix) = id switch
        {
            _ when id.StartsWith("TangledAltar", StringComparison.Ordinal) =>
                ("eater", id["TangledAltar".Length..]),
            _ when id.StartsWith("TangleAltar", StringComparison.Ordinal) =>
                ("eater", id["TangleAltar".Length..]),
            _ when id.StartsWith("CleansingFireAltar", StringComparison.Ordinal) =>
                ("exarch", id["CleansingFireAltar".Length..]),
            _ when id.StartsWith("CleansingAltar", StringComparison.Ordinal) =>
                ("exarch", id["CleansingAltar".Length..]),
            _ => throw new InvalidDataException($"Unknown altar mod faction: {modId}"),
        };
        string side = suffix.StartsWith("Upside", StringComparison.Ordinal) ? "upside"
            : suffix.StartsWith("Downside", StringComparison.Ordinal) ? "downside"
            : throw new InvalidDataException($"Unknown altar mod side: {modId}");
        return (faction, side);
    }

    private static void ValidateCoverage(IReadOnlyCollection<AltarChoice> choices)
    {
        if (choices.Count == 0 || choices.Any(choice => choice.Stats.Count == 0))
            throw new InvalidDataException("The altar choice data is empty.");
        foreach (string value in new[] { "eater", "exarch" })
            if (!choices.Any(choice => choice.Use.Faction == value))
                throw new InvalidDataException($"Altar faction is missing: {value}");
        foreach (string value in new[] { "player", "minions", "boss" })
            if (!choices.Any(choice => choice.Use.Target == value))
                throw new InvalidDataException($"Altar target is missing: {value}");
        foreach (string value in new[] { "upside", "downside" })
            if (!choices.Any(choice => choice.Use.Side == value))
                throw new InvalidDataException($"Altar side is missing: {value}");
    }

    private sealed class DatTable
    {
        private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
        private readonly string name;
        private readonly byte[] bytes;
        private readonly int rowWidth;
        private readonly int dataOffset;

        public DatTable(string name, byte[] bytes, int rowWidth)
        {
            this.name = name;
            this.bytes = bytes;
            this.rowWidth = rowWidth;
            if (bytes.Length < 12)
                throw Invalid("The file is truncated.");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            long separator = 4L + count * rowWidth;
            if (count == 0 || count > int.MaxValue || separator > bytes.Length - 8)
                throw Invalid("The row section is invalid.");
            RowCount = (int)count;
            dataOffset = (int)separator;
            if (!bytes.AsSpan(dataOffset, 8).SequenceEqual(new byte[]
                { 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB, 0xBB }))
                throw Invalid($"Unexpected row width; expected {rowWidth} bytes.");
        }

        public int RowCount { get; }

        public byte ReadByte(int row, int offset)
        {
            ValidateField(row, offset, 1);
            return bytes[4 + row * rowWidth + offset];
        }

        public int ReadInt32(int row, int offset)
        {
            ValidateField(row, offset, 4);
            return BinaryPrimitives.ReadInt32LittleEndian(
                bytes.AsSpan(4 + row * rowWidth + offset, 4));
        }

        public int? ReadReference(int row, int offset, int targetRows)
        {
            ValidateField(row, offset, 16);
            int position = 4 + row * rowWidth + offset;
            ulong key = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position, 8));
            ulong reserved = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(position + 8, 8));
            if (key is NullKey or ulong.MaxValue)
            {
                if (reserved != key)
                    throw Invalid("A null foreign key is malformed.");
                return null;
            }
            if (reserved != 0 || key >= (ulong)targetRows)
                throw Invalid("A foreign key is outside its target table.");
            return (int)key;
        }

        public string ReadString(int row, int offset)
        {
            ValidateField(row, offset, 8);
            ulong relative = BinaryPrimitives.ReadUInt64LittleEndian(
                bytes.AsSpan(4 + row * rowWidth + offset, 8));
            if (relative < 8 || relative > int.MaxValue || relative % 2 != 0)
                throw Invalid("A string pointer is invalid.");
            long startValue = (long)dataOffset + (int)relative;
            if (startValue < 0 || startValue > bytes.Length - 2)
                throw Invalid("A string pointer is outside the file.");
            int start = (int)startValue;
            int end = start;
            while (end <= bytes.Length - 2 &&
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(end, 2)) != 0)
                end += 2;
            if (end > bytes.Length - 2)
                throw Invalid("A string is not terminated.");
            try
            {
                return Utf16.GetString(bytes, start, end - start);
            }
            catch (DecoderFallbackException exception)
            {
                throw Invalid("A string is not valid UTF-16.", exception);
            }
        }

        private void ValidateField(int row, int offset, int length)
        {
            if ((uint)row >= (uint)RowCount || offset < 0 ||
                length < 0 || offset > rowWidth - length)
                throw Invalid("A field is outside its row.");
        }

        private InvalidDataException Invalid(string message, Exception? inner = null) =>
            new($"{name}: {message}", inner);
    }
}
