using System.Security.Claims;
using ClouderyApi.Models.Cloudery.DTOs;
using ClouderyApi.Services.Cloudery;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Controllers.Cloudery;

/// <summary>
/// 登录用户在云端保存的测评结果：站点本机存档之外的第二份副本，
/// 让同一个 Casdoor 账号在手机 / 电脑 / 其它平台上看到同一份历史。
///
/// 一律按当前登录用户过滤，不做任何跨用户读取；未登录返回 401 裸对象。
/// 响应沿用 Cloudery 模块的裸对象风格（不用 MhopOk 的蛇形包装）。
/// </summary>
[ApiController]
[Route("exam/results")]
public sealed class ExamResultsController(ExamResultService service) : ControllerBase
{
    /// <summary>当前用户的云端记录（新的在前）</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return NotLoggedIn();

        var results = await service.ListAsync(userId, cancellationToken);
        return Ok(new { success = true, total = results.Count, results });
    }

    /// <summary>上传一条记录（已存在则按存档时间决定是否覆盖），返回云端全量记录</summary>
    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ExamResultIn body, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return NotLoggedIn();
        if (body is null) return Rejected("缺少请求体");

        return await SyncCore(userId, new List<ExamResultIn> { body }, cancellationToken);
    }

    /// <summary>批量同步：把本机记录并入云端，并取回云端全量记录用于补全本机存档</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> Sync([FromBody] ExamResultSyncIn body, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return NotLoggedIn();

        var records = body?.Records;
        if (records is null || records.Count == 0)
        {
            // 只取回、不上传也是合法用法（换设备后第一次打开页面就是这种情形）
            var only = await service.ListAsync(userId, cancellationToken);
            return Ok(new ExamResultSyncOut { Success = true, Uploaded = 0, Total = only.Count, Results = only });
        }

        return await SyncCore(userId, records, cancellationToken);
    }

    /// <summary>删除一条记录（云端 Id 或站点本机记录键）</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return NotLoggedIn();

        var deleted = await service.DeleteAsync(userId, id, cancellationToken);
        if (!deleted) return NotFound(new { success = false, message = "记录不存在" });

        return Ok(new { success = true, message = "已删除" });
    }

    /// <summary>清空当前用户的云端记录</summary>
    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return NotLoggedIn();

        var deleted = await service.ClearAsync(userId, cancellationToken);
        return Ok(new { success = true, deleted, message = "云端记录已清空" });
    }

    private async Task<IActionResult> SyncCore(Guid userId, List<ExamResultIn> records, CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.SyncAsync(userId, records, cancellationToken);
            return Ok(result);
        }
        catch (ExamResultRejectedException ex)
        {
            return Rejected(ex.Message);
        }
    }

    /// <summary>当前登录用户的本地 Id：Casdoor 回调建立会话时写入 NameIdentifier</summary>
    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        if (User.Identity?.IsAuthenticated != true) return false;

        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(raw, out userId);
    }

    private IActionResult NotLoggedIn() =>
        Unauthorized(new { success = false, message = "未登录，无法使用云端同步" });

    private IActionResult Rejected(string message) =>
        BadRequest(new { success = false, message });
}
