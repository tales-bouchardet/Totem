namespace totem;

public sealed class TotemDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<TotemTab> Tabs { get; set; } = new();
    public int SelectedTab { get; set; }
}

public sealed class TotemTab
{
    public string Name { get; set; } = "Aba";
    public List<TotemItem> Items { get; set; } = new();
}

public sealed class TotemItem
{
    public string? Label { get; set; }
    public string Content { get; set; } = "";
    public bool IsCode { get; set; }
    public string? Language { get; set; }
    public bool IsPlainText { get; set; } = true;
    public bool IsSeparator { get; set; }
    public bool IsImage { get; set; }
    public string? ImageData { get; set; }
}

public sealed class CodeLanguage
{
    public string Id { get; }
    public string Name { get; }
    public string Skeleton { get; }

    public CodeLanguage(string id, string name, string skeleton)
    {
        Id = id;
        Name = name;
        Skeleton = skeleton;
    }
}

public static class CodeLanguages
{
    public static readonly CodeLanguage[] All =
    {
        new("cmd", "CMD (Batch)",
            "@echo off\nsetlocal\n\n"),
        new("powershell", "PowerShell",
            "param()\n\n"),
        new("sql", "SQL",
            "SELECT *\nFROM tabela\nWHERE 1 = 1;"),
        new("vbs", "VBScript",
            "Option Explicit\n\n"),
        new("password", "Senha", ""),
    };

    public static CodeLanguage? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(l => l.Id == id);
}
