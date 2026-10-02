using ClouderyApi.Shared.Domain;

namespace ClouderyApi.Modules.Mhop.Domain.Events;

/// <summary>
/// 内容进入待审核：新建帖子 / 重新提审 / 编辑待审内容时登记。
/// 载荷带实体引用而非 Id：新建实体的自增 Id 在 SaveChanges 之前不可用，
/// 订阅者在提交后读 <c>Post.Id</c> 才正确。
/// </summary>
public sealed record PostSubmittedForReview(MhopPost Post) : IDomainEvent;

/// <summary>回复进入待审核：新建回复 / 重新提审 / 编辑待审回复时登记。</summary>
public sealed record ReplySubmittedForReview(MhopReply Reply) : IDomainEvent;

/// <summary>帖子经人工审核放行；订阅者据此生成（或沿用）AI 自动回复。</summary>
public sealed record PostPublished(MhopPost Post) : IDomainEvent;
