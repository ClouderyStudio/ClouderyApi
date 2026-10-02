namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>
/// 帖子 / 回复的内容状态（mhop_posts.status / mhop_replies.status 的 int 列）。
/// 单一事实来源：控制器、审核服务与领域方法都只引用这里，不再各自定义常量。
/// </summary>
public static class ContentStatus
{
    /// <summary>待审核：AI 未放行或仍在审核中，仅作者本人可见。</summary>
    public const int Pending = 0;

    /// <summary>已通过审核，公开可见。</summary>
    public const int Published = 1;

    /// <summary>已驳回。</summary>
    public const int Rejected = 2;

    /// <summary>草稿：作者主动取消审核后自留，仅本人可见。</summary>
    public const int Draft = 3;
}
