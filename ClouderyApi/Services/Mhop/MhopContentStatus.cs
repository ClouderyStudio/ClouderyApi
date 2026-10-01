namespace ClouderyApi.Services.Mhop;

/// <summary>论坛帖子 / 回复的状态码（与 MhopForumController 内部常量保持一致）。</summary>
public static class MhopContentStatus
{
    /// <summary>待审核：AI 未通过或仍在审核中，仅作者本人可见。</summary>
    public const int Pending = 0;

    /// <summary>已公开。</summary>
    public const int Published = 1;

    /// <summary>已驳回。</summary>
    public const int Rejected = 2;

    /// <summary>草稿：作者主动取消审核，仅本人可见。</summary>
    public const int Draft = 3;
}
