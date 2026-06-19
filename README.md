# Totem

Bloco de notas pessoal para Windows com suporte a Markdown, blocos de código com syntax highlighting e imagens. Organizado em abas, salvo automaticamente e protegido por criptografia.

![screenshot](https://github.com/user-attachments/assets/ebfc9e2d-8692-4784-b602-0be811eca46d)

## Features

- **Abas** — crie quantas abas quiser, renomeie e reordene arrastando
- **Markdown renderizado** — edite em texto e veja renderizado ao sair (negrito, tabelas, listas, tachado, etc.)
- **Blocos de código** com syntax highlighting e numeração de linhas: CMD/Batch, PowerShell, SQL e VBScript
- **Imagens** — cole da área de transferência (`Ctrl+V` no modo edição) ou importe por arquivo
- **Labels** — pílulas coloridas sobre cada input para identificar o conteúdo
- **Separadores** — divisores visuais entre os inputs
- **Copiar com um clique** — clique em qualquer input para copiar o conteúdo; borda pisca e aparece "Copiado!"
- **Autosave automático** — estado salvo 600 ms após cada mudança, criptografado com DPAPI (só o usuário atual lê)
- **Exportar/Importar `.ttm`** — arquivo portátil com senha; criptografado com AES-256-GCM + PBKDF2 (200 000 iterações)

## Build

Requer [.NET 10](https://dotnet.microsoft.com/download) e o [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/).

```
otnet publish -c Release -r win-x64 -o publish
vpk pack --packId aec.totem --packVersion 1.0.0 --packDir publish --outputDir Totem_VPK --icon icon.ico --mainExe aec.totem.exe
```

O executável publicado fica em `publish\`.
