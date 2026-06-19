using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace totem;

/// <summary>
/// Cópia de imagens para a área de transferência.
///
/// O histórico do Windows (Win+V) só registra bitmaps e ignora qualquer cópia que
/// contenha arquivos (CF_HDROP) — não dá para ter imagem-no-histórico e colar-como-arquivo
/// na mesma cópia. Por isso há duas operações:
///  • <see cref="CopyImageAsync"/>  — só bitmap; entra no histórico (ação padrão).
///  • <see cref="CopyAsFileAsync"/> — arquivo + bitmap; cola no Explorer, mas fora do histórico.
///
/// O bitmap é montado a partir de um PNG temporário (referência por arquivo é a forma
/// confiável no WinUI 3; a partir de stream em memória falha de forma intermitente).
/// <see cref="Clipboard.Flush"/> materializa o conteúdo na área de transferência do SO
/// (necessário para o bitmap aparecer no histórico e sobreviver ao fechamento do app).
///
/// Ciclo de vida do temporário: quando o bitmap é materializado pelo Flush, o arquivo
/// vira descartável e é apagado na hora. Quando há CF_HDROP (cópia como arquivo) ou o
/// Flush falha, o arquivo precisa sobreviver enquanto estiver na área de transferência —
/// mantemos um só por vez e apagamos o anterior na cópia seguinte. <see cref="PurgeLeftovers"/>
/// limpa sobras de sessões passadas na inicialização.
/// </summary>
public static class ImageClipboard
{
    private static readonly string Pasta =
        Path.Combine(Path.GetTempPath(), "aec.totem", "clip");

    // Arquivo ainda referenciado pela área de transferência (delay-render ou CF_HDROP).
    private static string? _arquivoAtivo;

    /// <summary>Remove temporários deixados por sessões anteriores. Chamar na inicialização.</summary>
    public static void PurgeLeftovers()
    {
        try
        {
            if (Directory.Exists(Pasta))
                Directory.Delete(Pasta, recursive: true);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Copia como imagem (bitmap). Aparece no histórico (Win+V).</summary>
    public static async Task CopyImageAsync(byte[] png)
    {
        var caminho = await WriteTempAsync(png);
        var pkg = await BuildPackageAsync(caminho, includeFile: false);
        Clipboard.SetContentWithOptions(pkg, new ClipboardContentOptions { IsAllowedInHistory = true });

        // Flush materializa o bitmap no SO; aí o temporário não é mais necessário.
        var flushed = TryFlush();
        SetActiveFile(flushed ? null : caminho); // se não materializou, o arquivo segue sendo a fonte
        if (flushed) TryDelete(caminho);
    }

    /// <summary>Copia como arquivo (e também como imagem). Não entra no histórico.</summary>
    public static async Task CopyAsFileAsync(byte[] png)
    {
        var caminho = await WriteTempAsync(png);
        var pkg = await BuildPackageAsync(caminho, includeFile: true);
        Clipboard.SetContent(pkg);
        TryFlush();
        SetActiveFile(caminho); // o CF_HDROP aponta para o arquivo: mantê-lo vivo
    }

    private static async Task<string> WriteTempAsync(byte[] png)
    {
        Directory.CreateDirectory(Pasta);
        var caminho = Path.Combine(Pasta, $"img_{Guid.NewGuid():N}.png");
        await File.WriteAllBytesAsync(caminho, png);
        return caminho;
    }

    private static async Task<DataPackage> BuildPackageAsync(string path, bool includeFile)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        if (includeFile) pkg.SetStorageItems(new[] { file });
        pkg.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        return pkg;
    }

    // Substitui o arquivo ativo e apaga o anterior (já saiu da área de transferência).
    private static void SetActiveFile(string? caminho)
    {
        var anterior = _arquivoAtivo;
        _arquivoAtivo = caminho;
        if (anterior is not null && anterior != caminho) TryDelete(anterior);
    }

    private static bool TryFlush()
    {
        try { Clipboard.Flush(); return true; }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* em uso pelo SO; será limpo na próxima inicialização */ }
    }
}
