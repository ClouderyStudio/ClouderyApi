using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.RegularExpressions;

namespace ClouderyApi.Controllers.Misc;

[Route("misc/[controller]")]
[ApiController]
public class LongLinkController : ControllerBase
{
    [HttpGet]
    [Route("gen/{originLink}")]
    public string GetGeneratedLongLink(string originLink)
    {
        var data = Encoding.Unicode.GetBytes(originLink);
        var result = new StringBuilder(data.Length * 8);

        foreach (var b in data) result.Append(Convert.ToString(b, 2).PadLeft(8, '0'));
        return "https://loooooooong.a.8.9.a.f.f.0.7.0.0.6.2.ip6.arpa/" +
               result.ToString().Replace("0", "y").Replace("1", "s");
    }

    [HttpGet]
    [Route("jump/{decodedOriginLink}")]
    public IActionResult RedirectToOriginLink(string decodedOriginLink)
    {
        var encoded = decodedOriginLink.Replace("y", "0").Replace("s", "1");

        // 严格匹配整串：必须以 8 位二进制组开头并覆盖到结尾，防止截断/错位解码
        var match = Regex.Match(encoded, @"^(?:([01]{8}))+$");
        if (!match.Success || encoded.Length % 8 != 0)
            return BadRequest(new { success = false, message = "无效的跳转链接" });

        var cs = match.Groups[1].Captures;
        var data = new byte[cs.Count];
        for (var i = 0; i < cs.Count; i++) data[i] = Convert.ToByte(cs[i].Value, 2);

        var target = Encoding.Unicode.GetString(data, 0, data.Length).Replace("%2F", "/");

        // 安全校验：只允许 http/https 绝对地址，禁止 javascript:、data:、file: 等危险 scheme
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return BadRequest(new { success = false, message = "跳转目标不合法" });

        return Redirect(target);
    }
}