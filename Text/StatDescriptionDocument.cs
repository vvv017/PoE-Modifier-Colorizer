using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GgpkColorizer.Text;

public sealed class StatDescriptionDocument
{
    private readonly string resourcePath;
    private readonly string text;
    private readonly Encoding encoding;
    private readonly byte[] preamble;
    private readonly Dictionary<string, Entry> entries;

    private StatDescriptionDocument(
        string resourcePath,
        string text,
        Encoding encoding,
        byte[] preamble,
        List<Modifier> modifiers,
        List<string> includes,
        Dictionary<string, Entry> entries)
    {
        this.resourcePath = resourcePath;
        this.text = text;
        this.encoding = encoding;
        this.preamble = preamble;
        Modifiers = modifiers;
        Includes = includes;
        this.entries = entries;
    }

    public IReadOnlyList<Modifier> Modifiers { get; }

    public IReadOnlyList<string> Includes { get; }

    public static StatDescriptionDocument Parse(string resourcePath, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePath);
        ArgumentNullException.ThrowIfNull(bytes);

        (Encoding encoding, byte[] preamble, int contentOffset) = DetectEncoding(bytes);
        string text = encoding.GetString(bytes, contentOffset, bytes.Length - contentOffset);
        List<Line> lines = ReadLines(text);
        string normalizedPath = resourcePath.Replace('\\', '/').ToLowerInvariant();
        string group = GetGroup(normalizedPath);
        List<Modifier> modifiers = [];
        List<string> includes = [];
        Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
        Dictionary<string, int> occurrences = new(StringComparer.Ordinal);

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            string line = lines[lineIndex].Content;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (StartsWithDirective(line, "include"))
            {
                includes.Add(ReadDirectiveQuotedValue(line, "include", lineIndex));
                continue;
            }

            if (StartsWithDirective(line, "no_description"))
            {
                ValidateNoDescription(line, lineIndex);
                continue;
            }

            if (line.Equals("description", StringComparison.Ordinal))
            {
                ParseDescription(
                    normalizedPath,
                    group,
                    text,
                    lines,
                    ref lineIndex,
                    modifiers,
                    entries,
                    occurrences);
                continue;
            }

            throw Invalid(lineIndex, "Unknown top-level structure.");
        }

        return new StatDescriptionDocument(
            normalizedPath,
            text,
            encoding,
            preamble,
            modifiers,
            includes,
            entries);
    }

    public Modifier? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return entries.TryGetValue(key, out Entry? entry) ? entry.Modifier : null;
    }

    public byte[] Replace(IReadOnlyDictionary<string, string> replacementTemplates)
    {
        ArgumentNullException.ThrowIfNull(replacementTemplates);

        Dictionary<string, string> requested = new(StringComparer.Ordinal);
        foreach ((string key, string template) in replacementTemplates)
        {
            if (!entries.ContainsKey(key))
                throw new KeyNotFoundException($"Unknown modifier key: {key}");
            ValidateTemplate(template);
            requested.Add(key, template);
        }

        List<Replacement> replacements = new(requested.Count);
        foreach ((string key, string template) in requested)
        {
            Entry entry = entries[key];
            replacements.Add(new Replacement(entry.TemplateStart, entry.TemplateLength, template));
        }

        StringBuilder output = new(text);
        foreach (Replacement replacement in replacements.OrderByDescending(item => item.Start))
        {
            output.Remove(replacement.Start, replacement.Length);
            output.Insert(replacement.Start, replacement.Value);
        }

        byte[] encoded = Encode(output.ToString());
        ValidateReplacement(encoded, requested);
        return encoded;
    }

    private void ValidateReplacement(
        byte[] encoded,
        IReadOnlyDictionary<string, string> requested)
    {
        StatDescriptionDocument reparsed = Parse(resourcePath, encoded);
        if (encoding.CodePage != reparsed.encoding.CodePage ||
            !preamble.AsSpan().SequenceEqual(reparsed.preamble))
            throw new InvalidDataException("Replacement changed the resource encoding or BOM.");

        if (entries.Count != reparsed.entries.Count)
            throw new InvalidDataException("Replacement changed the modifier structure.");

        foreach ((string key, Entry original) in entries)
        {
            if (!reparsed.entries.TryGetValue(key, out Entry? current))
                throw new InvalidDataException($"Replacement removed modifier key: {key}");

            string expected = requested.TryGetValue(key, out string? replacement)
                ? replacement ?? throw new InvalidDataException("Replacement template is null.")
                : original.Modifier.Template;
            if (!current.Modifier.Template.Equals(expected, StringComparison.Ordinal))
                throw new InvalidDataException($"Replacement changed an unexpected modifier: {key}");
        }
    }

    private static void ParseDescription(
        string resourcePath,
        string group,
        string text,
        List<Line> lines,
        ref int lineIndex,
        List<Modifier> modifiers,
        Dictionary<string, Entry> entries,
        Dictionary<string, int> occurrences)
    {
        if (lineIndex + 2 >= lines.Count)
            throw Invalid(lineIndex, "Incomplete description block.");

        Line headerLine = ReadNextIndented(lines, ref lineIndex, "description header");
        string[] header = SplitFields(headerLine.Content);
        int statCount = ParseNonNegativeCount(header, lineIndex, "stat count");
        if (statCount == 0 || header.Length != statCount + 1)
            throw Invalid(lineIndex, "Stat count does not match the description header.");
        string[] statIds = header[1..];

        Line countLine = ReadNextIndented(lines, ref lineIndex, "English variant count");
        string[] countFields = SplitFields(countLine.Content);
        int variantCount = ParseNonNegativeCount(countFields, lineIndex, "English variant count");
        if (countFields.Length != 1)
            throw Invalid(lineIndex, "English variant count line contains unexpected fields.");

        for (int variantIndex = 0; variantIndex < variantCount; variantIndex++)
        {
            if (++lineIndex >= lines.Count)
                throw Invalid(lineIndex, "English variant list ended unexpectedly.");

            Line line = lines[lineIndex];
            if (line.Content.Length == 0 || !char.IsWhiteSpace(line.Content[0]))
                throw Invalid(lineIndex, "English variant must be indented.");

            QuotedSpan quoted = FindQuotedSpan(line.Content, lineIndex);
            string prefix = NormalizeFields(line.Content[..quoted.OpenQuote]);
            string suffix = NormalizeFields(line.Content[(quoted.CloseQuote + 1)..]);
            string occurrenceKey =
                $"{string.Join('\u001f', statIds)}\n{prefix}\n{suffix}";
            int occurrence = occurrences.TryGetValue(occurrenceKey, out int current) ? current + 1 : 1;
            occurrences[occurrenceKey] = occurrence;
            string occurrenceText = occurrence.ToString(CultureInfo.InvariantCulture);
            string variantKey =
                $"condition={prefix};suffix={suffix};occurrence={occurrenceText}";
            string key = CreateKey(resourcePath, statIds, prefix, suffix, occurrenceText);
            if (entries.ContainsKey(key))
                throw Invalid(lineIndex, $"Duplicate modifier key {key}.");

            int templateStart = line.Start + quoted.OpenQuote + 1;
            int templateLength = quoted.CloseQuote - quoted.OpenQuote - 1;
            string template = text.Substring(templateStart, templateLength);
            Modifier modifier = new()
            {
                Key = key,
                ResourcePath = resourcePath,
                StatIds = statIds,
                VariantKey = variantKey,
                Conditions = prefix,
                Template = template,
                Group = group,
            };
            modifiers.Add(modifier);
            entries.Add(key, new Entry(modifier, templateStart, templateLength));
        }

        while (lineIndex + 1 < lines.Count)
        {
            string next = lines[lineIndex + 1].Content;
            if (string.IsNullOrWhiteSpace(next) || !char.IsWhiteSpace(next[0]))
                break;

            ParseLanguageBlock(lines, ref lineIndex);
        }
    }

    private static void ParseLanguageBlock(List<Line> lines, ref int lineIndex)
    {
        Line languageLine = ReadNextIndented(lines, ref lineIndex, "language header");
        string languageHeader = languageLine.Content.TrimStart();
        if (!StartsWithDirective(languageHeader, "lang"))
            throw Invalid(lineIndex, "Expected a language header.");
        ReadDirectiveQuotedValue(languageHeader, "lang", lineIndex);

        Line countLine = ReadNextIndented(lines, ref lineIndex, "language variant count");
        string[] countFields = SplitFields(countLine.Content);
        int variantCount = ParseNonNegativeCount(countFields, lineIndex, "language variant count");
        if (countFields.Length != 1)
            throw Invalid(lineIndex, "Language variant count line contains unexpected fields.");

        for (int variantIndex = 0; variantIndex < variantCount; variantIndex++)
        {
            Line variantLine = ReadNextIndented(lines, ref lineIndex, "language variant");
            FindQuotedSpan(variantLine.Content, lineIndex);
        }
    }

    private static Line ReadNextIndented(
        List<Line> lines,
        ref int lineIndex,
        string structureName)
    {
        if (++lineIndex >= lines.Count)
            throw Invalid(lineIndex, $"Missing {structureName}.");

        Line line = lines[lineIndex];
        if (line.Content.Length == 0 || !char.IsWhiteSpace(line.Content[0]))
            throw Invalid(lineIndex, $"{structureName} must be indented.");
        return line;
    }

    private byte[] Encode(string value)
    {
        byte[] content = encoding.GetBytes(value);
        if (preamble.Length == 0)
            return content;

        byte[] result = GC.AllocateUninitializedArray<byte>(preamble.Length + content.Length);
        preamble.CopyTo(result, 0);
        content.CopyTo(result, preamble.Length);
        return result;
    }

    private static (Encoding Encoding, byte[] Preamble, int ContentOffset) DetectEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
            return (new UnicodeEncoding(false, false, true), Encoding.Unicode.Preamble.ToArray(), 2);
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
            return (new UnicodeEncoding(true, false, true), Encoding.BigEndianUnicode.Preamble.ToArray(), 2);
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            return (new UTF8Encoding(false, true), Encoding.UTF8.Preamble.ToArray(), 3);
        return (new UTF8Encoding(false, true), [], 0);
    }

    private static List<Line> ReadLines(string value)
    {
        List<Line> lines = [];
        int start = 0;
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] != '\r' && value[index] != '\n')
                continue;

            lines.Add(new Line(start, value[start..index]));
            if (value[index] == '\r' && index + 1 < value.Length && value[index + 1] == '\n')
                index++;
            start = index + 1;
        }

        if (start <= value.Length)
            lines.Add(new Line(start, value[start..]));
        return lines;
    }

    private static string ReadDirectiveQuotedValue(
        string line,
        string directive,
        int lineIndex)
    {
        QuotedSpan span = FindQuotedSpan(line, lineIndex);
        if (!line[..span.OpenQuote].Trim().Equals(directive, StringComparison.Ordinal))
            throw Invalid(lineIndex, $"Invalid {directive} directive.");
        if (!string.IsNullOrWhiteSpace(line[(span.CloseQuote + 1)..]))
            throw Invalid(lineIndex, $"Unexpected data after {directive} value.");
        return line[(span.OpenQuote + 1)..span.CloseQuote];
    }

    private static QuotedSpan FindQuotedSpan(string line, int lineIndex)
    {
        int openQuote = FindUnescapedQuote(line, 0);
        if (openQuote < 0)
            throw Invalid(lineIndex, "Variant has no opening quote.");
        int closeQuote = FindUnescapedQuote(line, openQuote + 1);
        if (closeQuote < 0)
            throw Invalid(lineIndex, "Variant has no closing quote.");
        if (FindUnescapedQuote(line, closeQuote + 1) >= 0)
            throw Invalid(lineIndex, "Variant contains an unexpected quote.");
        return new QuotedSpan(openQuote, closeQuote);
    }

    private static int FindUnescapedQuote(string value, int start)
    {
        for (int index = start; index < value.Length; index++)
        {
            if (value[index] != '"')
                continue;
            int backslashes = 0;
            for (int before = index - 1; before >= 0 && value[before] == '\\'; before--)
                backslashes++;
            if (backslashes % 2 == 0)
                return index;
        }
        return -1;
    }

    private static void ValidateTemplate(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.Contains('\r') || template.Contains('\n'))
            throw new ArgumentException("Templates must use the literal \\n sequence instead of line breaks.", nameof(template));
        if (FindUnescapedQuote(template, 0) >= 0)
            throw new ArgumentException("Quotes inside templates must be escaped.", nameof(template));
    }

    private static string[] SplitFields(string line) =>
        line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string NormalizeFields(string value) => string.Join(' ', SplitFields(value));

    private static int ParseNonNegativeCount(string[] fields, int lineIndex, string fieldName)
    {
        if (fields.Length == 0 ||
            !int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int count) ||
            count < 0)
            throw Invalid(lineIndex, $"Invalid {fieldName}.");
        return count;
    }

    private static bool StartsWithDirective(string line, string directive) =>
        line.StartsWith(directive, StringComparison.Ordinal) &&
        (line.Length == directive.Length || char.IsWhiteSpace(line[directive.Length]));

    private static void ValidateNoDescription(string line, int lineIndex)
    {
        string[] fields = SplitFields(line);
        if (fields.Length != 2 ||
            !fields[0].Equals("no_description", StringComparison.Ordinal))
            throw Invalid(lineIndex, "Invalid no_description directive.");
    }

    private static string CreateKey(
        string resourcePath,
        string[] statIds,
        string condition,
        string suffix,
        string occurrence)
    {
        string source =
            $"{resourcePath}\n{string.Join('\u001f', statIds)}\n{condition}\n{suffix}\n{occurrence}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private static string GetGroup(string resourcePath)
    {
        string fileName = Path.GetFileName(resourcePath);
        if (fileName.Contains("primordial_altar", StringComparison.OrdinalIgnoreCase))
            return "altar";
        if (fileName.Contains("map_stat", StringComparison.OrdinalIgnoreCase))
            return "map";
        return "shared";
    }

    private static InvalidDataException Invalid(int zeroBasedLine, string message) =>
        new($"Line {zeroBasedLine + 1}: {message}");

    private sealed record Entry(Modifier Modifier, int TemplateStart, int TemplateLength);
    private sealed record Replacement(int Start, int Length, string Value);
    private readonly record struct Line(int Start, string Content);
    private readonly record struct QuotedSpan(int OpenQuote, int CloseQuote);
}
