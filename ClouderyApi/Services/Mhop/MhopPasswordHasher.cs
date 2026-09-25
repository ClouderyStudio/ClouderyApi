using System.Security.Cryptography;
using System.Text;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 密码哈希（PBKDF2-HMAC-SHA256，120000 轮），格式与 Python 后端完全一致：
/// pbkdf2_sha256$轮数$盐hex$散列hex。便于后续从旧库迁移存量用户。
/// </summary>
public sealed class MhopPasswordHasher
{
    private const int Rounds = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Rounds, HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2_sha256${Rounds}${Convert.ToHexString(salt).ToLowerInvariant()}${Convert.ToHexString(derived).ToLowerInvariant()}";
    }

    public bool Verify(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2_sha256") return false;
            var rounds = int.Parse(parts[1]);
            var salt = Convert.FromHexString(parts[2]);
            var expected = Convert.FromHexString(parts[3]);
            var derived = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, rounds, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(derived, expected);
        }
        catch
        {
            return false;
        }
    }
}
