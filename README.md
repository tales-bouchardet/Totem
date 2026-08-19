# Totem

Bloco de notas pessoal para Windows com suporte a Markdown, blocos de código com syntax highlighting e imagens. Organizado em abas, salvo automaticamente e protegido por criptografia.

<img width="852" height="737" alt="image" src="https://github.com/user-attachments/assets/43bebdcb-0ab8-4be9-8b02-f1785a15395a" />


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

WPF sobre .NET Framework 4.6.2 (via [WPF-UI](https://github.com/lepoco/wpfui)), sem pré-requisitos além do próprio Windows.

```
dotnet publish -c Release -o publish
```

O executável publicado fica em `publish\totem.exe`.
