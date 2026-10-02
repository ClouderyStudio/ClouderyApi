using ClouderyApi.Shared.Exceptions;
using ClouderyApi.Models.Mhop;

namespace ClouderyApi.Tests;

/// <summary>
/// 点赞目标类型值对象（LikeTargetType）的纯单测：不依赖 HTTP、不依赖数据库。
/// 钉住落库取值（post / reply 由 mhop_likes.target_type 与唯一索引消费）与非法值的 400 文案。
/// </summary>
public sealed class DomainLikeTargetTests
{
    private static string Reason(Action action) => Assert.Throws<DomainRuleException>(action).Message;

    [Theory]
    [InlineData("post")]
    [InlineData("reply")]
    public void Parse_accepts_known_values(string raw)
    {
        var target = LikeTargetType.Parse(raw);
        Assert.Equal(raw, target.Value);
        Assert.Equal(raw == "post", target.IsPost);
    }

    [Fact]
    public void Parse_rejects_unknown_values_with_legacy_message()
    {
        Assert.Equal("非法点赞对象", Reason(() => LikeTargetType.Parse("comment")));
        Assert.Equal("非法点赞对象", Reason(() => LikeTargetType.Parse("Post")));
        Assert.Equal("非法点赞对象", Reason(() => LikeTargetType.Parse("")));
        Assert.Equal("非法点赞对象", Reason(() => LikeTargetType.Parse(null)));
    }

    [Fact]
    public void TryParse_reports_failure_without_throwing()
    {
        Assert.True(LikeTargetType.TryParse("post", out var post));
        Assert.Equal(LikeTargetType.Post, post);
        Assert.True(LikeTargetType.TryParse("reply", out var reply));
        Assert.Equal(LikeTargetType.Reply, reply);

        Assert.False(LikeTargetType.TryParse("bottle", out var unknown));
        Assert.Equal(default, unknown);
        Assert.False(LikeTargetType.TryParse(null, out _));
    }

    [Theory]
    [InlineData("post", true)]
    [InlineData("reply", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("comment", false)]
    public void IsValid_only_accepts_post_and_reply(string? raw, bool expected)
        => Assert.Equal(expected, LikeTargetType.IsValid(raw));

    [Fact]
    public void Values_match_the_stored_column_contract()
    {
        Assert.Equal("post", LikeTargetType.PostValue);
        Assert.Equal("reply", LikeTargetType.ReplyValue);
        Assert.Equal("post", LikeTargetType.Post.Value);
        Assert.Equal("reply", LikeTargetType.Reply.Value);
        Assert.True(LikeTargetType.Post.IsPost);
        Assert.False(LikeTargetType.Reply.IsPost);
    }

    [Fact]
    public void Values_share_the_content_kind_vocabulary()
    {
        Assert.Equal(MhopContentKind.Post, LikeTargetType.PostValue);
        Assert.Equal(MhopContentKind.Reply, LikeTargetType.ReplyValue);
    }

    [Fact]
    public void ToString_returns_the_stored_value()
    {
        Assert.Equal("post", LikeTargetType.Post.ToString());
        Assert.Equal("reply", LikeTargetType.Reply.ToString());
    }

    [Fact]
    public void Equality_is_value_based()
    {
        Assert.Equal(LikeTargetType.Post, LikeTargetType.Parse("post"));
        Assert.NotEqual(LikeTargetType.Post, LikeTargetType.Reply);
        Assert.Equal(LikeTargetType.Post.GetHashCode(), LikeTargetType.Parse("post").GetHashCode());
    }
}
