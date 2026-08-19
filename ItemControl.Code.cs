using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace totem;

// ── code block: language switching, colored reader, indent ───────────────────
public partial class ItemControl
{
    private void SetCode(CodeLanguage lang)
    {
        Model.IsCode = true;
        Model.IsPlainText = false;
        Model.Language = lang.Id;
        // InputBox is only the live buffer while actively editing code; re-sync it from
        // the model (which may just have been edited as plain text) before switching over.
        InputBox.Text = string.IsNullOrWhiteSpace(Model.Content) ? lang.Skeleton : Model.Content;
        Model.Content = InputBox.Text;
        ApplyCodeState();
        Changed?.Invoke();
    }

    private void SetPlainText()
    {
        Model.IsCode = false;
        Model.IsPlainText = true;
        Model.Language = null;
        ApplyCodeState();
        Changed?.Invoke();
    }

    private void SetPlain()
    {
        Model.IsCode = false;
        Model.IsPlainText = false;
        Model.Language = null;
        ApplyCodeState();
        Changed?.Invoke();
    }

    // InputBox is code-only now (ContentBox handles plain text/Markdown), so it
    // always uses the code font — nothing left here to toggle per mode.
    private void ApplyCodeState() => UpdateInputView();

    private void RenderCode()
    {
        UpdateCodeBadge();
        var paragraph = CodeHighlighter.BuildParagraph(InputBox.Text, Model.Language);
        CodeReadView.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0) };
    }

    private void UpdateCodeBadge() =>
        CodeBadge.Text = CodeLanguages.ById(Model.Language)?.Name ?? Model.Language ?? "";

    private void UpdateGutter()
    {
        if (!Model.IsCode) return;
        CodeGutter.Text = BuildGutterText(CountLines(InputBox.Text));
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        var count = 1;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n') count++;
        return count;
    }

    private static string BuildGutterText(int lines)
    {
        var sb = new StringBuilder(lines * 3);
        for (var n = 1; n <= lines; n++)
        {
            if (n > 1) sb.Append('\n');
            sb.Append(n);
        }
        return sb.ToString();
    }

    // ── Tab / Shift+Tab indent, Enter auto-indent ───────────────────────────

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!Model.IsCode || !_editing) return;

        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            ReindentSelection(dedent: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            return;
        }

        if (e.Key == Key.Enter)
        {
            var indent = CurrentLineIndent();
            if (indent.Length > 0)
            {
                e.Handled = true;
                var caret = InputBox.SelectionStart;
                InputBox.SelectedText = "\r\n" + indent;
                InputBox.SelectionStart = caret + 2 + indent.Length;
                InputBox.SelectionLength = 0;
            }
        }
    }

    private void ReindentSelection(bool dedent)
    {
        var text = InputBox.Text;
        var start = InputBox.SelectionStart;
        var end = start + InputBox.SelectionLength;

        if (InputBox.SelectionLength == 0)
        {
            if (dedent)
            {
                DedentLine(text, start);
            }
            else
            {
                InputBox.SelectedText = new string(' ', IndentWidth);
                InputBox.SelectionStart = start + IndentWidth;
            }
            return;
        }

        var blockStart = LineStartOf(text, start);
        var blockEnd = LineEndOf(text, end);
        var block = text.Substring(blockStart, blockEnd - blockStart);
        var lines = block.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (dedent)
            {
                var strip = 0;
                while (strip < IndentWidth && strip < lines[i].Length && lines[i][strip] == ' ') strip++;
                if (strip == 0 && lines[i].Length > 0 && lines[i][0] == '\t') strip = 1;
                lines[i] = lines[i].Substring(strip);
            }
            else
            {
                lines[i] = new string(' ', IndentWidth) + lines[i];
            }
        }
        var newBlock = string.Join("\r\n", lines);
        InputBox.Text = text.Substring(0, blockStart) + newBlock + text.Substring(blockEnd);
        InputBox.SelectionStart = blockStart;
        InputBox.SelectionLength = newBlock.Length;
    }

    private void DedentLine(string text, int caret)
    {
        var ls = LineStartOf(text, caret);
        var strip = 0;
        while (strip < IndentWidth && ls + strip < text.Length && text[ls + strip] == ' ') strip++;
        if (strip == 0 && ls < text.Length && text[ls] == '\t') strip = 1;
        if (strip == 0) return;

        InputBox.Text = text.Substring(0, ls) + text.Substring(ls + strip);
        var newCaret = Math.Max(ls, caret - strip);
        InputBox.SelectionStart = newCaret;
        InputBox.SelectionLength = 0;
    }

    private string CurrentLineIndent()
    {
        var text = InputBox.Text;
        var caret = InputBox.SelectionStart;
        var ls = LineStartOf(text, caret);
        var p = ls;
        while (p < caret && (text[p] == ' ' || text[p] == '\t')) p++;
        return text.Substring(ls, p - ls);
    }

    private static int LineStartOf(string s, int pos)
    {
        var i = Math.Min(pos, s.Length);
        while (i > 0 && s[i - 1] != '\n' && s[i - 1] != '\r') i--;
        return i;
    }

    private static int LineEndOf(string s, int pos)
    {
        var i = Math.Min(pos, s.Length);
        while (i < s.Length && s[i] != '\n' && s[i] != '\r') i++;
        return i;
    }

    // ── "Bloco de código" submenu handlers ──────────────────────────────────

    private void SetCodeCmd_Click(object sender, RoutedEventArgs e) => SetCode(CodeLanguages.ById("cmd")!);
    private void SetCodePowerShell_Click(object sender, RoutedEventArgs e) => SetCode(CodeLanguages.ById("powershell")!);
    private void SetCodeSql_Click(object sender, RoutedEventArgs e) => SetCode(CodeLanguages.ById("sql")!);
    private void SetCodeVbs_Click(object sender, RoutedEventArgs e) => SetCode(CodeLanguages.ById("vbs")!);
    private void SetPlainText_Click(object sender, RoutedEventArgs e) => SetPlainText();
    private void SetPlain_Click(object sender, RoutedEventArgs e) => SetPlain();
}
