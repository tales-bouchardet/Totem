using System.Windows;
using System.Windows.Documents;

namespace totem;

public static class SimpleMarkdown
{
    public static Paragraph BuildParagraph(string text, bool applyFormatting)
    {
        var para = new Paragraph { Margin = new Thickness(0) };
        if (string.IsNullOrEmpty(text))
            return para;

        text = text.Replace("\r\n", "\n");

        if (!applyFormatting)
        {
            AppendPlain(para, text);
            return para;
        }

        var i = 0;
        var n = text.Length;
        while (i < n)
        {
            if (text[i] == '\n')
            {
                para.Inlines.Add(new LineBreak());
                i++;
                continue;
            }

            if (Matches(text, i, "**") && TryFindClose(text, i + 2, "**", out var boldEnd))
            {
                para.Inlines.Add(new Bold(new Run(text.Substring(i + 2, boldEnd - i - 2))));
                i = boldEnd + 2;
                continue;
            }

            if (Matches(text, i, "~~") && TryFindClose(text, i + 2, "~~", out var strikeEnd))
            {
                para.Inlines.Add(new Span(new Run(text.Substring(i + 2, strikeEnd - i - 2)))
                {
                    TextDecorations = TextDecorations.Strikethrough,
                });
                i = strikeEnd + 2;
                continue;
            }

            if (text[i] == '*' && TryFindClose(text, i + 1, "*", out var italicEnd))
            {
                para.Inlines.Add(new Italic(new Run(text.Substring(i + 1, italicEnd - i - 1))));
                i = italicEnd + 1;
                continue;
            }

            var j = i;
            while (j < n && text[j] != '\n' && !IsMarkerStart(text, j)) j++;
            if (j == i) j++;
            para.Inlines.Add(new Run(text.Substring(i, j - i)));
            i = j;
        }

        return para;
    }

    private static void AppendPlain(Paragraph para, string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) para.Inlines.Add(new LineBreak());
            para.Inlines.Add(new Run(lines[i]));
        }
    }

    private static bool IsMarkerStart(string s, int i) =>
        Matches(s, i, "**") || s[i] == '*' || Matches(s, i, "~~");

    private static bool Matches(string s, int i, string token) =>
        i + token.Length <= s.Length && string.CompareOrdinal(s, i, token, 0, token.Length) == 0;

    private static bool TryFindClose(string s, int from, string token, out int index)
    {
        index = s.IndexOf(token, from, StringComparison.Ordinal);
        return index >= 0 && !ContainsNewline(s, from, index);
    }

    private static bool ContainsNewline(string s, int from, int to)
    {
        for (var k = from; k < to; k++)
            if (s[k] == '\n') return true;
        return false;
    }
}
