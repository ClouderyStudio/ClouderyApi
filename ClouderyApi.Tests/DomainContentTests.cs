using ClouderyApi.Shared.Exceptions;
using ClouderyApi.Modules.Mhop.Domain;

namespace ClouderyApi.Tests;

/// <summary>
/// 内容聚合（帖子 / 回复）领域规则的纯单测：不依赖 HTTP、不依赖数据库。
/// 覆盖值对象校验、状态机合法/非法转换与审核结论清理。
/// </summary>
public sealed class DomainContentTests
{
    private static ContentScreening Safe => new(false, Array.Empty<string>());

    private static ContentScreening WithCrisis => new(true, Array.Empty<string>());

    private static ContentScreening Sensitive(params string[] words) => new(false, words);

    private static MhopPost NewPost(string content = "今天有点累", string board = "mood")
        => MhopPost.NewAuthorPost(7, false, ContentText.ForPost(content), BoardSlug.Create(board), [], false);

    private static MhopReply NewReply(string content = "抱抱你")
        => MhopReply.NewAuthorReply(1, 7, false, ContentText.ForReply(content), [], false);

    // ---------------- 值对象 ----------------

    [Fact]
    public void ContentText_trims_and_rejects_empty_post()
    {
        Assert.Equal("正文", ContentText.ForPost("  正文  ").Value);

        var tooLong = new string('x', ContentText.MaxPostLength + 1);
        Assert.Equal("内容不能为空", Assert.Throws<DomainRuleException>(() => ContentText.ForPost("   ")).Message);
        Assert.Equal("内容不能超过 2000 字", Assert.Throws<DomainRuleException>(() => ContentText.ForPost(tooLong)).Message);
    }

    [Fact]
    public void ContentText_uses_reply_specific_messages()
    {
        var tooLong = new string('x', ContentText.MaxReplyLength + 1);
        Assert.Equal("回复内容不能为空", Assert.Throws<DomainRuleException>(() => ContentText.ForReply(null)).Message);
        Assert.Equal("回复内容不能超过 1000 字", Assert.Throws<DomainRuleException>(() => ContentText.ForReply(tooLong)).Message);
    }

    [Fact]
    public void BoardSlug_requires_exact_known_slug()
    {
        Assert.Equal("mood", BoardSlug.Create("mood").Value);
        Assert.Equal("请选择板块", Assert.Throws<DomainRuleException>(() => BoardSlug.Create(" ")).Message);
        Assert.Equal("请选择板块", Assert.Throws<DomainRuleException>(() => BoardSlug.Create("nope")).Message);
        // 历史行为：不做 trim，带空格的 slug 一律拒绝
        Assert.Throws<DomainRuleException>(() => BoardSlug.Create(" mood "));
    }

    [Fact]
    public void ImageRefs_normalize_drops_blank_and_caps_at_nine()
    {
        var normalized = MhopImageRefs.Normalize(["a", " ", "", "b"]);
        Assert.Equal(["a", "b"], normalized);
        Assert.Equal(9, MhopImageRefs.Normalize(Enumerable.Range(0, 20).Select(i => $"u{i}")).Count);
        Assert.Equal(string.Empty, MhopImageRefs.Serialize([]));
        Assert.Equal(["u0"], MhopImageRefs.Parse(MhopImageRefs.Serialize(["u0"])));
    }

    // ---------------- 帖子状态机 ----------------

    [Fact]
    public void New_post_starts_pending_and_only_withdrawable()
    {
        var post = NewPost();

        Assert.Equal(ContentStatus.Pending, post.Status);
        Assert.True(post.IsEditable);
        Assert.True(post.CanWithdraw);
        Assert.False(post.CanSubmit);
        Assert.Equal(string.Empty, post.Images);
    }

    [Fact]
    public void Withdraw_moves_pending_to_draft_and_rejects_other_states()
    {
        var post = NewPost();
        post.Withdraw();
        Assert.Equal(ContentStatus.Draft, post.Status);
        Assert.False(post.IsEditable && post.CanWithdraw);
        Assert.True(post.CanSubmit);

        var ex = Assert.Throws<DomainRuleException>(() => post.Withdraw());
        Assert.Equal("只有审核中的内容可以取消审核", ex.Message);
        Assert.Equal(ContentStatus.Draft, post.Status);
    }

    [Fact]
    public void SubmitForReview_clears_previous_review_state()
    {
        var post = NewPost();
        post.Withdraw();
        post.ReviewNote = "旧理由";
        post.AiFlag = "suspect";
        post.AiReviewNote = "旧 AI 理由";
        post.AiReviewedAt = DateTime.UtcNow;

        post.SubmitForReview();

        Assert.Equal(ContentStatus.Pending, post.Status);
        Assert.Equal(string.Empty, post.ReviewNote);
        Assert.Equal(string.Empty, post.AiFlag);
        Assert.Equal(string.Empty, post.AiReviewNote);
        Assert.Null(post.AiReviewedAt);
    }

    [Fact]
    public void SubmitForReview_rejects_published_post()
    {
        var post = NewPost();
        post.Publish();
        Assert.Equal("只有草稿可以重新提交审核", Assert.Throws<DomainRuleException>(() => post.SubmitForReview()).Message);
    }

    [Fact]
    public void Publish_and_Reject_truncate_review_note_to_column_width()
    {
        var note = new string('长', ContentRules.ReviewNoteMaxLength + 10);

        var published = NewPost();
        published.Publish(note);
        Assert.Equal(ContentStatus.Published, published.Status);
        Assert.Equal(ContentRules.ReviewNoteMaxLength, published.ReviewNote.Length);

        var rejected = NewPost();
        rejected.Reject(note);
        Assert.Equal(ContentStatus.Rejected, rejected.Status);
        Assert.Equal(ContentRules.ReviewNoteMaxLength, rejected.ReviewNote.Length);
    }

    [Fact]
    public void RejectBySensitiveWords_writes_system_note()
    {
        var reply = NewReply("加微信领资料");
        reply.RejectBySensitiveWords(["加微信领"]);

        Assert.Equal(ContentStatus.Rejected, reply.Status);
        Assert.Equal("系统拦截：命中敏感词 加微信领", reply.ReviewNote);
    }

    [Fact]
    public void Post_edit_preserves_guard_order_status_then_content_then_board()
    {
        var published = NewPost();
        published.Publish();
        // 已通过审核：即使正文为空也先报状态错误（历史错误顺序）
        Assert.Equal("已通过审核的内容不可修改，仅可删除",
            Assert.Throws<DomainRuleException>(() => published.ApplyAuthorEdit("", "mood", false, [], Safe)).Message);

        var pending = NewPost();
        Assert.Equal("内容不能为空",
            Assert.Throws<DomainRuleException>(() => pending.ApplyAuthorEdit("  ", "mood", false, [], Safe)).Message);
        Assert.Equal("请选择板块",
            Assert.Throws<DomainRuleException>(() => pending.ApplyAuthorEdit("正文", "nope", false, [], Safe)).Message);
    }

    [Fact]
    public void Post_edit_replaces_fields_and_returns_removed_images()
    {
        var post = MhopPost.NewAuthorPost(7, false, ContentText.ForPost("旧正文"), BoardSlug.Create("mood"),
            ["keep", "drop"], false);

        var removed = post.ApplyAuthorEdit("新正文", "chat", true, ["keep"], WithCrisis);

        Assert.Equal(["drop"], removed);
        Assert.Equal("新正文", post.Content);
        Assert.Equal("chat", post.Board);
        Assert.True(post.IsAnonymous);
        Assert.True(post.Crisis);
        Assert.Equal(["keep"], MhopImageRefs.Parse(post.Images));
    }

    [Fact]
    public void Post_edit_clears_ai_flag_on_pending_post()
    {
        var post = NewPost();
        post.AiFlag = "suspect";
        post.AiReviewNote = "理由";
        post.AiReviewedAt = DateTime.UtcNow;

        post.ApplyAuthorEdit("改过的正文", "mood", false, [], Safe);

        Assert.Equal(string.Empty, post.AiFlag);
        Assert.Equal(string.Empty, post.AiReviewNote);
        Assert.Null(post.AiReviewedAt);
    }

    // ---------------- 回复状态机 ----------------

    [Fact]
    public void Reply_edit_keeps_manual_note_on_draft_but_clears_ai_flag()
    {
        // 草稿：保留人工审核理由（历史行为）
        var draft = NewReply();
        draft.Withdraw();
        draft.ReviewNote = "人工旧理由";
        draft.AiFlag = "suspect";

        draft.ApplyAuthorEdit("改过的回复", false, [], Safe);

        Assert.Equal(ContentStatus.Draft, draft.Status);
        Assert.Equal("人工旧理由", draft.ReviewNote);
        Assert.Equal(string.Empty, draft.AiFlag);
    }

    [Fact]
    public void Reply_edit_resets_manual_note_on_pending_reply()
    {
        // 待审核：重新走敏感词复核，未命中则重置人工理由
        var pending = NewReply();
        pending.ReviewNote = "人工旧理由";

        pending.ApplyAuthorEdit("改过的回复", false, [], Safe);

        Assert.Equal(ContentStatus.Pending, pending.Status);
        Assert.Equal(string.Empty, pending.ReviewNote);
    }

    [Fact]
    public void Reply_edit_rejects_pending_reply_when_words_hit()
    {
        var reply = NewReply();

        reply.ApplyAuthorEdit("加微信领资料", false, [], Sensitive("加微信领"));

        Assert.Equal(ContentStatus.Rejected, reply.Status);
        Assert.Equal("系统拦截：命中敏感词 加微信领", reply.ReviewNote);
    }

    [Fact]
    public void Reply_withdraw_submit_and_recall_roundtrip()
    {
        var reply = NewReply();
        reply.Withdraw();
        Assert.Equal(ContentStatus.Draft, reply.Status);
        reply.SubmitForReview();
        Assert.Equal(ContentStatus.Pending, reply.Status);

        reply.Recall("  理由过长也要能存  ");
        Assert.True(reply.Recalled);
        Assert.Equal("理由过长也要能存", reply.RecallReason);

        reply.Restore();
        Assert.False(reply.Recalled);
        Assert.Equal(string.Empty, reply.RecallReason);
    }

    [Fact]
    public void Reply_submit_uses_reply_specific_empty_message()
    {
        var reply = NewReply();
        reply.Withdraw();
        reply.Content = "   ";

        Assert.Equal("回复内容不能为空", Assert.Throws<DomainRuleException>(() => reply.SubmitForReview()).Message);
    }

    [Fact]
    public void Post_add_view_increments_counter()
    {
        var post = NewPost();
        post.AddView();
        post.AddView();
        Assert.Equal(2, post.ViewCount);
    }
}
