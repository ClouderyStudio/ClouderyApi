using System.Security.Cryptography;
using System.Text;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 隐私插件的访问口令哈希（PBKDF2-HMAC-SHA256，120000 轮）。
///
/// 与 <c>MhopPasswordHasher</c> 同算法同格式（<c>pbkdf2_sha256$轮数$盐hex$散列hex</c>），
/// 但**刻意不复用那个类**：MHOP 的是用户登录口令，这里是作者给单个插件设的分享口令，
/// 生命周期与轮换节奏都不同，绑在一起会让任一边的策略调整波及另一边。
///
/// 口令**只存哈希**。明文只在作者提交那一刻存在于内存，任何接口（包括作者本人与超管）
/// 都取不回来 —— 忘记了只能改成新口令。
/// </summary>
public sealed class ScforgeAccessHasher
{
    private const int Rounds = 120_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>计算哈希。调用方需先自行校验长度（见 <see cref="ScforgeAccessMode"/>）。</summary>
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Rounds, HashAlgorithmName.SHA256, HashBytes);
        return $"pbkdf2_sha256${Rounds}${Convert.ToHexString(salt).ToLowerInvariant()}${Convert.ToHexString(derived).ToLowerInvariant()}";
    }

    /// <summary>校验口令。格式被篡改或解析失败一律按不匹配处理，不抛异常。</summary>
    public bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

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
