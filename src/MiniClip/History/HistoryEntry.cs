namespace MiniClip;

/// <summary>
/// A single clipboard history entry: the text exactly as it was copied, plus the
/// moment MiniClip observed it. The text is never trimmed or normalised — commands,
/// indentation and newlines have to survive a round trip untouched.
/// </summary>
public sealed record HistoryEntry
{
    public HistoryEntry(string text, DateTimeOffset capturedAt)
    {
        Text = text;
        CapturedAt = capturedAt;
    }

    /// <summary>The clip text, byte-for-byte as it came off the clipboard.</summary>
    public string Text { get; }

    /// <summary>When this entry last entered the list (or was promoted to the top).</summary>
    public DateTimeOffset CapturedAt { get; }

    /// <summary>
    /// Number of visual lines the clip occupies, capped so a pathological input
    /// cannot make the popup measure an enormous string.
    /// </summary>
    public int LineCount
    {
        get
        {
            var count = 1;
            var limit = Math.Min(Text.Length, 4096);
            for (var i = 0; i < limit; i++)
            {
                if (Text[i] == '\n')
                {
                    count++;
                    if (count > 64)
                    {
                        return 64;
                    }
                }
            }

            return count;
        }
    }

    public bool IsMultiLine => LineCount > 1;

    /// <summary>Character count, shown as useful context for long clips.</summary>
    public int CharacterCount => Text.Length;

    /// <summary>A single-line preview for the row, with the whitespace flattened.</summary>
    public string SingleLinePreview
    {
        get
        {
            if (!IsMultiLine)
            {
                return Text;
            }

            Span<char> buffer = stackalloc char[Math.Min(Text.Length, 512)];
            var written = 0;
            for (var i = 0; i < Text.Length && written < buffer.Length; i++)
            {
                var c = Text[i];
                if (c is '\r')
                {
                    continue;
                }

                buffer[written++] = c == '\n' || c == '\t' ? ' ' : c;
            }

            return new string(buffer[..written]);
        }
    }
}
