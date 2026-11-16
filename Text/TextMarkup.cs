using System.Text;
using System.Text.RegularExpressions;

namespace GgpkColorizer.Text;

public static partial class TextMarkup
{
    public static string Display(string rawTemplate)
    {
        ArgumentNullException.ThrowIfNull(rawTemplate);
        string unwrapped = RemoveOuterColour(rawTemplate);
        string labels = KeywordMarkup().Replace(unwrapped, match =>
            match.Groups["label"].Value);
        StringBuilder output = new(labels.Length);
        for (int index = 0; index < labels.Length; index++)
        {
            if (labels[index] == '\\' && index + 1 < labels.Length)
            {
                char escaped = labels[index + 1];
                if (escaped == 'n')
                {
                    output.Append(Environment.NewLine);
                    index++;
                    continue;
                }
                if (escaped is '\\' or '"')
                {
                    output.Append(escaped);
                    index++;
                    continue;
                }
            }
            output.Append(labels[index]);
        }
        return output.ToString();
    }

    private static string RemoveOuterColour(string value)
    {
        if (!value.StartsWith("<colour:", StringComparison.OrdinalIgnoreCase))
            return value;
        int tagEnd = value.IndexOf('>');
        if (tagEnd < 0)
            return value;
        int contentStart = tagEnd + 1;
        if (value.AsSpan(contentStart).StartsWith("{{") && value.EndsWith("}}", StringComparison.Ordinal))
            return value[(contentStart + 2)..^2];
        if (value.AsSpan(contentStart).StartsWith("{") && value.EndsWith("}", StringComparison.Ordinal))
            return value[(contentStart + 1)..^1];
        return value;
    }

    [GeneratedRegex(@"\[(?:[^\]|]*\|)?(?<label>[^\]]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex KeywordMarkup();
}
