using System.IO;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace totem;

public static class TotemCrypto
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TTM1");
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 200_000;

    public static byte[] Encrypt(string plainText, string password)
    {
        var salt = RandomBytes(SaltSize);
        var nonce = RandomBytes(NonceSize);
        var key = DeriveKey(password, salt);
        var plain = Encoding.UTF8.GetBytes(plainText);
        try
        {
            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(true, new AeadParameters(new KeyParameter(key), TagSize * 8, nonce));
            var output = new byte[cipher.GetOutputSize(plain.Length)];
            var len = cipher.ProcessBytes(plain, 0, plain.Length, output, 0);
            cipher.DoFinal(output, len);

            var cipherText = new byte[plain.Length];
            var tag = new byte[TagSize];
            Array.Copy(output, 0, cipherText, 0, plain.Length);
            Array.Copy(output, plain.Length, tag, 0, TagSize);

            using var ms = new MemoryStream(Magic.Length + SaltSize + NonceSize + TagSize + cipherText.Length);
            ms.Write(Magic, 0, Magic.Length);
            ms.Write(salt, 0, salt.Length);
            ms.Write(nonce, 0, nonce.Length);
            ms.Write(tag, 0, tag.Length);
            ms.Write(cipherText, 0, cipherText.Length);
            return ms.ToArray();
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
            Array.Clear(plain, 0, plain.Length);
        }
    }

    public static string Decrypt(byte[] data, string password)
    {
        var minimum = Magic.Length + SaltSize + NonceSize + TagSize;
        if (data.Length < minimum || !StartsWith(data, Magic))
            throw new CryptographicException("Arquivo .ttm inválido.");

        var offset = Magic.Length;
        var salt = Slice(data, offset, SaltSize); offset += SaltSize;
        var nonce = Slice(data, offset, NonceSize); offset += NonceSize;
        var tag = Slice(data, offset, TagSize); offset += TagSize;
        var cipherLen = data.Length - offset;

        var cipherAndTag = new byte[cipherLen + TagSize];
        Array.Copy(data, offset, cipherAndTag, 0, cipherLen);
        Array.Copy(tag, 0, cipherAndTag, cipherLen, TagSize);

        var key = DeriveKey(password, salt);
        try
        {
            var cipher = new GcmBlockCipher(new AesEngine());
            cipher.Init(false, new AeadParameters(new KeyParameter(key), TagSize * 8, nonce));
            var output = new byte[cipher.GetOutputSize(cipherAndTag.Length)];
            try
            {
                var len = cipher.ProcessBytes(cipherAndTag, 0, cipherAndTag.Length, output, 0);
                len += cipher.DoFinal(output, len);
                return Encoding.UTF8.GetString(output, 0, len);
            }
            catch (InvalidCipherTextException)
            {
                throw new CryptographicException("Senha incorreta ou arquivo corrompido.");
            }
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        var generator = new Pkcs5S2ParametersGenerator(new Sha256Digest());
        generator.Init(Encoding.UTF8.GetBytes(password), salt, Iterations);
        var key = (KeyParameter)generator.GenerateDerivedMacParameters(KeySize * 8);
        return key.GetKey();
    }

    private static byte[] RandomBytes(int size)
    {
        var bytes = new byte[size];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        for (var i = 0; i < prefix.Length; i++)
            if (data[i] != prefix[i]) return false;
        return true;
    }

    private static byte[] Slice(byte[] data, int start, int length)
    {
        var result = new byte[length];
        Array.Copy(data, start, result, 0, length);
        return result;
    }
}
