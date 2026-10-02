using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Infrastructure;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// AI 自动回复写入路径（<see cref="MhopAiService.QueueForumReply"/>）的落库契约：
/// 回复行与 mhop_ai_logs 审计日志必须在同一个显式事务里一起提交
/// （回复 SaveChanges → 日志 SaveChanges → Commit）。
/// 构造函数把 LLM 端点指向不可达地址，让 ForumReplyAsync 走本地兜底，产出确定性文本（engine=local）。
/// </summary>
public sealed class MhopAiReplyWriteTests : IntegrationTestBase
{
    private const string Content = "今天加班到很晚，有点撑不住了";

    public MhopAiReplyWriteTests()
    {
        Environment.SetEnvironmentVariable("Llm__BaseUrl", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("Llm__ApiKey", "contract-test");
    }

    private async Task<int> SeedPublishedPostAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var post = new MhopPost
        {
            UserId = 1,
            Content = Content,
            Board = "mood",
            Status = ContentStatus.Published,
            IsAnonymous = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.MhopPosts.Add(post);
        await db.SaveChangesAsync();
        return post.Id;
    }

    [Fact]
    public async Task QueueForumReply_persists_reply_and_audit_log_together()
    {
        var postId = await SeedPublishedPostAsync();
        var ai = Factory.Services.GetRequiredService<MhopAiService>();

        ai.QueueForumReply(postId, Content, false);

        var reply = await WaitForAiReplyAsync(postId);
        Assert.NotNull(reply);
        Assert.Equal(ContentStatus.Published, reply!.Status);
        Assert.True(reply.IsAi);
        Assert.Equal(postId, reply.PostId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var log = await db.MhopAiLogs.AsNoTracking().SingleAsync(l => l.ReplyId == reply.Id);

        Assert.Equal("forum", log.Module);
        Assert.Equal("local", log.Engine);
        Assert.Equal(Content, log.Prompt);
        Assert.Contains("可以试着这样照顾自己", log.Response);
    }

    [Fact]
    public async Task QueueForumReply_does_not_add_a_second_reply_when_one_exists()
    {
        var postId = await SeedPublishedPostAsync();
        var ai = Factory.Services.GetRequiredService<MhopAiService>();

        ai.QueueForumReply(postId, Content, false);
        var first = await WaitForAiReplyAsync(postId);
        Assert.NotNull(first);

        // 已有 AI 回复时跳过重复生成（MhopAiService.cs:384-388 的守卫）
        ai.QueueForumReply(postId, Content, false);
        await Task.Delay(1500);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var count = await db.MhopReplies.AsNoTracking().CountAsync(r => r.PostId == postId && r.IsAi);

        Assert.Equal(1, count);
    }

    private async Task<MhopReply?> WaitForAiReplyAsync(int postId, int attempts = 100)
    {
        for (var i = 0; i < attempts; i++)
        {
            await Task.Delay(200);
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var reply = await db.MhopReplies.AsNoTracking()
                .FirstOrDefaultAsync(r => r.PostId == postId && r.IsAi);
            if (reply is not null) return reply;
        }

        return null;
    }
}
