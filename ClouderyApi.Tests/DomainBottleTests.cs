using ClouderyApi.Models;
using ClouderyApi.Models.Mhop;

namespace ClouderyApi.Tests;

/// <summary>
/// 漂流瓶聚合（瓶子 / 消息）领域规则的纯单测：不依赖 HTTP、不依赖数据库。
/// 覆盖状态流转、举报去重、未读口径与人工处置（下架 / 恢复 / 放行）的标记语义。
/// </summary>
public sealed class DomainBottleTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static MhopBottle NewBottle(int userId = 7) => MhopBottle.Throw(userId, "想让海替我记着", false, Now);

    private static MhopBottle Picked(int throwerId = 7, int pickerId = 9)
    {
        var bottle = NewBottle(throwerId);
        bottle.Pick(pickerId, Now);
        return bottle;
    }

    private static MhopBottleMessage Message(int senderUserId, DateTime createdAt)
        => MhopBottleMessage.Create(1, senderUserId, "你好", false, createdAt);

    // ---------------- 投瓶 / 捞瓶 ----------------

    [Fact]
    public void Throw_starts_pending_and_marks_body_read()
    {
        var bottle = NewBottle();

        Assert.Equal(MhopBottleStatus.Pending, bottle.Status);
        Assert.Equal(7, bottle.UserId);
        Assert.Equal(Now, bottle.CreatedAt);
        Assert.Equal(Now, bottle.LastMessageAt);
        Assert.Equal(Now, bottle.ThrowerLastReadAt);
        Assert.Null(bottle.PickerUserId);
        Assert.False(bottle.Crisis);
    }

    [Fact]
    public void Throw_records_crisis_flag()
        => Assert.True(MhopBottle.Throw(7, "撑不下去了", true, Now).Crisis);

    [Fact]
    public void IsParty_and_RoleOf_know_both_sides()
    {
        var bottle = Picked();

        Assert.True(bottle.IsParty(7));
        Assert.True(bottle.IsParty(9));
        Assert.False(bottle.IsParty(11));
        Assert.Equal("thrower", bottle.RoleOf(7));
        Assert.Equal("picker", bottle.RoleOf(9));
    }

    [Fact]
    public void Pick_sets_picked_state_and_picker_read_time()
    {
        var bottle = Picked();

        Assert.Equal(MhopBottleStatus.Picked, bottle.Status);
        Assert.Equal(9, bottle.PickerUserId);
        Assert.Equal(Now, bottle.PickedAt);
        Assert.Equal(Now, bottle.PickerLastReadAt);
    }

    // ---------------- 结束 / 举报 / 超时基准 ----------------

    [Fact]
    public void End_marks_manual_and_records_ender()
    {
        var bottle = Picked();
        bottle.End(9);

        Assert.Equal(MhopBottleStatus.Ended, bottle.Status);
        Assert.Equal(MhopBottleEndReason.Manual, bottle.EndReason);
        Assert.Equal(9, bottle.EndedByUserId);
    }

    [Fact]
    public void TouchLastMessage_moves_timeout_baseline()
    {
        var bottle = Picked();
        var later = Now.AddDays(1);
        bottle.TouchLastMessage(later);
        Assert.Equal(later, bottle.LastMessageAt);
    }

    [Fact]
    public void TryReport_dedupes_same_reporter()
    {
        var bottle = NewBottle();

        Assert.True(bottle.TryReport(9, "广告", Now));
        Assert.Equal(1, bottle.ReportedCount);
        Assert.Equal("广告", bottle.ReportReason);
        Assert.Equal(Now, bottle.LastReportedAt);

        Assert.False(bottle.TryReport(9, "又举报一次", Now.AddMinutes(1)));
        Assert.Equal(1, bottle.ReportedCount);
        Assert.Equal("广告", bottle.ReportReason); // 重复举报不覆盖理由
    }

    [Fact]
    public void TryReport_counts_distinct_reporters()
    {
        var bottle = NewBottle();
        Assert.True(bottle.TryReport(9, "a", Now));
        Assert.True(bottle.TryReport(10, "b", Now));
        Assert.Equal(2, bottle.ReportedCount);
        Assert.Equal([9, 10], System.Text.Json.JsonSerializer.Deserialize<List<int>>(bottle.ReportedBy));
    }

    [Fact]
    public void TryReport_tolerates_corrupt_reported_by_json()
    {
        var bottle = NewBottle();
        bottle.ReportedBy = "not-json";
        Assert.True(bottle.TryReport(9, "a", Now));
        Assert.Equal(1, bottle.ReportedCount);
    }

    // ---------------- 后台处置 ----------------

    [Fact]
    public void MarkRemoved_sets_removed_status()
    {
        var bottle = Picked();
        bottle.MarkRemoved();
        Assert.Equal(MhopBottleStatus.Removed, bottle.Status);
    }

    [Fact]
    public void Restore_returns_unpicked_bottle_to_sea_and_clears_ai_flag()
    {
        var bottle = NewBottle();
        bottle.AiFlag = MhopModerationOutcome.Violation;
        bottle.AiReviewNote = "AI 判定违规";
        bottle.MarkRemoved();

        bottle.Restore();

        Assert.Equal(MhopBottleStatus.Drifting, bottle.Status);
        Assert.Equal(MhopModerationOutcome.None, bottle.AiFlag);
        Assert.Equal(string.Empty, bottle.AiReviewNote);
    }

    [Fact]
    public void Restore_returns_ongoing_conversation_to_picked()
    {
        var bottle = Picked();
        bottle.MarkRemoved();
        bottle.Restore();
        Assert.Equal(MhopBottleStatus.Picked, bottle.Status);
    }

    [Fact]
    public void Restore_keeps_finished_conversation_finished()
    {
        var bottle = Picked();
        bottle.End(7);
        bottle.AiFlag = MhopModerationOutcome.Suspect;
        bottle.Restore();
        Assert.Equal(MhopBottleStatus.Ended, bottle.Status);
        Assert.Equal(MhopModerationOutcome.None, bottle.AiFlag);
    }

    [Fact]
    public void Approve_publishes_pending_bottle_and_stamps_manual_flag()
    {
        var bottle = NewBottle();
        bottle.Approve(Now.AddHours(1));

        Assert.Equal(MhopBottleStatus.Drifting, bottle.Status);
        Assert.Equal(MhopModerationOutcome.Approved, bottle.AiFlag);
        Assert.Equal(Now.AddHours(1), bottle.AiReviewedAt);
    }

    [Fact]
    public void Approve_rejects_non_pending_bottle()
    {
        var bottle = Picked();
        Assert.Equal("该瓶子不在待审核状态", Assert.Throws<DomainRuleException>(() => bottle.Approve(Now)).Message);
    }

    [Fact]
    public void SetReviewNote_trims_and_ignores_blank()
    {
        var bottle = NewBottle();
        bottle.SetReviewNote("  人工放行  ");
        Assert.Equal("人工放行", bottle.ReviewNote);

        bottle.SetReviewNote("   ");
        Assert.Equal("人工放行", bottle.ReviewNote);
    }

    // ---------------- 未读口径 ----------------

    [Fact]
    public void UnreadCountFor_counts_visible_messages_from_the_other_side_only()
    {
        var bottle = Picked();
        bottle.ThrowerLastReadAt = Now; // 捞起时间即 9 的已读时间
        bottle.Messages.Add(Message(9, Now.AddMinutes(1)));  // 对方新消息 → 未读
        bottle.Messages.Add(Message(7, Now.AddMinutes(2)));  // 自己发的 → 不计
        bottle.Messages.Add(Message(9, Now.AddMinutes(-1))); // 早于已读 → 不计
        var hidden = Message(9, Now.AddMinutes(3));
        hidden.Hide();
        bottle.Messages.Add(hidden);                         // 已隐藏 → 不计

        Assert.Equal(1, bottle.UnreadCountFor(7));
        Assert.Equal(1, bottle.UnreadCountFor(9)); // 7 的消息晚于 9 的已读时间，计 1 条
    }

    // ---------------- 消息状态 ----------------

    [Fact]
    public void Message_is_visible_by_default_and_can_be_hidden()
    {
        var message = Message(9, Now);
        Assert.Equal(MhopBottleMessageStatus.Visible, message.Status);
        Assert.Equal(MhopModerationOutcome.None, message.AiFlag);

        message.Hide();
        Assert.Equal(MhopBottleMessageStatus.Hidden, message.Status);
    }

    [Fact]
    public void Show_restores_visibility_and_keeps_first_review_time()
    {
        var message = Message(9, Now);
        var reviewedAt = Now.AddMinutes(5);
        message.AiReviewedAt = reviewedAt;
        message.Hide();

        message.Show(Now.AddHours(1));

        Assert.Equal(MhopBottleMessageStatus.Visible, message.Status);
        Assert.Equal(MhopModerationOutcome.Approved, message.AiFlag);
        Assert.Equal(reviewedAt, message.AiReviewedAt); // 已有的审核时间不被覆盖
    }
}
