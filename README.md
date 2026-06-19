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

## Tutorial rápido

**1. Adicionar um input**
Clique com o botão direito em qualquer área vazia da aba → **Novo input**

**2. Editar o conteúdo**
Clique com o botão direito no input → **Editar**. Por padrão o conteúdo é renderizado em Markdown; para mudar o modo, acesse **Bloco de código** ou **Texto puro** no mesmo menu

**3. Adicionar uma label**
Menu de contexto do input → **Adicionar label** → digite o texto → Salvar

**4. Copiar o conteúdo**
Clique com o botão esquerdo no input (modo leitura). Para copiar só um trecho, selecione o texto e use o menu de contexto → **Copiar**

**5. Organizar com abas**
Clique em **+** na régua de abas para criar uma nova aba. Clique com o botão direito na aba para renomear ou excluir

**6. Exportar para compartilhar**
Menu **≡** → **Exportar (.ttm)** → defina uma senha → escolha onde salvar. Para importar em outra máquina: **Importar (.ttm)** → selecione o arquivo → informe a senha

## Build

Requer [.NET 10](https://dotnet.microsoft.com/download) e o [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/).

```
dotnet publish -c Release -r win-x64 --self-contained
```

O executável publicado fica em `publish\`.
