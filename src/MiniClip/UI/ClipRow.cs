namespace MiniClip.UI;

/// <summary>
/// One line of the candidate list. Owns its own preview formatting so the view stays
/// free of string surgery and the rules can be read in one place.
/// </summary>
/// <remarks>
/// The preview is a <em>read</em> of the payload, never a re-formatting of it. What the
/// user sees here is trimmed for width; what gets pasted is the untouched original.
/// </remarks>
public sealed class ClipRow : System.ComponentModel.INotifyPropertyChanged
{
    /// <summary>Longest preview line we will hand to the renderer. The row clips anything longer.</summary>
    private const int MaxPreviewLength = 400;

    private readonly HistoryEntry _entry;
    private bool _isSelected;

    public ClipRow(HistoryEntry entry, int index)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Index = index;
        (PreviewLine1, PreviewLine2, IsBlank) = BuildPreview(entry.Text);
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Position in the list, newest first. Zero is the most recent clip.</summary>
    public int Index { get; }

    /// <summary>The exact text that will be pasted. Never shown verbatim — see the preview properties.</summary>
    public string Text => _entry.Text;

    public string PreviewLine1 { get; }

    public string PreviewLine2 { get; }

    public bool HasSecondLine => PreviewLine2.Length > 0;

    /// <summary>
    /// True when the clip is nothing but whitespace. Such an entry is real and pasteable,
    /// so it must never render as an empty row — that reads as a rendering bug.
    /// </summary>
    public bool IsBlank { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    /// <summary>
    /// Splits a clip into at most two preview lines.
    /// </summary>
    /// <remarks>
    /// Rules, in order: normalise line endings; drop leading blank lines so the preview
    /// always starts at the first real line; collapse runs of spaces and tabs for display
    /// while keeping leading indentation of the shown line (up to 8 columns), because
    /// indentation is what makes code read as code; trim trailing whitespace per line.
    /// A clone of the payload is never produced — only a display string.
    /// </remarks>
    private static (string Line1, string Line2, bool IsBlank) BuildPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, string.Empty, true);
        }

        var lines = SplitLines(text);

        // Skip leading blank lines.
        var first = 0;
        while (first < lines.Count && lines[first].Length == 0)
        {
            first++;
        }

        if (first >= lines.Count)
        {
            // Whitespace only. Say so, in the payload's own terms.
            return ("¶", $" {DescribeWhitespace(text)}", true);
        }

        var line1 = Condense(lines[first]);

        // Find the next non-blank line for the second preview row.
        var second = first + 1;
        while (second < lines.Count && lines[second].Length == 0)
        {
            second++;
        }

        var line2 = second < lines.Count ? Condense(lines[second]) : string.Empty;

        return (line1, line2, false);
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>(4);
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is not ('\n' or '\r'))
            {
                continue;
            }

            lines.Add(text[start..i]);

            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
            }

            start = i + 1;
        }

        lines.Add(text[start..]);
        return lines;
    }

    /// <summary>
    /// Collapses horizontal whitespace runs to one space for display, preserving up to
    /// eight columns of leading indentation so an indented code block still reads as one.
    /// </summary>
    private static string Condense(string line)
    {
        var trimmedEnd = line.Length;
        while (trimmedEnd > 0 && char.IsWhiteSpace(line[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        if (trimmedEnd == 0)
        {
            return string.Empty;
        }

        var indent = 0;
        while (indent < trimmedEnd && indent < 8 && line[indent] is ' ' or '\t')
        {
            indent++;
        }

        var builder = new System.Text.StringBuilder(Math.Min(trimmedEnd, MaxPreviewLength) + 8);

        for (var i = 0; i < indent; i++)
        {
            builder.Append(' ');
        }

        var previousWasSpace = false;
        for (var i = indent; i < trimmedEnd && builder.Length < MaxPreviewLength; i++)
        {
            var c = line[i];
            if (c is ' ' or '\t')
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(c);
            previousWasSpace = false;
        }

        return builder.ToString();
    }

    /// <summary>Describes a whitespace-only clip in words, so the row is never blank.</summary>
    private static string DescribeWhitespace(string text)
    {
        var characters = 0;
        var lineBreaks = 0;

        foreach (var c in text)
        {
            if (c is '\n' or '\r')
            {
                lineBreaks++;
            }
            else
            {
                characters++;
            }
        }

        if (characters == 0)
        {
            return lineBreaks <= 1 ? "1 个换行" : $"{lineBreaks} 个换行";
        }

        return characters == 1 ? "1 个空白字符" : $"{characters} 个空白字符";
    }
}
