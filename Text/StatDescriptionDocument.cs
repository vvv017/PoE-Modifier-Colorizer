using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GgpkColorizer.Text;

public sealed class StatDescriptionDocument
{
    private const string EnglishLanguage = "English";
    private const string TraditionalChineseLanguage = "Traditional Chinese";
    private readonly string resourcePath;
    private readonly string text;
    private readonly Encoding encoding;
    private readonly byte[] preamble;
    private readonly Dictionary<string, Entry> entries;
    private readonly List<StatBlock> statBlocks;

    private StatDescriptionDocument(
        string resourcePath,
        string text,
        Encoding encoding,
        byte[] preamble,
        List<Modifier> modifiers,
        List<string> includes,
        Dictionary<string, Entry> entries,
        List<StatBlock> statBlocks)
    {
        this.resourcePath = resourcePath;
        this.text = text;
        this.encoding = encoding;
        this.preamble = preamble;
        Modifiers = modifiers;
        Includes = includes;
        this.entries = entries;
        this.statBlocks = statBlocks;
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
        List<StatBlock> statBlocks = [];

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
                int descriptionLineIndex = lineIndex;
                string[] statIds = ParseDescription(
                    normalizedPath,
                    group,
                    text,
                    lines,
                    ref lineIndex,
                    modifiers,
                    entries,
                    occurrences);
                int nextStructureIndex = lineIndex + 1;
                while (nextStructureIndex < lines.Count &&
                    string.IsNullOrWhiteSpace(lines[nextStructureIndex].Content))
                    nextStructureIndex++;
                int blockStart = lines[descriptionLineIndex].Start;
                int blockEnd = nextStructureIndex < lines.Count
                    ? lines[nextStructureIndex].Start
                    : text.Length;
                statBlocks.Add(new StatBlock(statIds, blockStart, blockEnd - blockStart));
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
            entries,
            statBlocks);
    }

    public Modifier? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return entries.TryGetValue(key, out Entry? entry) ? entry.Modifier : null;
    }

    public string GetSingleStatBlock(string statId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statId);
        StatBlock? block = FindSingleStatBlock(statId);
        return block is null ? "" : text.Substring(block.Start, block.Length);
    }

    public string CreateSignedValueBlock(
        string statId,
        string negativeTemplate,
        string nonnegativeTemplate,
        string language = EnglishLanguage,
        string? positiveTemplate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statId);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ValidateTemplate(negativeTemplate);
        ValidateTemplate(nonnegativeTemplate);
        if (positiveTemplate is not null)
            ValidateTemplate(positiveTemplate);

        string originalBlock = GetSingleStatBlock(statId);
        if (originalBlock.Length == 0)
            throw new InvalidDataException($"Single-stat block was not found: {statId}");

        List<Line> lines = ReadLines(originalBlock);
        if (lines.Count < 4 ||
            !lines[0].Content.Equals("description", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported description block: {statId}");

        string[] header = SplitFields(lines[1].Content);
        if (header.Length != 2 ||
            !header[0].Equals("1", StringComparison.Ordinal) ||
            !header[1].Equals(statId, StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported single-stat header: {statId}");

        VariantBlock target = FindVariantBlock(lines, language, statId);
        Line countLine = lines[target.CountLineIndex];
        string[] countFields = SplitFields(countLine.Content);
        if (countFields.Length != 1 ||
            !countFields[0].Equals("1", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Signed value block requires one {language} variant: {statId}");

        Line variantLine = lines[target.VariantLineIndex];
        QuotedSpan quoted = FindQuotedSpan(variantLine.Content, target.VariantLineIndex);
        string conditionPrefix = variantLine.Content[..quoted.OpenQuote];
        if (!NormalizeFields(conditionPrefix).Equals("#", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Signed value block requires a catch-all {language} variant: {statId}");

        int conditionIndex = conditionPrefix.IndexOf('#');
        if (conditionIndex < 0)
            throw new InvalidDataException($"Invalid catch-all condition: {statId}");
        string leadingWhitespace = conditionPrefix[..conditionIndex];
        string quoteSpacing = conditionPrefix[(conditionIndex + 1)..];
        string suffix = variantLine.Content[(quoted.CloseQuote + 1)..];
        string lineEnding = GetLineEnding(originalBlock, lines, target.VariantLineIndex);
        if (lineEnding.Length == 0)
            throw new InvalidDataException(
                $"{language} variant has no reusable line ending: {statId}");

        string negativeLine =
            $"{leadingWhitespace}#|-1{quoteSpacing}\"{negativeTemplate}\"{suffix}";
        string nonnegativeCondition = positiveTemplate is null ? "0|#" : "0";
        string nonnegativeLine =
            $"{leadingWhitespace}{nonnegativeCondition}{quoteSpacing}\"{nonnegativeTemplate}\"{suffix}";
        string replacementVariants = negativeLine + lineEnding + nonnegativeLine;
        string replacementVariantCount = "2";
        if (positiveTemplate is not null)
        {
            string positiveLine =
                $"{leadingWhitespace}1|#{quoteSpacing}\"{positiveTemplate}\"{suffix}";
            replacementVariants += lineEnding + positiveLine;
            replacementVariantCount = "3";
        }

        int countToken = countLine.Content.IndexOf('1');
        if (countToken < 0)
            throw new InvalidDataException($"Invalid {language} variant count: {statId}");
        string replacementCount =
            countLine.Content[..countToken] +
            replacementVariantCount +
            countLine.Content[(countToken + 1)..];

        StringBuilder output = new(originalBlock);
        output.Remove(variantLine.Start, variantLine.Content.Length);
        output.Insert(variantLine.Start, replacementVariants);
        output.Remove(countLine.Start, countLine.Content.Length);
        output.Insert(countLine.Start, replacementCount);
        string generatedBlock = output.ToString();

        byte[] preview = ReplaceSingleStatBlock(statId, originalBlock, generatedBlock);
        StatDescriptionDocument reparsed = Parse(resourcePath, preview);
        if (!reparsed.GetSingleStatBlock(statId).Equals(generatedBlock, StringComparison.Ordinal))
            throw new InvalidDataException("Generated signed value block failed validation.");

        (string Conditions, string Template)[] expectedVariants = positiveTemplate is null
            ? [("#|-1", negativeTemplate), ("0|#", nonnegativeTemplate)]
            : [
                ("#|-1", negativeTemplate),
                ("0", nonnegativeTemplate),
                ("1|#", positiveTemplate),
            ];
        (string Conditions, string Template)[] actualVariants = reparsed.Modifiers
            .Where(modifier =>
                modifier.Language.Equals(language, StringComparison.Ordinal) &&
                modifier.StatIds.Length == 1 &&
                modifier.StatIds[0].Equals(statId, StringComparison.Ordinal))
            .Select(modifier => (modifier.Conditions, modifier.Template))
            .ToArray();
        if (!actualVariants.SequenceEqual(expectedVariants))
            throw new InvalidDataException("Generated signed value variants failed validation.");
        return generatedBlock;
    }

    public byte[] ReplaceSingleStatBlock(
        string statId,
        string expectedBlock,
        string replacementBlock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statId);
        ArgumentNullException.ThrowIfNull(expectedBlock);
        ArgumentNullException.ThrowIfNull(replacementBlock);

        StatBlock? current = FindSingleStatBlock(statId);
        string currentBlock = current is null
            ? ""
            : text.Substring(current.Start, current.Length);
        if (!currentBlock.Equals(expectedBlock, StringComparison.Ordinal))
            throw new InvalidDataException($"Single-stat block changed before replacement: {statId}");

        if (current is null && replacementBlock.Length > 0 &&
            !EndsWithLineEnding(text))
            throw new InvalidDataException(
                "Cannot insert a description block when the resource has no trailing line ending.");

        string outputText;
        if (current is null)
        {
            outputText = text + replacementBlock;
        }
        else
        {
            StringBuilder output = new(text);
            output.Remove(current.Start, current.Length);
            output.Insert(current.Start, replacementBlock);
            outputText = output.ToString();
        }

        byte[] encoded = Encode(outputText);
        ValidateSingleStatBlockReplacement(
            statId,
            current,
            expectedBlock,
            replacementBlock,
            encoded);
        return encoded;
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

    private StatBlock? FindSingleStatBlock(string statId)
    {
        StatBlock? match = null;
        foreach (StatBlock block in statBlocks)
        {
            if (!block.StatIds.Contains(statId, StringComparer.Ordinal))
                continue;
            if (block.StatIds.Length != 1)
                throw new InvalidDataException(
                    $"Stat belongs to a multi-stat description block: {statId}");
            if (match is not null)
                throw new InvalidDataException($"Duplicate single-stat description block: {statId}");
            match = block;
        }
        return match;
    }

    private void ValidateSingleStatBlockReplacement(
        string statId,
        StatBlock? originalBlock,
        string expectedBlock,
        string replacementBlock,
        byte[] encoded)
    {
        StatDescriptionDocument reparsed = Parse(resourcePath, encoded);
        if (encoding.CodePage != reparsed.encoding.CodePage ||
            !preamble.AsSpan().SequenceEqual(reparsed.preamble))
            throw new InvalidDataException("Block replacement changed the resource encoding or BOM.");

        string actualBlock = reparsed.GetSingleStatBlock(statId);
        if (!actualBlock.Equals(replacementBlock, StringComparison.Ordinal))
            throw new InvalidDataException("Inserted description block did not round-trip exactly.");

        List<Entry> originalOthers = entries.Values
            .Where(entry => !IsSingleStatEntry(entry, statId))
            .ToList();
        List<Entry> currentOthers = reparsed.entries.Values
            .Where(entry => !IsSingleStatEntry(entry, statId))
            .ToList();
        if (originalOthers.Count != currentOthers.Count)
            throw new InvalidDataException("Block replacement changed other modifier entries.");

        foreach (Entry original in originalOthers)
        {
            if (!reparsed.entries.TryGetValue(original.Modifier.Key, out Entry? current) ||
                !EntriesMatch(original, current))
                throw new InvalidDataException(
                    $"Block replacement changed another modifier: {original.Modifier.Key}");
        }

        string restoredText;
        if (originalBlock is null)
        {
            if (replacementBlock.Length == 0)
            {
                restoredText = reparsed.text;
            }
            else
            {
                StatBlock inserted = reparsed.FindSingleStatBlock(statId)
                    ?? throw new InvalidDataException("Inserted description block was not indexed.");
                restoredText = reparsed.text.Remove(inserted.Start, inserted.Length);
            }
        }
        else if (replacementBlock.Length == 0)
        {
            restoredText = reparsed.text.Insert(originalBlock.Start, expectedBlock);
        }
        else
        {
            StatBlock replacement = reparsed.FindSingleStatBlock(statId)
                ?? throw new InvalidDataException("Replacement description block was not indexed.");
            restoredText = reparsed.text
                .Remove(replacement.Start, replacement.Length)
                .Insert(replacement.Start, expectedBlock);
        }

        if (!reparsed.Encode(restoredText).AsSpan().SequenceEqual(Encode(text)))
            throw new InvalidDataException("Block replacement is not byte-for-byte reversible.");
    }

    private static bool IsSingleStatEntry(Entry entry, string statId) =>
        entry.Modifier.StatIds.Length == 1 &&
        entry.Modifier.StatIds[0].Equals(statId, StringComparison.Ordinal);

    private static bool EntriesMatch(Entry original, Entry current) =>
        original.Modifier.Key.Equals(current.Modifier.Key, StringComparison.Ordinal) &&
        original.Modifier.ResourcePath.Equals(
            current.Modifier.ResourcePath,
            StringComparison.Ordinal) &&
        original.Modifier.StatIds.SequenceEqual(
            current.Modifier.StatIds,
            StringComparer.Ordinal) &&
        original.Modifier.VariantKey.Equals(
            current.Modifier.VariantKey,
            StringComparison.Ordinal) &&
        original.Modifier.Conditions.Equals(
            current.Modifier.Conditions,
            StringComparison.Ordinal) &&
        original.Modifier.Template.Equals(
            current.Modifier.Template,
            StringComparison.Ordinal) &&
        original.Modifier.Language.Equals(
            current.Modifier.Language,
            StringComparison.Ordinal) &&
        original.Modifier.Group.Equals(current.Modifier.Group, StringComparison.Ordinal);

    private static VariantBlock FindVariantBlock(
        List<Line> lines,
        string targetLanguage,
        string statId)
    {
        VariantBlock? match = null;
        string language = EnglishLanguage;
        int countLineIndex = 2;
        while (countLineIndex < lines.Count)
        {
            Line countLine = lines[countLineIndex];
            if (countLine.Content.Length == 0 ||
                !char.IsWhiteSpace(countLine.Content[0]))
                throw new InvalidDataException($"Invalid {language} variant count: {statId}");

            string[] countFields = SplitFields(countLine.Content);
            int variantCount = ParseNonNegativeCount(
                countFields,
                countLineIndex,
                $"{language} variant count");
            if (countFields.Length != 1)
                throw new InvalidDataException($"Invalid {language} variant count: {statId}");

            int variantLineIndex = countLineIndex + 1;
            int nextLineIndex = variantLineIndex + variantCount;
            if (nextLineIndex > lines.Count)
                throw new InvalidDataException($"Incomplete {language} variant list: {statId}");

            for (int index = variantLineIndex; index < nextLineIndex; index++)
            {
                Line variantLine = lines[index];
                if (variantLine.Content.Length == 0 ||
                    !char.IsWhiteSpace(variantLine.Content[0]))
                    throw new InvalidDataException($"Invalid {language} variant: {statId}");
                FindQuotedSpan(variantLine.Content, index);
            }

            if (language.Equals(targetLanguage, StringComparison.Ordinal))
            {
                if (match is not null)
                    throw new InvalidDataException(
                        $"Duplicate {targetLanguage} variant block: {statId}");
                match = new VariantBlock(countLineIndex, variantLineIndex);
            }

            if (nextLineIndex >= lines.Count ||
                string.IsNullOrWhiteSpace(lines[nextLineIndex].Content))
                break;

            Line languageLine = lines[nextLineIndex];
            if (!char.IsWhiteSpace(languageLine.Content[0]))
                throw new InvalidDataException($"Invalid language header: {statId}");
            string languageHeader = languageLine.Content.TrimStart();
            if (!StartsWithDirective(languageHeader, "lang"))
                throw new InvalidDataException($"Invalid language header: {statId}");
            language = ReadDirectiveQuotedValue(
                languageHeader,
                "lang",
                nextLineIndex);
            countLineIndex = nextLineIndex + 1;
        }

        return match ?? throw new InvalidDataException(
            $"{targetLanguage} variant block was not found: {statId}");
    }

    private static string GetLineEnding(string value, List<Line> lines, int lineIndex)
    {
        Line line = lines[lineIndex];
        int contentEnd = line.Start + line.Content.Length;
        int nextStart = lineIndex + 1 < lines.Count
            ? lines[lineIndex + 1].Start
            : value.Length;
        if (nextStart < contentEnd)
            throw new InvalidDataException("Invalid line offsets.");
        return value[contentEnd..nextStart];
    }

    private static bool EndsWithLineEnding(string value) =>
        value.Length > 0 && value[^1] is '\r' or '\n';

    private static string[] ParseDescription(
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

        ParseVariants(
            resourcePath,
            group,
            EnglishLanguage,
            text,
            lines,
            ref lineIndex,
            statIds,
            modifiers,
            entries,
            occurrences);

        while (lineIndex + 1 < lines.Count)
        {
            string next = lines[lineIndex + 1].Content;
            if (string.IsNullOrWhiteSpace(next) || !char.IsWhiteSpace(next[0]))
                break;

            Line languageLine = ReadNextIndented(lines, ref lineIndex, "language header");
            string languageHeader = languageLine.Content.TrimStart();
            if (!StartsWithDirective(languageHeader, "lang"))
                throw Invalid(lineIndex, "Expected a language header.");
            string language = ReadDirectiveQuotedValue(
                languageHeader,
                "lang",
                lineIndex);
            ParseVariants(
                resourcePath,
                group,
                language,
                text,
                lines,
                ref lineIndex,
                statIds,
                modifiers,
                entries,
                occurrences);
        }

        return statIds;
    }

    private static void ParseVariants(
        string resourcePath,
        string group,
        string language,
        string text,
        List<Line> lines,
        ref int lineIndex,
        string[] statIds,
        List<Modifier> modifiers,
        Dictionary<string, Entry> entries,
        Dictionary<string, int> occurrences)
    {
        Line countLine = ReadNextIndented(lines, ref lineIndex, $"{language} variant count");
        string[] countFields = SplitFields(countLine.Content);
        int variantCount = ParseNonNegativeCount(
            countFields,
            lineIndex,
            $"{language} variant count");
        if (countFields.Length != 1)
            throw Invalid(lineIndex, $"{language} variant count line contains unexpected fields.");

        for (int variantIndex = 0; variantIndex < variantCount; variantIndex++)
        {
            Line line = ReadNextIndented(lines, ref lineIndex, $"{language} variant");
            QuotedSpan quoted = FindQuotedSpan(line.Content, lineIndex);
            if (language is not EnglishLanguage and not TraditionalChineseLanguage)
                continue;

            string prefix = NormalizeFields(line.Content[..quoted.OpenQuote]);
            string suffix = NormalizeFields(line.Content[(quoted.CloseQuote + 1)..]);
            string occurrenceKey =
                $"{string.Join('\u001f', statIds)}\n{prefix}\n{suffix}";
            if (!language.Equals(EnglishLanguage, StringComparison.Ordinal))
                occurrenceKey += $"\n{language}";
            int occurrence = occurrences.TryGetValue(occurrenceKey, out int current)
                ? current + 1
                : 1;
            occurrences[occurrenceKey] = occurrence;
            string occurrenceText = occurrence.ToString(CultureInfo.InvariantCulture);
            string variantKey =
                $"condition={prefix};suffix={suffix};occurrence={occurrenceText}";
            string key = CreateKey(
                resourcePath,
                statIds,
                prefix,
                suffix,
                occurrenceText,
                language);
            if (entries.ContainsKey(key))
                throw Invalid(lineIndex, $"Duplicate modifier key {key}.");

            int templateStart = line.Start + quoted.OpenQuote + 1;
            int templateLength = quoted.CloseQuote - quoted.OpenQuote - 1;
            string template = text.Substring(templateStart, templateLength);
            Modifier modifier = new()
            {
                Language = language,
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
        string occurrence,
        string language)
    {
        string source =
            $"{resourcePath}\n{string.Join('\u001f', statIds)}\n{condition}\n{suffix}\n{occurrence}";
        if (!language.Equals(EnglishLanguage, StringComparison.Ordinal))
            source += $"\n{language}";
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
    private sealed record StatBlock(string[] StatIds, int Start, int Length);
    private readonly record struct VariantBlock(int CountLineIndex, int VariantLineIndex);
    private readonly record struct Line(int Start, string Content);
    private readonly record struct QuotedSpan(int OpenQuote, int CloseQuote);
}
