using ClouderyApi.Shared.Exceptions;
using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Infrastructure;

using ClouderyApi.Modules.Mhop.Application;
namespace ClouderyApi.Tests;

/// <summary>
/// MHOP 用户聚合（角色 / 状态 / 模块权限）领域规则的纯单测：不依赖 HTTP、不依赖数据库。
/// 覆盖管理员升降级、超管保护（最后一个可用超管不可停用 / 降级）、自我操作保护与权限码规范化。
/// </summary>
public sealed class DomainUserTests
{
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

    private static string Reason(Action action) => Assert.Throws<DomainRuleException>(action).Message;

    // ---------------- 角色 / 状态判定 ----------------

    [Theory]
    [InlineData(MhopUserRole.User, false, false)]
    [InlineData(MhopUserRole.Admin, true, false)]
    [InlineData(MhopUserRole.SuperAdmin, true, true)]
    public void role_helpers_classify_staff_and_super(string role, bool staff, bool super)
    {
        var user = NewUser(role);

        Assert.Equal(staff, user.IsStaff);
        Assert.Equal(super, user.IsSuperAdmin);
    }

    [Fact]
    public void IsActive_tracks_status()
    {
        Assert.True(NewUser().IsActive);
        Assert.False(NewUser(status: MhopUserStatus.Disabled).IsActive);
    }

    // ---------------- 提升 / 降级 ----------------

    [Fact]
    public void Promote_sets_admin_role_and_normalizes_permissions()
    {
        var user = NewUser();

        user.Promote(PermissionSet.From(["dashboard", "review", "dashboard", "unknown"]));

        Assert.Equal(MhopUserRole.Admin, user.Role);
        Assert.Equal(new[] { "dashboard", "review" }, PermissionSet.Parse(user.Permissions).Codes);
    }

    [Fact]
    public void Promote_rejects_existing_staff()
    {
        Assert.Equal("该用户已是管理员", Reason(() => NewUser(MhopUserRole.Admin).Promote(PermissionSet.Empty)));
        Assert.Equal("该用户已是管理员", Reason(() => NewUser(MhopUserRole.SuperAdmin).Promote(PermissionSet.Empty)));
    }

    [Fact]
    public void Demote_clears_role_and_permissions()
    {
        var user = NewUser(MhopUserRole.Admin, permissions: """["users"]""", id: 3);

        user.Demote(operatorUserId: 9);

        Assert.Equal(MhopUserRole.User, user.Role);
        Assert.Equal(string.Empty, user.Permissions);
    }

    [Fact]
    public void Demote_rejects_non_admin_and_self()
    {
        const string notDemotable = "该用户不是可降级的管理员（超级管理员请使用「取消超管」）";

        Assert.Equal(notDemotable, Reason(() => NewUser().Demote(9)));
        Assert.Equal(notDemotable, Reason(() => NewUser(MhopUserRole.SuperAdmin).Demote(9)));
        Assert.Equal("不能取消自己的管理员权限", Reason(() => NewUser(MhopUserRole.Admin, id: 5).Demote(5)));
    }

    [Fact]
    public void PromoteSuper_keeps_stored_permissions()
    {
        var user = NewUser(MhopUserRole.Admin, permissions: """["bottles"]""");

        user.PromoteSuper();

        Assert.Equal(MhopUserRole.SuperAdmin, user.Role);
        Assert.Equal("""["bottles"]""", user.Permissions);
    }

    [Fact]
    public void PromoteSuper_rejects_existing_super()
    {
        Assert.Equal("该用户已是超级管理员", Reason(() => NewUser(MhopUserRole.SuperAdmin).PromoteSuper()));
    }

    [Fact]
    public void DemoteSuper_returns_restored_permissions()
    {
        var user = NewUser(MhopUserRole.SuperAdmin, permissions: """["users","ai_logs"]""", id: 2);

        var restored = user.DemoteSuper(operatorUserId: 1, isLastActiveSuperAdmin: false);

        Assert.Equal(MhopUserRole.Admin, user.Role);
        Assert.Equal(new[] { "users", "ai_logs" }, restored.Codes);
    }

    [Fact]
    public void DemoteSuper_guards_self_and_last_super()
    {
        Assert.Equal("该用户不是超级管理员", Reason(() => NewUser(MhopUserRole.Admin).DemoteSuper(1, false)));
        Assert.Equal("不能取消自己的超级管理员身份",
            Reason(() => NewUser(MhopUserRole.SuperAdmin, id: 1).DemoteSuper(1, false)));
        Assert.Equal("系统至少需要保留一个超级管理员",
            Reason(() => NewUser(MhopUserRole.SuperAdmin, id: 2).DemoteSuper(1, true)));
    }

    // ---------------- 停用 / 启用 ----------------

    [Fact]
    public void Disable_and_Enable_toggle_status()
    {
        var user = NewUser(MhopUserRole.User, id: 2);

        user.Disable(operatorUserId: 1, isLastActiveSuperAdmin: false);
        Assert.Equal(MhopUserStatus.Disabled, user.Status);

        user.Enable();
        Assert.Equal(MhopUserStatus.Active, user.Status);
    }

    [Fact]
    public void Disable_guards_self_and_last_super()
    {
        Assert.Equal("不能停用当前登录的管理员",
            Reason(() => NewUser(MhopUserRole.Admin, id: 4).Disable(4, false)));
        Assert.Equal("系统至少需要保留一个可用的超级管理员",
            Reason(() => NewUser(MhopUserRole.SuperAdmin, id: 4).Disable(1, true)));
    }

    // ---------------- 模块权限 ----------------

    [Fact]
    public void SetPermissions_requires_plain_admin_and_normalizes()
    {
        const string onlyPlainAdmin = "仅普通管理员可分配模块权限（超级管理员隐式拥有全部权限）";

        Assert.Equal(onlyPlainAdmin, Reason(() => NewUser(MhopUserRole.SuperAdmin).SetPermissions(PermissionSet.Empty)));
        Assert.Equal(onlyPlainAdmin, Reason(() => NewUser().SetPermissions(PermissionSet.Empty)));

        var admin = NewUser(MhopUserRole.Admin);
        admin.SetPermissions(PermissionSet.From([" bottles ", "bottles"]));

        Assert.Equal(new[] { "bottles" }, PermissionSet.Parse(admin.Permissions).Codes);
    }

    // ---------------- 用户标识 ----------------

    [Fact]
    public void SetBadge_trims_truncates_and_tolerates_null()
    {
        var user = NewUser();

        Assert.Equal("认证咨询师", user.SetBadge("  认证咨询师 "));
        Assert.Equal("", user.SetBadge(null));

        var truncated = user.SetBadge(new string('x', 200));
        Assert.Equal(MhopUser.MaxBadgeLength, truncated.Length);
        Assert.Equal(truncated, user.Badge);
    }

    // ---------------- PermissionSet 值对象 ----------------

    [Fact]
    public void PermissionSet_parses_filters_and_dedups()
    {
        var set = PermissionSet.Parse("""["dashboard"," nope ","review","dashboard"]""");

        Assert.Equal(new[] { "dashboard", "review" }, set.Codes);
        Assert.True(set.Contains("dashboard"));
        Assert.False(set.Contains("nope"));
        Assert.Equal("""["dashboard","review"]""", set.Serialize());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("""{"a":1}""")]
    public void PermissionSet_treats_bad_input_as_empty(string? json)
    {
        var set = PermissionSet.Parse(json);

        Assert.True(set.IsEmpty);
        Assert.Empty(set.Codes);
        Assert.Equal("[]", set.Serialize());
    }

    [Fact]
    public void PermissionSet_From_null_is_empty()
    {
        Assert.True(PermissionSet.From(null).IsEmpty);
        Assert.Equal("[]", PermissionSet.Empty.Serialize());
    }

    // ---------------- 服务层门面 ----------------

    [Fact]
    public void Effective_and_Has_follow_role()
    {
        var plain = NewUser();
        Assert.False(MhopAdminPermissions.Has(plain, PermissionSet.Review));
        Assert.Empty(MhopAdminPermissions.Effective(plain));

        var admin = NewUser(MhopUserRole.Admin, permissions: """["review"]""");
        Assert.True(MhopAdminPermissions.Has(admin, PermissionSet.Review));
        Assert.False(MhopAdminPermissions.Has(admin, PermissionSet.Users));
        Assert.Equal(new[] { "review" }, MhopAdminPermissions.Effective(admin));

        var super = NewUser(MhopUserRole.SuperAdmin);
        Assert.True(MhopAdminPermissions.Has(super, PermissionSet.Bottles));
        Assert.Equal(PermissionSet.All, MhopAdminPermissions.Effective(super));

        Assert.False(MhopAdminPermissions.Has(null, PermissionSet.Review));
        Assert.Empty(MhopAdminPermissions.Effective(null));
    }
}
