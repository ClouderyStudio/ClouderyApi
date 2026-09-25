using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>公共接口：健康检查、热线信息、在线心跳（对应 Python 后端 routers/common.py 与 main.py 的 /api/health，本实现挂载于 /mhop）。</summary>
[ApiController]
[Route("mhop")]
public class MhopCommonController : MhopControllerBase
{
    private readonly MhopOnlineTracker _online;
    private readonly MhopOptions _options;
    private List<HotlineOut>? _hotlines;

    public MhopCommonController(MhopOnlineTracker online, IOptions<MhopOptions> options)
    {
        _online = online;
        _options = options.Value;
    }

    [HttpGet("health")]
    public IActionResult Health() => MhopOk(new { status = "ok", app = _options.AppName });

    [HttpGet("hotlines")]
    public IActionResult Hotlines() => MhopOk(_hotlines ??= BuildHotlines());

    [HttpPost("online/heartbeat")]
    public IActionResult Heartbeat([FromBody] HeartbeatIn body)
    {
        var key = (body.Key ?? string.Empty).Trim();
        if (key.Length > 64) key = key[..64];
        if (key.Length == 0) key = "anon";
        return MhopOk(new { online = _online.Heartbeat(key) });
    }

    [HttpGet("online/count")]
    public IActionResult Count() => MhopOk(new { online = _online.Count() });

    /// <summary>紧急援助热线（首页置顶 + 危机场景强制提示）。</summary>
    private List<HotlineOut> BuildHotlines()
    {
        var list = new List<HotlineOut>
        {
            new()
            {
                Name = "全国心理援助热线", Phone = "12356", Tag = "全国通用 · 24小时",
                Description = "国家卫健委统一心理援助热线，免费、保密", Level = "national",
            },
            new()
            {
                Name = "北京心理危机研究与干预中心", Phone = "010-82951332", Tag = "危机干预 · 24小时",
                Description = "面向自杀/自伤等紧急心理危机的专业干预热线", Level = "national",
            },
            new()
            {
                Name = "紧急报警 / 医疗急救", Phone = "110 / 120", Tag = "生命受到直接威胁时",
                Description = "若你或身边人正处在立即危险中，请第一时间拨打", Level = "emergency",
            },
        };

        if (!string.IsNullOrWhiteSpace(_options.LekeHotline))
        {
            list.Add(new HotlineOut
            {
                Name = "北京乐科心理干预研究院专属援助电话", Phone = _options.LekeHotline,
                Tag = "合作机构专线", Description = "平台合作研究院专属援助通道", Level = "partner",
            });
        }

        return list;
    }
}
