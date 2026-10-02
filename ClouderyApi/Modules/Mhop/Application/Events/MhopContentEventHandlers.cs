using ClouderyApi.Modules.Mhop.Domain.Events;
using ClouderyApi.Modules.Mhop.Infrastructure;
using ClouderyApi.Shared.Domain;

namespace ClouderyApi.Modules.Mhop.Application.Events;

/// <summary>帖子待审核 → 排队送 AI 初筛（火后不理，不阻塞响应）。</summary>
public sealed class PostSubmittedForReviewHandler(MhopContentReviewService review)
    : IDomainEventHandler<PostSubmittedForReview>
{
    public Task HandleAsync(PostSubmittedForReview domainEvent, CancellationToken cancellationToken = default)
    {
        review.QueuePostReview(domainEvent.Post.Id);
        return Task.CompletedTask;
    }
}

/// <summary>回复待审核 → 排队送 AI 初筛（火后不理，不阻塞响应）。</summary>
public sealed class ReplySubmittedForReviewHandler(MhopContentReviewService review)
    : IDomainEventHandler<ReplySubmittedForReview>
{
    public Task HandleAsync(ReplySubmittedForReview domainEvent, CancellationToken cancellationToken = default)
    {
        review.QueueReplyReview(domainEvent.Reply.Id);
        return Task.CompletedTask;
    }
}

/// <summary>
/// 帖子人工放行 → 生成 AI 自动回复。保持同步 await 语义：已有 AI 回复时同步写库，
/// 没有时只排队（与改造前 AdminAppService 的直接调用逐字一致）。
/// </summary>
public sealed class PostPublishedHandler(MhopAiService ai)
    : IDomainEventHandler<PostPublished>
{
    public Task HandleAsync(PostPublished domainEvent, CancellationToken cancellationToken = default)
        => ai.EnsureForumReplyAsync(domainEvent.Post.Id, domainEvent.Post.Content, domainEvent.Post.Crisis);
}
