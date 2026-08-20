using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace totem;

public static class CodeHighlighter
{
    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(255, r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush CommentBrush = Brush(0x6A, 0x99, 0x55);
    private static readonly Brush KeywordBrush = Brush(0x56, 0x9C, 0xD6);
    private static readonly Brush StringBrush = Brush(0xCE, 0x91, 0x78);
    private static readonly Brush NumberBrush = Brush(0xB5, 0xCE, 0xA8);
    private static readonly Brush VariableBrush = Brush(0x9C, 0xDC, 0xFE);
    private static readonly Brush FunctionBrush = Brush(0xDC, 0xDC, 0xAA);
    private static readonly Brush ExecutableBrush = Brush(0x89, 0xD1, 0x85);
    private static readonly Brush ParameterBrush = Brush(0x9D, 0x9D, 0x9D);
    private static readonly Brush DefaultBrush = Brush(0xFF, 0xFF, 0xFF);

    private readonly struct Span
    {
        public readonly int Start;
        public readonly int Length;
        public readonly Brush? Brush;

        public Span(int start, int length, Brush? brush)
        {
            Start = start;
            Length = length;
            Brush = brush;
        }
    }

    public static Paragraph BuildParagraph(string code, string? languageId)
    {
        var para = new Paragraph { Margin = new Thickness(0) };
        if (string.IsNullOrEmpty(code))
            return para;

        var spans = Tokenize(code, LangFor(languageId));
        var inlines = new List<Inline>(spans.Count);
        foreach (var s in spans)
            inlines.Add(new Run(code.Substring(s.Start, s.Length)) { Foreground = s.Brush ?? DefaultBrush });

        para.Inlines.AddRange(inlines);
        return para;
    }

    private static bool IsNewline(char c) => c is '\n' or '\r';

    private static List<Span> Tokenize(string code, Lang lang)
    {
        var spans = new List<Span>();
        var n = code.Length;
        var i = 0;

        var stringBrush = lang.StringColor ?? StringBrush;
        var variableBrush = lang.VariableColor ?? VariableBrush;

        var pendStart = -1;
        var pendEnd = -1;

        void Flush()
        {
            if (pendStart < 0) return;
            spans.Add(new Span(pendStart, pendEnd - pendStart, null));
            pendStart = -1;
        }

        void Pend(int from, int to)
        {
            if (pendStart < 0) pendStart = from;
            pendEnd = to;
        }

        void Emit(int start, int len, Brush brush)
        {
            Flush();
            spans.Add(new Span(start, len, brush));
        }

        var commandPos = true;

        while (i < n)
        {
            var c = code[i];

            var prefix = MatchLineComment(code, i, lang);
            if (prefix is not null)
            {
                var j = i;
                while (j < n && !IsNewline(code[j])) j++;
                Emit(i, j - i, CommentBrush);
                i = j;
                continue;
            }

            if (lang.BlockStart is not null && Matches(code, i, lang.BlockStart))
            {
                var k = code.IndexOf(lang.BlockEnd!, i + lang.BlockStart.Length, StringComparison.Ordinal);
                var end = k < 0 ? n : k + lang.BlockEnd!.Length;
                Emit(i, end - i, CommentBrush);
                i = end;
                continue;
            }

            if (Array.IndexOf(lang.StringDelims, c) >= 0)
            {
                var j = i + 1;
                while (j < n)
                {
                    if (code[j] == c)
                    {
                        if (j + 1 < n && code[j + 1] == c) { j += 2; continue; }
                        j++;
                        break;
                    }
                    if (IsNewline(code[j])) break;
                    j++;
                }
                var end = Math.Min(j, n);
                Emit(i, end - i, stringBrush);
                commandPos = false;
                i = end;
                continue;
            }

            if (lang.CommandWords && commandPos && IsCommandStart(c))
            {
                var j = i;
                while (j < n && !char.IsWhiteSpace(code[j])) j++;
                var token = code.Substring(i, j - i);
                if (HasLetter(token) && !lang.Keywords.Contains(LeadingWord(token)))
                {
                    Emit(i, j - i, FunctionBrush);
                    commandPos = false;
                    i = j;
                    continue;
                }
            }

            if (lang.DashOperators && c == '-' && i + 1 < n && char.IsLetter(code[i + 1]))
            {
                var j = i + 1;
                while (j < n && char.IsLetter(code[j])) j++;
                if (lang.Operators.Contains(code.Substring(i + 1, j - i - 1)))
                {
                    Emit(i, j - i, KeywordBrush);
                    commandPos = false;
                    i = j;
                    continue;
                }
            }

            if (lang.Parameters && (c == '-' || c == '/') && (i == 0 || char.IsWhiteSpace(code[i - 1])))
            {
                var j = i;
                while (j < n && code[j] is '-' or '/') j++;
                if (j < n && char.IsLetter(code[j]))
                {
                    while (j < n && (char.IsLetterOrDigit(code[j]) || code[j] is '_' or '-' or ':')) j++;
                    Emit(i, j - i, ParameterBrush);
                    commandPos = false;
                    i = j;
                    continue;
                }
            }

            if (lang.VariablePrefix != '\0' && c == lang.VariablePrefix)
            {
                var j = i + 1;
                while (j < n && (char.IsLetterOrDigit(code[j]) || code[j] is '_' or ':')) j++;
                if (j > i + 1)
                {
                    Emit(i, j - i, variableBrush);
                    commandPos = false;
                    i = j;
                    continue;
                }
            }

            if (lang.PercentVariables && c is '%' or '!')
            {
                var close = c;
                var j = i + 1;
                while (j < n && (char.IsLetterOrDigit(code[j]) || code[j] == '_')) j++;
                if (j < n && code[j] == close) j++;
                if (j > i + 1)
                {
                    Emit(i, j - i, VariableBrush);
                    commandPos = false;
                    i = j;
                    continue;
                }
            }

            if (char.IsLetter(c) || c == '_')
            {
                var j = i;
                while (j < n && (char.IsLetterOrDigit(code[j]) || code[j] == '_')) j++;

                if (lang.HyphenatedNames)
                {
                    while (j + 1 < n && code[j] == '-' && (char.IsLetter(code[j + 1]) || code[j + 1] == '_'))
                    {
                        j++;
                        while (j < n && (char.IsLetterOrDigit(code[j]) || code[j] == '_')) j++;
                    }
                }

                var extLen = MatchGreenExtension(code, j, lang);
                if (extLen > 0)
                {
                    Emit(i, (j + extLen) - i, ExecutableBrush);
                    commandPos = false;
                    i = j + extLen;
                    continue;
                }
                var word = code.Substring(i, j - i);
                var brush = ClassifyWord(word, lang);
                if (brush is not null)
                    Emit(i, j - i, brush);
                else
                    Pend(i, j);
                commandPos = false;
                i = j;
                continue;
            }

            if (char.IsDigit(c))
            {
                var j = i;
                while (j < n && (char.IsLetterOrDigit(code[j]) || code[j] == '.')) j++;
                Emit(i, j - i, NumberBrush);
                commandPos = false;
                i = j;
                continue;
            }

            if (IsNewline(c)) commandPos = true;
            else if (!char.IsWhiteSpace(c)) commandPos = false;
            Pend(i, i + 1);
            i++;
        }

        Flush();
        return spans;
    }

    private static bool IsCommandStart(char c) =>
        char.IsLetter(c) || c is '_' or '.' or '\\';

    private static bool HasLetter(string s)
    {
        foreach (var c in s)
            if (char.IsLetter(c)) return true;
        return false;
    }

    private static string LeadingWord(string token)
    {
        var k = 0;
        while (k < token.Length && (char.IsLetterOrDigit(token[k]) || token[k] == '_')) k++;
        return token.Substring(0, k);
    }

    private static Brush? ClassifyWord(string word, Lang lang)
    {
        var dash = word.IndexOf('-');
        if (dash > 0)
            return lang.Verbs.Contains(word.Substring(0, dash)) ? FunctionBrush : null;

        if (lang.Keywords.Contains(word)) return KeywordBrush;
        if (lang.Functions.Contains(word)) return FunctionBrush;
        return null;
    }

    private static int MatchGreenExtension(string s, int pos, Lang lang)
    {
        if (lang.GreenExtensions.Length == 0 || pos >= s.Length || s[pos] != '.') return 0;
        foreach (var ext in lang.GreenExtensions)
        {
            if (pos + ext.Length > s.Length) continue;
            if (string.Compare(s, pos, ext, 0, ext.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
            var after = pos + ext.Length;
            if (after == s.Length || !(char.IsLetterOrDigit(s[after]) || s[after] == '_'))
                return ext.Length;
        }
        return 0;
    }

    private static bool Matches(string s, int i, string token) =>
        i + token.Length <= s.Length && string.CompareOrdinal(s, i, token, 0, token.Length) == 0;

    private static string? MatchLineComment(string s, int i, Lang lang)
    {
        foreach (var p in lang.LineComments)
        {
            if (!Matches(s, i, p)) continue;

            if (char.IsLetter(p[0]))
            {
                var after = i + p.Length;
                if (after < s.Length && (char.IsLetterOrDigit(s[after]) || s[after] == '_')) continue;
                if (i > 0 && (char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')) continue;
            }
            return p;
        }
        return null;
    }

    private sealed class Lang
    {
        public string[] LineComments = Array.Empty<string>();
        public string? BlockStart;
        public string? BlockEnd;
        public char[] StringDelims = Array.Empty<char>();
        public HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Operators = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Functions = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Verbs = new(StringComparer.OrdinalIgnoreCase);
        public string[] GreenExtensions = Array.Empty<string>();
        public Brush? StringColor;
        public Brush? VariableColor;
        public char VariablePrefix = '\0';
        public bool PercentVariables;
        public bool DashOperators;
        public bool HyphenatedNames;
        public bool CommandWords;
        public bool Parameters;
    }

    private static Lang LangFor(string? id) => id switch
    {
        "cmd" => Cmd,
        "powershell" => PowerShell,
        "sql" => Sql,
        "vbs" => Vbs,
        _ => Plain,
    };

    private static readonly Lang Plain = new();

    private static readonly Lang Cmd = new()
    {
        LineComments = new[] { "REM", "::" },
        StringDelims = new[] { '"' },
        PercentVariables = true,
        Parameters = true,
        GreenExtensions = new[] { ".exe", ".msc", ".cpl" },
        Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "echo", "off", "on", "set", "setlocal", "endlocal", "if", "else", "for",
            "in", "do", "goto", "call", "exit", "pause", "cls", "cd", "chdir", "dir",
            "copy", "xcopy", "del", "erase", "move", "ren", "rename", "md", "mkdir",
            "rd", "rmdir", "type", "find", "findstr", "errorlevel", "not", "exist",
            "defined", "start", "title", "color", "choice", "shift", "equ", "neq",
            "lss", "leq", "gtr", "geq", "enabledelayedexpansion", "enableextensions",
            "disabledelayedexpansion", "path", "pushd", "popd", "verify", "rem",
        },
    };

    private static readonly Lang PowerShell = new()
    {
        LineComments = new[] { "#" },
        BlockStart = "<#",
        BlockEnd = "#>",
        StringDelims = new[] { '"', '\'' },
        VariablePrefix = '$',
        DashOperators = true,
        HyphenatedNames = true,
        CommandWords = true,
        Parameters = true,

        VariableColor = Brush(0x6A, 0xC4, 0x6A),
        StringColor = Brush(0x4F, 0xB3, 0xD9),
        Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "function", "filter", "param", "begin", "process", "end", "if", "else",
            "elseif", "switch", "foreach", "for", "while", "do", "until", "break",
            "continue", "return", "try", "catch", "finally", "throw", "trap", "class",
            "enum", "in", "hidden", "static", "using", "namespace", "data", "dynamicparam",
            "workflow", "parallel", "sequence", "exit",
        },
        Operators = new(StringComparer.OrdinalIgnoreCase)
        {
            "eq", "ne", "gt", "ge", "lt", "le", "and", "or", "not", "xor", "band",
            "bor", "bxor", "like", "notlike", "match", "notmatch", "contains",
            "notcontains", "in", "notin", "replace", "split", "join", "is", "isnot",
            "as", "f", "shl", "shr",
        },

        Verbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "Get", "Set", "New", "Remove", "Add", "Clear", "Copy", "Move", "Rename",
            "Write", "Read", "Out", "Format", "Export", "Import", "ConvertTo",
            "ConvertFrom", "Convert", "Select", "Where", "Sort", "Group", "Measure",
            "Compare", "ForEach", "Tee", "Start", "Stop", "Restart", "Suspend",
            "Resume", "Wait", "Invoke", "Test", "Resolve", "Push", "Pop", "Enter",
            "Exit", "Join", "Split", "Receive", "Send", "Update", "Install",
            "Uninstall", "Register", "Unregister", "Enable", "Disable", "Show",
            "Hide", "Find", "Search", "Use", "Open", "Close", "Save", "Lock",
            "Unlock", "Watch", "Connect", "Disconnect",
        },
    };

    private static readonly Lang Sql = new()
    {
        LineComments = new[] { "--" },
        BlockStart = "/*",
        BlockEnd = "*/",
        StringDelims = new[] { '\'' },
        VariablePrefix = '@',
        Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "select", "from", "where", "insert", "into", "values", "update", "set",
            "delete", "create", "alter", "drop", "truncate", "table", "view", "index",
            "trigger", "procedure", "function", "join", "inner", "left", "right",
            "full", "outer", "cross", "on", "as", "and", "or", "not", "null", "is",
            "in", "like", "between", "group", "by", "order", "having", "distinct",
            "top", "limit", "offset", "union", "all", "any", "some", "exists", "case",
            "when", "then", "else", "end", "asc", "desc", "count", "sum", "avg", "min",
            "max", "primary", "key", "foreign", "references", "default", "constraint",
            "unique", "check", "identity", "begin", "commit", "rollback", "transaction",
            "declare", "exec", "execute", "go", "with", "over", "partition", "use",
            "int", "bigint", "smallint", "tinyint", "bit", "decimal", "numeric",
            "float", "real", "money", "char", "varchar", "nchar", "nvarchar", "text",
            "date", "datetime", "datetime2", "time", "timestamp", "uniqueidentifier",
            "cast", "convert", "isnull", "coalesce", "values", "output", "returns",
        },
    };

    private static readonly Lang Vbs = new()
    {
        LineComments = new[] { "'", "REM" },
        StringDelims = new[] { '"' },
        Keywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "dim", "redim", "preserve", "set", "const", "public", "private", "function",
            "sub", "end", "if", "then", "else", "elseif", "for", "each", "next", "to",
            "step", "while", "wend", "do", "loop", "until", "select", "case", "with",
            "call", "exit", "on", "error", "resume", "goto", "option", "explicit",
            "new", "nothing", "null", "empty", "true", "false", "and", "or", "not",
            "xor", "eqv", "imp", "mod", "is", "byval", "byref", "class", "property",
            "get", "let", "default", "stop", "randomize", "erase",
        },

        Functions = new(StringComparer.OrdinalIgnoreCase)
        {
            "MsgBox", "InputBox", "CreateObject", "GetObject", "Array", "IsArray",
            "IsNull", "IsEmpty", "IsNumeric", "IsObject", "IsDate", "CStr", "CInt",
            "CLng", "CDbl", "CSng", "CBool", "CDate", "CByte", "CCur", "Asc", "Chr",
            "Len", "Left", "Right", "Mid", "InStr", "InStrRev", "Replace", "Split",
            "Join", "Filter", "Trim", "LTrim", "RTrim", "UCase", "LCase", "Space",
            "String", "StrComp", "StrReverse", "Now", "Date", "Time", "Year",
            "Month", "Day", "Hour", "Minute", "Second", "Weekday", "WeekdayName",
            "MonthName", "DateAdd", "DateDiff", "DatePart", "DateSerial",
            "DateValue", "TimeSerial", "TimeValue", "FormatNumber", "FormatCurrency",
            "FormatPercent", "FormatDateTime", "Abs", "Int", "Fix", "Round", "Sgn",
            "Sqr", "Rnd", "Exp", "Log", "Sin", "Cos", "Tan", "Atn", "Hex", "Oct",
            "Eval", "Execute", "TypeName", "VarType", "UBound", "LBound",
            "WScript", "Err",
        },
    };
}
