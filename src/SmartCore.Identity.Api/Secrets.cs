using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace SmartCore.Identity;

public sealed class Secrets
{
    private readonly byte[] macKey;
    private readonly byte[] materialKey;
    public Secrets(string macKey, string materialKey)
    {
        this.macKey = Convert.FromBase64String(macKey);
        this.materialKey = Convert.FromBase64String(materialKey);
        if (this.macKey.Length != 32 || this.materialKey.Length != 32 || this.macKey.SequenceEqual(this.materialKey))
            throw new InvalidOperationException("Two distinct 32-byte secret keys are required.");
    }
    public static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
    public static string Code() => RandomNumberGenerator.GetInt32(1000000).ToString("D6");
    public byte[] Mac(string purpose, params string[] parts) => HMACSHA256.HashData(macKey,
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { purpose, parts })));
    public static bool Equal(byte[]? left, byte[] right) => left is not null && CryptographicOperations.FixedTimeEquals(left, right);
    public byte[] Seal(string value, string purpose)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var output = new byte[28 + plaintext.Length];
            nonce.CopyTo(output, 0);
            using var aes = new AesGcm(materialKey, 16);
            aes.Encrypt(nonce, plaintext, output.AsSpan(28), output.AsSpan(12,16), Encoding.UTF8.GetBytes(purpose));
            return output;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public string Open(byte[] ciphertext, string purpose)
    {
        var plaintext = new byte[ciphertext.Length - 28];
        try
        {
            using var aes = new AesGcm(materialKey,16);
            aes.Decrypt(ciphertext.AsSpan(0,12), ciphertext.AsSpan(28), ciphertext.AsSpan(12,16), plaintext, Encoding.UTF8.GetBytes(purpose));
            return Encoding.UTF8.GetString(plaintext);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    public static async Task<string> HashPassword(string password)
    {
        var bytes = Encoding.UTF8.GetBytes(password);
        try
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            using var argon = new Argon2id(bytes) { Salt = salt, MemorySize = 65536, Iterations = 3, DegreeOfParallelism = 4 };
            var hash = await argon.GetBytesAsync(32);
            return $"$argon2id$v=19$m=65536,t=3,p=4${Convert.ToBase64String(salt).TrimEnd('=')}${Convert.ToBase64String(hash).TrimEnd('=')}";
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
