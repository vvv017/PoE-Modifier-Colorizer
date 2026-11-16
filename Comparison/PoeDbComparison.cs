using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GgpkColorizer.Comparison;

public sealed class PoeDbComparison
{
    private const long MaxResponseBytes = 8 * 1024 * 1024;
    private const string ModifiersUrl = "https://poedb.tw/us/Modifiers";
    private const string AltarUrl = "https://poedb.tw/us/Eldritch_Altar";

    private static readonly string[] MapUrls =
    [
        "https://poedb.tw/us/Maps_low_tier",
        "https://poedb.tw/us/Maps_mid_tier",
        "https://poedb.tw/us/Maps_top_tier",
        "https://poedb.tw/us/Maps_uber_tier",
    ];

    private static readonly HttpClient Client = CreateClient();
    private static readonly Regex HtmlTag = new(
        "<[^>]+>",
        RegexOptions.Compiled);
    private static readonly Regex BreakTag = new(
        "<br\\s*/?>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex KeywordTag = new(
        "\\[(?:[^\\[\\]|]*\\|)?([^\\[\\]]+)\\]",
        RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(
        "\\{(?:\\d+)?(?::(?<format>\\+?d))?\\}",
        RegexOptions.Compiled);
    private static readonly Regex TableRow = new(
        "<tr[^>]*>(.*?)</tr>",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.Singleline);
    private static readonly Regex TableCell = new(
        "<td[^>]*>(.*?)</td>",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.Singleline);

    static PoeDbComparison()
    {
        Debug.Assert(NormalizeHtml("A<br>B") == "A\nB");
        Debug.Assert(TemplateMatches(
            "Adds {0}% Damage",
            "Adds (10—20)% Damage"));
        Debug.Assert(!TemplateMatches(
            "Can regenerate Life",
            "Cannot regenerate Life"));
        Debug.Assert(TemplateMatches(
            "Damage is {0:+d}%",
            "Damage is +10%"));
        Debug.Assert(TemplateMatches(
            "Damage is {}%",
            "Damage is 10%"));
        Debug.Assert(TemplateMatches(
            "Damage is {:+d}%",
            "Damage is +10%"));
        Debug.Assert(!TemplateMatches(
            "Damage is {:+d}%",
            "Damage is 10%"));
        Debug.Assert(NormalizeTemplate(
            "<colour:red>{{Adds {0:d}% Damage}}") ==
            "Adds {0:d}% Damage");
        Debug.Assert(ParseAltarSide("Downside") == "downside");
        Debug.Assert(ParseAltarTarget("Map boss gains:") == "boss");
    }

    public async Task<ComparisonReport> CompareAsync(
        ModifierCatalog catalog,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var notices = new List<string>();
        var mapCandidates = new List<Candidate>();
        var altarCandidates = new List<Candidate>();
        var failedGroups = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        string? modifierIndex = await DownloadAsync(
            ModifiersUrl,
            notices,
            cancellationToken).ConfigureAwait(false);
        if (modifierIndex is null)
        {
            failedGroups.Add("map");
        }
        else
        {
            string[] missingLinks = MapUrls
                .Where(url => !modifierIndex.Contains(
                    new Uri(url).AbsolutePath,
                    StringComparison.Ordinal))
                .ToArray();
            if (missingLinks.Length > 0)
            {
                notices.Add(
                    "PoEDB Modifiers no longer links all expected map pages: " +
                    string.Join(", ", missingLinks));
                failedGroups.Add("map");
            }
        }

        foreach (string url in MapUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? html = await DownloadAsync(
                url,
                notices,
                cancellationToken).ConfigureAwait(false);
            if (html is null)
            {
                failedGroups.Add("map");
                continue;
            }

            try
            {
                mapCandidates.AddRange(ParseMapPage(html, url));
            }
            catch (Exception exception) when (
                exception is InvalidDataException or JsonException)
            {
                notices.Add($"PoEDB map schema was not recognized at {url}: " +
                    exception.Message);
                failedGroups.Add("map");
            }
        }

        string? altarHtml = await DownloadAsync(
            AltarUrl,
            notices,
            cancellationToken).ConfigureAwait(false);
        if (altarHtml is null)
        {
            failedGroups.Add("altar");
        }
        else
        {
            try
            {
                altarCandidates.AddRange(ParseAltarPage(altarHtml, out int unclassified));
                if (unclassified > 0)
                {
                    notices.Add($"PoEDB omits the altar faction for {unclassified} rows; those rows were not matched to a faction.");
                    failedGroups.Add("altar");
                }
            }
            catch (InvalidDataException exception)
            {
                notices.Add("PoEDB altar schema was not recognized: " +
                    exception.Message);
                failedGroups.Add("altar");
            }
        }

        var items = new List<ComparisonItem>(catalog.Items.Count);
        foreach (Modifier modifier in catalog.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.AddRange(CompareModifier(
                modifier,
                mapCandidates,
                altarCandidates,
                failedGroups));
        }

        return new ComparisonReport
        {
            SnapshotId = catalog.SnapshotId,
            CheckedAt = DateTimeOffset.UtcNow,
            Items = items,
            Notices = notices,
        };
    }

    private static IReadOnlyList<ComparisonItem> CompareModifier(
        Modifier modifier,
        IReadOnlyList<Candidate> mapCandidates,
        IReadOnlyList<Candidate> altarCandidates,
        HashSet<string> failedGroups)
    {
        string group = modifier.Group.ToLowerInvariant();
        var results = new List<ComparisonItem>();

        if (group == "map")
        {
            results.Add(CompareCandidates(
                modifier,
                mapCandidates,
                failedGroups,
                "map"));
        }
        else if (group != "altar")
        {
            results.Add(CreateResult(
                modifier,
                "unchecked",
                detail:
                    "PoEDB comparison is not defined for this source group."));
            return results;
        }

        List<AltarUse> altarUses = modifier.AltarUses
            .DistinctBy(use => (use.Faction, use.Target, use.Side))
            .ToList();
        if (altarUses.Count == 0)
        {
            if (group == "altar")
            {
                results.Add(CreateResult(
                    modifier,
                    "unchecked",
                    detail: "Local altar context is unavailable; no " +
                        "unconstrained PoEDB match was attempted."));
            }
            return results;
        }

        foreach (AltarUse altarUse in altarUses)
        {
            results.Add(CompareAltarUse(
                modifier,
                altarUse,
                altarCandidates,
                failedGroups));
        }

        return results;
    }

    private static ComparisonItem CompareAltarUse(
        Modifier modifier,
        AltarUse altarUse,
        IReadOnlyList<Candidate> altarCandidates,
        HashSet<string> failedGroups)
    {
        if (!IsKnownAltarUse(altarUse))
        {
            return CreateResult(
                modifier,
                "unchecked",
                detail: "Local altar context contains an unsupported value: " +
                    DescribeAltarUse(altarUse));
        }

        List<Candidate> candidates = altarCandidates
            .Where(candidate =>
                candidate.Faction == altarUse.Faction &&
                candidate.Target == altarUse.Target &&
                candidate.Side == altarUse.Side)
            .ToList();

        return CompareCandidates(
            modifier,
            candidates,
            failedGroups,
            "altar",
            altarUse);
    }

    private static ComparisonItem CompareCandidates(
        Modifier modifier,
        IReadOnlyList<Candidate> candidates,
        HashSet<string> failedGroups,
        string group,
        AltarUse? altarUse = null)
    {
        string altarContext = altarUse is null
            ? ""
            : DescribeAltarUse(altarUse);

        List<Candidate> matches = candidates
            .Where(candidate => TemplateMatches(
                modifier.Template,
                candidate.Text))
            .ToList();

        List<IGrouping<string, Candidate>> matchGroups = matches
            .GroupBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .ToList();

        if (matchGroups.Count == 1)
        {
            List<Candidate> equivalentMatches = matchGroups[0].ToList();
            Candidate match = equivalentMatches[0];
            bool hasPlaceholders = Placeholder.IsMatch(modifier.Template);
            bool textOnlyAltarMatch = altarUse is not null;
            string status = hasPlaceholders || textOnlyAltarMatch
                ? "template-matched"
                : "matched";
            string detail = textOnlyAltarMatch
                ? "Text and altar context matched a single PoEDB candidate " +
                  $"for {altarContext}. PoEDB exposes no reliable ID for " +
                  "this match. Candidate reference: " + match.Context
                : hasPlaceholders
                    ? "Text structure matched. Dynamic numeric ranges were " +
                      "not compared. " + match.Context
                    : "Text matched. " + match.Context;
            if (hasPlaceholders && textOnlyAltarMatch)
            {
                detail += " Dynamic numeric ranges were not compared.";
            }
            if (equivalentMatches.Count > 1)
            {
                detail += $" The same PoEDB modifier appears in " +
                    $"{equivalentMatches.Count} compared categories.";
            }
            return CreateResult(
                modifier,
                status,
                string.Join(
                    "\n---\n",
                    equivalentMatches
                        .Select(candidate => candidate.Text)
                        .Distinct(StringComparer.Ordinal)),
                match.SourceUrl,
                detail.Trim());
        }

        if (matchGroups.Count > 1)
        {
            return CreateResult(
                modifier,
                "ambiguous",
                string.Join(
                    "\n---\n",
                    matches.Take(3).Select(item => item.Text)),
                matches[0].SourceUrl,
                $"{matchGroups.Count} PoEDB modifiers match this text" +
                (altarUse is null ? "" : $" for {altarContext}") +
                "; no entry " +
                "was chosen.");
        }

        bool incomplete = failedGroups.Contains(group);
        return CreateResult(
            modifier,
            incomplete ? "unchecked" : "unmatched",
            detail: incomplete
                ? "The relevant PoEDB source was incomplete; no mismatch " +
                  "was inferred."
                : "No reliable text match was found" +
                  (altarUse is null ? "" : $" for {altarContext}") +
                  ". PoEDB may combine, " +
                  "omit, or version this modifier differently.");
    }

    private static bool IsKnownAltarUse(AltarUse altarUse) =>
        altarUse.Faction is "eater" or "exarch" &&
        altarUse.Target is "player" or "minions" or "boss" &&
        altarUse.Side is "upside" or "downside";

    private static string DescribeAltarUse(AltarUse altarUse) =>
        $"{altarUse.Faction}/{altarUse.Target}/{altarUse.Side}";

    private static ComparisonItem CreateResult(
        Modifier modifier,
        string status,
        string websiteText = "",
        string sourceUrl = "",
        string detail = "") => new()
    {
        ModifierKey = modifier.Key,
        Status = status,
        LocalText = NormalizeTemplate(modifier.Template),
        WebsiteText = websiteText,
        SourceUrl = sourceUrl,
        Detail = detail,
    };

    private static async Task<string?> DownloadAsync(
        string url,
        List<string> notices,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await Client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                notices.Add(
                    $"PoEDB returned {(int)response.StatusCode} for {url}.");
                return null;
            }

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            {
                notices.Add(
                    $"PoEDB response exceeded 8 MB for {url}.");
                return null;
            }

            await response.Content.LoadIntoBufferAsync(MaxResponseBytes)
                .WaitAsync(TimeSpan.FromSeconds(20), cancellationToken)
                .ConfigureAwait(false);

            return await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or OperationCanceledException or TimeoutException)
        {
            notices.Add(
                $"PoEDB could not be read at {url}: {exception.Message}");
            return null;
        }
    }

    private static IReadOnlyList<Candidate> ParseMapPage(
        string html,
        string sourceUrl)
    {
        string json = ExtractModsViewJson(html);
        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(
                "normal",
                out JsonElement normal) ||
            normal.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "The ModsView normal array is missing.");
        }

        var candidates = new List<Candidate>();
        foreach (JsonElement entry in normal.EnumerateArray())
        {
            if (!entry.TryGetProperty("str", out JsonElement rawText) ||
                rawText.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string text = NormalizeHtml(rawText.GetString() ?? "");
            if (text.Length == 0)
                continue;

            string name = entry.TryGetProperty("Name", out JsonElement rawName)
                ? rawName.GetString() ?? ""
                : "";
            string family = ReadFamilies(entry);
            string context = string.Join(
                "; ",
                new[] { name, family }.Where(value => value.Length > 0));
            string identity = context.Length > 0 ? context : text;
            candidates.Add(new Candidate(
                text,
                sourceUrl,
                context,
                identity));
        }

        if (candidates.Count == 0)
            throw new InvalidDataException(
                "The ModsView normal array contained no text.");
        return candidates;
    }

    private static string ExtractModsViewJson(string html)
    {
        const string marker = "new ModsView(";
        int markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidDataException("The ModsView payload is missing.");

        int start = markerIndex + marker.Length;
        while (start < html.Length && char.IsWhiteSpace(html[start]))
            start++;
        if (start >= html.Length || html[start] != '{')
            throw new InvalidDataException("The ModsView payload is not JSON.");

        bool inString = false;
        bool escaped = false;
        int depth = 0;
        for (int index = start; index < html.Length; index++)
        {
            char current = html[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (current == '\\')
                    escaped = true;
                else if (current == '"')
                    inString = false;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }
            if (current == '{')
                depth++;
            else if (current == '}' && --depth == 0)
                return html[start..(index + 1)];
        }

        throw new InvalidDataException("The ModsView JSON is incomplete.");
    }

    private static IReadOnlyList<Candidate> ParseAltarPage(string html, out int unclassified)
    {
        unclassified = 0;
        const string header =
            "<th>Category</th><th>Side</th><th>Choice Type</th>" +
            "<th>Name</th><th>Weight</th>";
        int headerIndex = html.IndexOf(
            header,
            StringComparison.OrdinalIgnoreCase);
        if (headerIndex < 0)
            throw new InvalidDataException(
                "The five-column altar table is missing.");

        int bodyStart = html.IndexOf(
            "<tbody>",
            headerIndex,
            StringComparison.OrdinalIgnoreCase);
        int bodyEnd = bodyStart < 0
            ? -1
            : html.IndexOf(
                "</tbody>",
                bodyStart,
                StringComparison.OrdinalIgnoreCase);
        if (bodyStart < 0 || bodyEnd < 0)
            throw new InvalidDataException("The altar table body is missing.");

        string body = html[bodyStart..bodyEnd];
        MatchCollection rows = TableRow.Matches(body);
        if (rows.Count == 0)
            throw new InvalidDataException("The altar table has no rows.");

        var candidates = new List<Candidate>(rows.Count);
        foreach (Match row in rows)
        {
            MatchCollection cells = TableCell.Matches(row.Groups[1].Value);
            if (cells.Count != 5)
                throw new InvalidDataException(
                    "An altar row does not have five cells.");

            if (string.IsNullOrWhiteSpace(cells[0].Groups[1].Value))
            {
                unclassified++;
                continue;
            }
            string faction = cells[0].Value.Contains(
                "TangleAltar",
                StringComparison.OrdinalIgnoreCase)
                ? "eater"
                : cells[0].Value.Contains(
                    "CleansingFireAltar",
                    StringComparison.OrdinalIgnoreCase)
                    ? "exarch"
                    : throw new InvalidDataException(
                        "An altar row has an unknown category.");
            string sideLabel = NormalizeHtml(cells[1].Groups[1].Value);
            string targetLabel = NormalizeHtml(cells[2].Groups[1].Value);
            string side = ParseAltarSide(sideLabel);
            string target = ParseAltarTarget(targetLabel);
            string text = NormalizeHtml(cells[3].Groups[1].Value);
            string weight = NormalizeHtml(cells[4].Groups[1].Value);
            if (text.Length == 0)
                throw new InvalidDataException(
                    "An altar row has no modifier text.");

            candidates.Add(new Candidate(
                text,
                AltarUrl,
                $"{faction}; {sideLabel}; {targetLabel}; weight {weight}",
                $"{faction}\u001f{side}\u001f{target}\u001f{text}",
                faction,
                target,
                side));
        }

        return candidates;
    }

    private static string ParseAltarSide(string value)
    {
        if (value.Equals("Upside", StringComparison.OrdinalIgnoreCase))
            return "upside";
        if (value.Equals("Downside", StringComparison.OrdinalIgnoreCase))
            return "downside";
        throw new InvalidDataException(
            $"An altar row has an unknown side: {value}");
    }

    private static string ParseAltarTarget(string value)
    {
        if (value.Equals(
                "Player gains:",
                StringComparison.OrdinalIgnoreCase))
        {
            return "player";
        }
        if (value.Equals(
                "Eldritch Minions gain:",
                StringComparison.OrdinalIgnoreCase))
        {
            return "minions";
        }
        if (value.Equals(
                "Map boss gains:",
                StringComparison.OrdinalIgnoreCase))
        {
            return "boss";
        }
        throw new InvalidDataException(
            $"An altar row has an unknown choice type: {value}");
    }

    private static string ReadFamilies(JsonElement entry)
    {
        if (!entry.TryGetProperty("ModFamilyList", out JsonElement families) ||
            families.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return string.Join(
            ", ",
            families.EnumerateArray()
                .Select(family => family.GetString())
                .Where(family => !string.IsNullOrEmpty(family)));
    }

    private static bool TemplateMatches(string template, string websiteText)
    {
        string local = NormalizeTemplate(template);
        string website = NormalizeText(websiteText);
        if (local.Length == 0 || website.Length == 0)
            return false;

        if (!Placeholder.IsMatch(local))
        {
            return local.Contains('\n')
                ? string.Equals(
                    local,
                    website,
                    StringComparison.OrdinalIgnoreCase)
                : website.Split('\n').Any(line => string.Equals(
                    local,
                    line,
                    StringComparison.OrdinalIgnoreCase));
        }

        var pattern = new StringBuilder();
        int previous = 0;
        foreach (Match placeholder in Placeholder.Matches(local))
        {
            pattern.Append(Regex.Escape(local[previous..placeholder.Index]));
            pattern.Append(NumericPlaceholderPattern(placeholder));
            previous = placeholder.Index + placeholder.Length;
        }
        pattern.Append(Regex.Escape(local[previous..]));
        pattern.Replace(@"\ ", @"\s+");
        pattern.Replace(@"\n", @"\s+");
        string boundedPattern = "\\A" + pattern + "\\z";

        return Regex.IsMatch(
            website,
            boundedPattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(200)) ||
            !local.Contains('\n') && website.Split('\n').Any(line =>
                Regex.IsMatch(
                    line,
                    boundedPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(200)));
    }

    private static string NumericPlaceholderPattern(Match placeholder)
    {
        const string number = @"\d+(?:[.,]\d+)?";
        const string range = number +
            @"(?:\s*[—–-]\s*[+\-−]?" + number + @")?";
        string sign = placeholder.Groups["format"].Value == "+d"
            ? @"[+\-−]"
            : @"[+\-−]?";
        return "(?:" + sign + @"[([]?" + range + @"[)\]]?|" +
            @"[([]?" + sign + range + @"[)\]]?)";
    }

    private static string NormalizeTemplate(string template)
    {
        string text = template
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);
        text = StripColourMarkup(text);
        text = KeywordTag.Replace(text, "$1");
        return NormalizeText(text);
    }

    private static string NormalizeHtml(string html)
    {
        string text = BreakTag.Replace(html, "\n");
        text = HtmlTag.Replace(text, "");
        return NormalizeText(WebUtility.HtmlDecode(text));
    }

    private static string NormalizeText(string text)
    {
        string[] lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => Regex.Replace(line, "\\s+", " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        return string.Join("\n", lines);
    }

    private static string StripColourMarkup(string text)
    {
        const string marker = "<colour:";
        int searchStart = 0;
        while (true)
        {
            int markerStart = text.IndexOf(
                marker,
                searchStart,
                StringComparison.OrdinalIgnoreCase);
            if (markerStart < 0)
                return text;

            int tagEnd = text.IndexOf('>', markerStart);
            if (tagEnd < 0)
                return text;

            ReadOnlySpan<char> wrapped = text.AsSpan(tagEnd + 1);
            int wrapperLength = wrapped.StartsWith(
                "{{",
                StringComparison.Ordinal)
                ? 2
                : wrapped.StartsWith("{", StringComparison.Ordinal)
                    ? 1
                    : 0;
            if (wrapperLength == 0)
            {
                searchStart = tagEnd + 1;
                continue;
            }

            int depth = 1;
            int openingBrace = tagEnd + 1;
            int end = openingBrace + 1;
            for (; end < text.Length && depth > 0; end++)
            {
                if (text[end] == '{')
                    depth++;
                else if (text[end] == '}')
                    depth--;
            }
            if (depth != 0)
                return text;

            string content = text[
                (openingBrace + wrapperLength)..(end - wrapperLength)];
            text = text[..markerStart] + content + text[end..];
            searchStart = markerStart + content.Length;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GgpkColorizer/0.1");
        return client;
    }

    private sealed record Candidate(
        string Text,
        string SourceUrl,
        string Context,
        string Identity,
        string Faction = "",
        string Target = "",
        string Side = "");
}
