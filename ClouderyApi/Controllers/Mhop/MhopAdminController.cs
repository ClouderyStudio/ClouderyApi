using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using ClouderyApi.Modules.Mhop.Application;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// 管理后台：数据看板、帖子巡检、回复审核、AI 回复撤回/恢复、用户管理、AI 日志。
/// 对应 Python 后端 routers/admin.py，全路由要求管理员权限。
/// 本控制器只做 HTTP 绑定与 MhopOk 包装，用例编排见 AdminAppService，映射见 MhopAdminMapper。
/// </summary>
[ApiController]
[Route("mhop/admin")]
[MhopAdmin]
public class MhopAdminController : MhopControllerBase
{
    private readonly AdminAppService _admin;

    public MhopAdminController(AdminAppService admin)
    {
        _admin = admin;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats() => MhopOk(await _admin.StatsAsync());

    [HttpGet("posts")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> ListPosts([FromQuery] int? status = null, [FromQuery] string? flag = null)
        => MhopOk(await _admin.ListPostsAsync(status, flag));

    [HttpPost("posts/{postId:int}/moderate")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> ModeratePost(int postId, [FromBody] ModerateIn body)
        => MhopOk(await _admin.ModeratePostAsync(postId, body));

    /// <summary>强制重新生成某帖的 AI 自动回复：先清理旧回复，再调用大模型生成一条新的。</summary>
    [HttpPost("posts/{postId:int}/ai-reply/regenerate")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> RegenerateAiReply(int postId)
        => MhopOk(await _admin.RegenerateAiReplyAsync(postId));

    /// <summary>管理员删除帖子：不限作者与状态，连同其全部回复、点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("posts/{postId:int}")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> DeletePost(int postId)
        => MhopOk(await _admin.DeletePostAsync(postId, HttpContext.RequestAborted));

    [HttpGet("replies")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> ListReplies([FromQuery] int? status = null, [FromQuery] string? flag = null)
        => MhopOk(await _admin.ListRepliesAsync(status, flag));

    [HttpPost("replies/{replyId:int}/moderate")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> ModerateReply(int replyId, [FromBody] ModerateIn body)
        => MhopOk(await _admin.ModerateReplyAsync(replyId, body));

    /// <summary>撤回 AI 回复：对所有用户即时隐藏正文，保留内容与原因以备审计，可恢复。</summary>
    [HttpPost("replies/{replyId:int}/recall")]
    [MhopPerm(MhopAdminPermissions.Review, MhopAdminPermissions.AiLogs)]
    public async Task<IActionResult> RecallReply(int replyId, [FromBody] RecallIn body)
        => MhopOk(await _admin.RecallReplyAsync(replyId, body));

    /// <summary>恢复被撤回的 AI 回复，重新公开展示。</summary>
    [HttpPost("replies/{replyId:int}/restore")]
    [MhopPerm(MhopAdminPermissions.Review, MhopAdminPermissions.AiLogs)]
    public async Task<IActionResult> RestoreReply(int replyId)
        => MhopOk(await _admin.RestoreReplyAsync(replyId));

    /// <summary>管理员删除回复：不限作者与状态，连同其点赞、AI 日志与图片一并清理。</summary>
    [HttpDelete("replies/{replyId:int}")]
    [MhopPerm(MhopAdminPermissions.Review)]
    public async Task<IActionResult> DeleteReply(int replyId)
        => MhopOk(await _admin.DeleteReplyAsync(replyId, HttpContext.RequestAborted));

    [HttpGet("users")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> ListUsers() => MhopOk(await _admin.ListUsersAsync());

    [HttpPost("users/{userId:int}/status")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> SetUserStatus(int userId, [FromBody] StatusIn body)
        => MhopOk(await _admin.SetUserStatusAsync(userId, body));

    /// <summary>设置用户标识。badge 为空字符串表示清除标识。</summary>
    [HttpPost("users/{userId:int}/badge")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> SetUserBadge(int userId, [FromBody] BadgeIn body)
        => MhopOk(await _admin.SetUserBadgeAsync(userId, body));

    /// <summary>
    /// 角色管理（仅超级管理员）：
    /// promote 普通用户→普通管理员（可同时带模块权限）；demote 普通管理员→普通用户；
    /// promote_super 指定超级管理员；demote_super 超管降为普通管理员（保留其模块授权记录）。
    /// </summary>
    [HttpPost("users/{userId:int}/role")]
    [MhopSuper]
    public async Task<IActionResult> SetUserRole(int userId, [FromBody] RoleIn body)
        => MhopOk(await _admin.SetUserRoleAsync(userId, body));

    /// <summary>管理员重置用户密码。</summary>
    [HttpPost("users/{userId:int}/reset-password")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> ResetUserPassword(int userId, [FromBody] ResetPasswordIn body)
        => MhopOk(await _admin.ResetUserPasswordAsync(userId, body));

    /// <summary>管理员删除用户：连同其名下帖子、回复、点赞、AI 日志与图片一并清理，不可恢复。</summary>
    [HttpDelete("users/{userId:int}")]
    [MhopPerm(MhopAdminPermissions.Users)]
    public async Task<IActionResult> DeleteUser(int userId)
        => MhopOk(await _admin.DeleteUserAsync(userId, HttpContext.RequestAborted));

    // ---------------- 模块权限分配（超级管理员） ----------------

    [HttpGet("users/{userId:int}/permissions")]
    [MhopSuper]
    public async Task<IActionResult> GetPermissions(int userId)
        => MhopOk(await _admin.GetPermissionsAsync(userId));

    [HttpPut("users/{userId:int}/permissions")]
    [MhopSuper]
    public async Task<IActionResult> SetPermissions(int userId, [FromBody] PermissionsIn body)
        => MhopOk(await _admin.SetPermissionsAsync(userId, body));

    [HttpGet("ai-logs")]
    [MhopPerm(MhopAdminPermissions.AiLogs)]
    public async Task<IActionResult> ListAiLogs() => MhopOk(await _admin.ListAiLogsAsync());
}
