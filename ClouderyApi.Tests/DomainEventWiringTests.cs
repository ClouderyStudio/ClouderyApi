using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Domain.Events;
using ClouderyApi.Shared.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// 守护 Stage 4 的领域事件接线。
/// 背景：内容副作用事件化时做的变异测试证明——把订阅者注册全部注释掉，
/// 既有 30 条相关测试仍然全绿（静默退化，表现为待审内容永久停在 Pending）。
/// 因此这里补最小守卫：实体是否登记事件、驳回是否撤销未派发事件、
/// 派发器是否真的调用订阅者、以及内容侧订阅者是否仍注册在 DI 容器里。
/// </summary>
public sealed class DomainEventWiringTests : IntegrationTestBase
{
    private static ContentScreening Safe => new(false, []);

    private static ContentScreening Sensitive => new(false, ["傻"]);

    private static MhopPost NewPost(string content = "今天有点累")
        => MhopPost.NewAuthorPost(7, false, ContentText.ForPost(content), BoardSlug.Create("mood"), [], false);

    private static MhopReply NewReply(string content = "抱抱你")
        => MhopReply.NewAuthorReply(1, 7, false, ContentText.ForReply(content), [], false);

    private static MhopBottle NewBottle(string content = "有人在海边吗")
        => MhopBottle.Throw(7, content, false, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private static MhopBottleMessage NewMessage(string content = "在的")
        => MhopBottleMessage.Create(1, 7, content, false, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    // ---------------- 实体登记与撤销 ----------------

    [Fact]
    public void NewAuthorPost_registers_review_event()
    {
        var post = NewPost();

        Assert.IsType<PostSubmittedForReview>(Assert.Single(post.DomainEvents));
    }

    [Fact]
    public void NewAuthorReply_registers_review_event()
    {
        var reply = NewReply();

        Assert.IsType<ReplySubmittedForReview>(Assert.Single(reply.DomainEvents));
    }

    [Fact]
    public void Throw_registers_bottle_thrown_event()
    {
        var bottle = NewBottle();

        Assert.IsType<BottleThrown>(Assert.Single(bottle.DomainEvents));
    }

    [Fact]
    public void Create_registers_bottle_message_sent_event()
    {
        var message = NewMessage();

        Assert.IsType<BottleMessageSent>(Assert.Single(message.DomainEvents));
    }

    [Fact]
    public void RejectBySensitiveWords_cancels_pending_review_event()
    {
        var reply = NewReply();

        reply.RejectBySensitiveWords(["傻"]);

        Assert.Equal(ContentStatus.Rejected, reply.Status);
        Assert.Empty(reply.DomainEvents);
    }

    [Fact]
    public void Editing_pending_content_registers_review_event_but_editing_draft_does_not()
    {
        var pending = NewPost();
        pending.ClearDomainEvents();
        pending.ApplyAuthorEdit("改过的正文", "mood", false, [], Safe);

        Assert.IsType<PostSubmittedForReview>(Assert.Single(pending.DomainEvents));

        var draft = NewPost();
        draft.Withdraw();
        draft.ClearDomainEvents();
        draft.ApplyAuthorEdit("草稿正文", "mood", false, [], Safe);

        Assert.Empty(draft.DomainEvents);
    }

    [Fact]
    public void Editing_reply_hit_by_sensitive_words_does_not_register_review_event()
    {
        var reply = NewReply();
        reply.ClearDomainEvents();

        reply.ApplyAuthorEdit("改过的回复", false, [], Sensitive);

        Assert.Equal(ContentStatus.Rejected, reply.Status);
        Assert.Empty(reply.DomainEvents);
    }

    [Fact]
    public void SubmitForReview_and_Publish_register_their_events()
    {
        var post = NewPost();
        post.Withdraw();
        post.ClearDomainEvents();

        post.SubmitForReview();
        Assert.IsType<PostSubmittedForReview>(Assert.Single(post.DomainEvents));

        post.ClearDomainEvents();
        post.Publish("通过");
        Assert.IsType<PostPublished>(Assert.Single(post.DomainEvents));
    }

    // ---------------- 用户权限变更 ----------------

    private static MhopUser NewUser(
        string role = MhopUserRole.User,
        string status = MhopUserStatus.Active,
        string permissions = "",
        int id = 1) => new()
        {
            Id = id,
            Username = "member",
            Role = role,
            Status = status,
            Permissions = permissions,
        };

    [Fact]
    public void Promote_and_Demote_register_user_role_changed_event()
    {
        var user = NewUser();
        user.Promote(PermissionSet.From(["review"]));
        Assert.IsType<UserRoleChanged>(Assert.Single(user.DomainEvents));
        Assert.Equal(MhopUserRole.Admin, user.Role);

        user.ClearDomainEvents();
        user.Demote(operatorUserId: 9);
        Assert.IsType<UserRoleChanged>(Assert.Single(user.DomainEvents));
        Assert.Equal(MhopUserRole.User, user.Role);
    }

    [Fact]
    public void PromoteSuper_and_DemoteSuper_register_user_role_changed_event()
    {
        var user = NewUser();
        user.PromoteSuper();
        Assert.IsType<UserRoleChanged>(Assert.Single(user.DomainEvents));
        Assert.Equal(MhopUserRole.SuperAdmin, user.Role);

        user.ClearDomainEvents();
        user.DemoteSuper(operatorUserId: 9, isLastActiveSuperAdmin: false);
        Assert.IsType<UserRoleChanged>(Assert.Single(user.DomainEvents));
        Assert.Equal(MhopUserRole.Admin, user.Role);
    }

    [Fact]
    public void Disable_and_Enable_register_user_status_changed_event()
    {
        var user = NewUser(MhopUserRole.Admin, id: 2);
        user.Disable(operatorUserId: 1, isLastActiveSuperAdmin: false);
        Assert.IsType<UserStatusChanged>(Assert.Single(user.DomainEvents));
        Assert.Equal(MhopUserStatus.Disabled, user.Status);

        user.ClearDomainEvents();
        user.Enable();
        Assert.IsType<UserStatusChanged>(Assert.Single(user.DomainEvents));
        Assert.Equal(MhopUserStatus.Active, user.Status);
    }

    [Fact]
    public void SetPermissions_registers_user_permissions_changed_event()
    {
        var user = NewUser(MhopUserRole.Admin);
        user.SetPermissions(PermissionSet.From(["review"]));

        Assert.IsType<UserPermissionsChanged>(Assert.Single(user.DomainEvents));
    }

    [Fact]
    public void SetBadge_registers_user_badge_changed_event()
    {
        var user = NewUser();
        var badge = user.SetBadge("  认证咨询师 ");

        Assert.Equal("认证咨询师", badge);
        Assert.IsType<UserBadgeChanged>(Assert.Single(user.DomainEvents));
    }

    // ---------------- 派发器 ----------------

    [Fact]
    public async Task Dispatcher_invokes_matching_handler_and_clears_events()
    {
        var post = NewPost();
        var handler = new RecordingPostReviewHandler();
        using var provider = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<PostSubmittedForReview>>(handler)
            .BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(provider);

        await dispatcher.DispatchAsync([post]);

        Assert.Same(post, handler.Handled);
        Assert.Empty(post.DomainEvents);
    }

    [Fact]
    public async Task Dispatcher_skips_events_without_subscribers_and_still_clears_them()
    {
        var post = NewPost();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new DomainEventDispatcher(provider);

        await dispatcher.DispatchAsync([post]);

        Assert.Empty(post.DomainEvents);
    }

    // ---------------- DI 注册 ----------------

    [Fact]
    public void Content_handlers_are_registered_in_di()
    {
        var services = Factory.Services;

        Assert.NotEmpty(services.GetServices<IDomainEventHandler<PostSubmittedForReview>>());
        Assert.NotEmpty(services.GetServices<IDomainEventHandler<ReplySubmittedForReview>>());
        Assert.NotEmpty(services.GetServices<IDomainEventHandler<PostPublished>>());
        Assert.NotEmpty(services.GetServices<IDomainEventHandler<BottleThrown>>());
        Assert.NotEmpty(services.GetServices<IDomainEventHandler<BottleMessageSent>>());
    }

}

/// <summary>
/// 记录型订阅者桩。必须是 public 顶层类型：派发器用 <c>dynamic</c> 调用 handler，
/// 运行时绑定器按可访问性解析，private 嵌套类型会抛 RuntimeBinderException。
/// </summary>
public sealed class RecordingPostReviewHandler : IDomainEventHandler<PostSubmittedForReview>
{
    public MhopPost? Handled { get; private set; }

    public Task HandleAsync(PostSubmittedForReview domainEvent, CancellationToken cancellationToken = default)
    {
        Handled = domainEvent.Post;
        return Task.CompletedTask;
    }
}
