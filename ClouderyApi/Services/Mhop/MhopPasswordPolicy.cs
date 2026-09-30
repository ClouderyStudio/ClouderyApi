using System.Text.RegularExpressions;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 密码复杂度统一规则：8-64 位，必须同时包含大写字母 + 小写字母 + 数字（三类）；特殊字符可选。
/// 邮箱首次登录的自动注册（GenerateRandomPassword）绕过此策略（系统生成，不暴露给用户选择）。
/// </summary>
public static class MhopPasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 64;

    private static readonly Regex Upper = new("[A-Z]", RegexOptions.Compiled);
    private static readonly Regex Lower = new("[a-z]", RegexOptions.Compiled);
    private static readonly Regex Digit = new(@"\d", RegexOptions.Compiled);

    /// <summary>返回 null 表示通过；否则返回可读错误文案（供 400 详情字段展示）。</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password)) return "密码不能为空";
        if (password.Length < MinLength) return $"密码至少 {MinLength} 位";
        if (password.Length > MaxLength) return $"密码最多 {MaxLength} 位";

        var hasUpper = Upper.IsMatch(password);
        var hasLower = Lower.IsMatch(password);
        var hasDigit = Digit.IsMatch(password);

        if (!hasUpper || !hasLower || !hasDigit)
            return "密码需同时包含大写字母、小写字母和数字";

        return null;
    }

    /// <summary>前端提示用的策略摘要文案。</summary>
    public const string Hint =
        "密码 8-64 位，必须同时包含大写字母、小写字母和数字（三类）";
}
