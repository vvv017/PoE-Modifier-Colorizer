using System.Text.RegularExpressions;

namespace GgpkColorizer;

internal static partial class ColorMarkup
{
    public static readonly List<GameColor> Palette =
    [
        new("red", "#FF4444"),
        new("yellow", "#FFFF00"),
        new("green", "#44CC44"),
        new("ancestralblue", "#80BFFF"),
        new("rgb(255,255,255)", "#FFFFFF"),
    ];

    public static string Apply(string template, string color)
    {
        if (!Palette.Any(item => item.Name == color))
            throw new ArgumentException("Choose a supported game color.", nameof(color));
        string plain = Unwrap(template);
        if (string.IsNullOrWhiteSpace(plain))
            throw new InvalidDataException("An empty description cannot be colored.");
        if (plain.StartsWith('<'))
            throw new InvalidDataException("This description already uses unsupported markup; it was not changed.");
        // Stat templates format {0}; doubled outer braces keep the color scope literal.
        string result = "<colour:" + color + ">{{" + plain + "}}";
        if (!Placeholder().Matches(plain).Select(match => match.Value)
            .SequenceEqual(Placeholder().Matches(result).Select(match => match.Value)))
            throw new InvalidDataException("The color change altered a value placeholder.");
        return result;
    }

    public static string Unwrap(string template)
    {
        var match = OuterColor().Match(template);
        return match.Success ? match.Groups[2].Value : template;
    }

    [GeneratedRegex(@"\{(?:\d+)?(?::[^{}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"\A<colour:([^>]+)>\{\{([\s\S]*)\}\}\z", RegexOptions.CultureInvariant)]
    private static partial Regex OuterColor();
}
