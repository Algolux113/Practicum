using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Application.Interfaces;

namespace Infrastructure.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Prefix = "pbkdf2-sha256-v1:";
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        Span<byte> payload = stackalloc byte[SaltSize + HashSize];
        var salt = payload[..SaltSize];
        RandomNumberGenerator.Fill(salt);
        DeriveKey(password, salt, payload[SaltSize..]);
        return Prefix + Convert.ToHexString(payload);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (passwordHash is null)
            return false;
        if (NeedsRehash(passwordHash))
            return VerifyLegacy(password, passwordHash);

        Span<byte> payload = stackalloc byte[SaltSize + HashSize];
        if (!TryDecode(passwordHash.AsSpan(Prefix.Length), payload))
            return false;

        Span<byte> actual = stackalloc byte[HashSize];
        DeriveKey(password, payload[..SaltSize], actual);
        return CryptographicOperations.FixedTimeEquals(actual, payload[SaltSize..]);
    }

    public bool NeedsRehash(string passwordHash) => !passwordHash.StartsWith(Prefix, StringComparison.Ordinal);

    private static bool VerifyLegacy(string password, string passwordHash)
    {
        Span<byte> expected = stackalloc byte[SHA256.HashSizeInBytes];
        return TryDecode(passwordHash, expected) &&
               CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(password)), expected);
    }

    private static bool TryDecode(ReadOnlySpan<char> hex, Span<byte> destination) =>
        hex.Length == destination.Length * 2 &&
        Convert.FromHexString(hex, destination, out _, out _) == OperationStatus.Done;

    private static void DeriveKey(string password, ReadOnlySpan<byte> salt, Span<byte> destination) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, destination, Iterations, HashAlgorithmName.SHA256);
}
