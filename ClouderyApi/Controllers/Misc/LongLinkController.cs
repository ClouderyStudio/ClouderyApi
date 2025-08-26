using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Drawing;
using System.Text;

namespace ClouderyApi.Controllers.Misc
{
    [Route("misc/[controller]")]
    [ApiController]
    public class LongLinkController : ControllerBase
    {
        [HttpGet]
        [Route("gen/{originLink}")]
        public string GetGeneratedLongLink(string originLink)
        {
            byte[] data = Encoding.Unicode.GetBytes(originLink);
            StringBuilder result = new StringBuilder(data.Length * 8);

            foreach (byte b in data)
            {
                result.Append(Convert.ToString(b, 2).PadLeft(8, '0'));
            }
            return "https://loooooooong.a.8.9.a.f.f.0.7.0.0.6.2.ip6.arpa/" + result.ToString().Replace("0","y").Replace("1","s");
        }

        [HttpGet]
        [Route("jump/{decodedOriginLink}")]
        public IActionResult RedirectToOriginLink(string decodedOriginLink)
        {
            System.Text.RegularExpressions.CaptureCollection cs =
                System.Text.RegularExpressions.Regex.Match(decodedOriginLink.Replace("y", "0").Replace("s", "1"), @"([01]{8})+").Groups[1].Captures;
            byte[] data = new byte[cs.Count];
            for (int i = 0; i < cs.Count; i++)
            {
                data[i] = Convert.ToByte(cs[i].Value, 2);
            }
            return Redirect(Encoding.Unicode.GetString(data, 0, data.Length).Replace("%2F","/"));
        }
    }
}
