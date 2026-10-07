using System.Net;
using ClouderyApi.Modules.Mhop.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests.TestSupport;

/// <summary>
/// 集成测试里给账号补齐「邮箱已通过验证码验证」门槛。
/// 验证码直接经 <see cref="MhopEmailCodeService"/> 生成——仓库内的 appsettings.json 配了真实 SMTP，
/// 走 POST /mhop/auth/email-code 会真的发信，因此测试只借服务取码；
/// 绑定与校验仍走真实接口 PUT /mhop/auth/me/email。
/// </summary>
public static class MhopEmailVerify
{
    /// <summary>为指定 token 的账号绑定并验证邮箱；返回验证码用于断言。</summary>
    public static async Task<string> VerifyEmailAsync(
        ClouderyApiFactory factory,
        HttpClient client,
        string token,
        string email)
    {
        var codes = factory.Services.GetRequiredService<MhopEmailCodeService>();
        var issued = await codes.IssueCodeAsync(email);
        Assert.NotNull(issued.Code);

        var (status, body) = await JsonHttp.PutJsonAsync(client, "/mhop/auth/me/email",
            new { email, code = issued.Code }, token);
        Assert.Equal(HttpStatusCode.OK, status);
        return issued.Code!;
    }
}
