namespace totem;

// ── Modelo de dados serializável (vira JSON antes de criptografar no .ttm) ──────

public sealed class TotemDocument
{
    // Versão do formato. Incrementar quando a estrutura mudar de forma incompatível;
    // a carga recusa documentos com versão maior que esta (criados por app mais novo).
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<TotemTab> Tabs { get; set; } = new();
    public int SelectedTab { get; set; } // índice da aba ativa
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
    public bool IsPlainText { get; set; }
    public bool IsSeparator { get; set; }
    public bool IsImage { get; set; }
    public string? ImageData { get; set; } // Base64 da imagem
}

// ── Linguagens disponíveis para o modo "bloco de código" ───────────────────────

public sealed record CodeLanguage(string Id, string Name, string Skeleton);

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
    };

    public static CodeLanguage? ById(string? id) =>
        id is null ? null : All.FirstOrDefault(l => l.Id == id);
}
