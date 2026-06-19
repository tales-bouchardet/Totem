using System.Security.Cryptography;
using System.Text;

namespace totem;

/// <summary>
/// Formato do arquivo .ttm (binário sigiloso):
///   [4]  magic  "TTM1"
///   [16] salt   (PBKDF2)
///   [12] nonce  (AES-GCM)
///   [16] tag    (autenticação AES-GCM)
///   [..] ciphertext
///
/// A senha informada na exportação é a própria chave: deriva-se uma chave
/// AES-256 via PBKDF2/SHA-256. Como o GCM é autenticado, importar com a senha
/// errada (ou com o arquivo adulterado) lança e a importação é recusada.
/// </summary>
public static class TotemCrypto
{
    private static readonly byte[] Magic = "TTM1"u8.ToArray();
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 200_000;

    public static byte[] Encrypt(string plainText, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        var plain = Encoding.UTF8.GetBytes(plainText);
        try
        {
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
                aes.Encrypt(nonce, plain, cipher, tag);

            using var ms = new MemoryStream(Magic.Length + SaltSize + NonceSize + TagSize + cipher.Length);
            ms.Write(Magic);
            ms.Write(salt);
            ms.Write(nonce);
            ms.Write(tag);
            ms.Write(cipher);
            return ms.ToArray();
        }
        finally
        {
            // Não deixa a chave derivada nem o texto-puro perdurarem na memória.
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Lança <see cref="CryptographicException"/> se a senha estiver errada ou o arquivo for inválido.</summary>
    public static string Decrypt(byte[] data, string password)
    {
        var minimum = Magic.Length + SaltSize + NonceSize + TagSize;
        if (data.Length < minimum || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new CryptographicException("Arquivo .ttm inválido.");

        var offset = Magic.Length;
        var salt = data.AsSpan(offset, SaltSize).ToArray(); offset += SaltSize;
        var nonce = data.AsSpan(offset, NonceSize).ToArray(); offset += NonceSize;
        var tag = data.AsSpan(offset, TagSize).ToArray(); offset += TagSize;
        var cipher = data.AsSpan(offset).ToArray();

        var key = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

        var plain = new byte[cipher.Length];
        try
        {
            using (var aes = new AesGcm(key, TagSize))
                aes.Decrypt(nonce, cipher, tag, plain); // AuthenticationTagMismatch -> senha incorreta

            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
