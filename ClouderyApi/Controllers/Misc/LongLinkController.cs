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
        var cs =
            Regex.Match(decodedOriginLink.Replace("y", "0").Replace("s", "1"), @"([01]{8})+").Groups[1].Captures;
        var data = new byte[cs.Count];
        for (var i = 0; i < cs.Count; i++) data[i] = Convert.ToByte(cs[i].Value, 2);
        return Redirect(Encoding.Unicode.GetString(data, 0, data.Length).Replace("%2F", "/"));
    }
}