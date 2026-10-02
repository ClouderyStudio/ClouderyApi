using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

using ClouderyApi.Modules.Mhop.Domain.Events;
using ClouderyApi.Shared.Domain;
using ClouderyApi.Shared.Exceptions;

namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>MHOP 用户角色：user（普通用户）/ admin（普通管理员）/ superadmin（超级管理员）。</summary>
public static class MhopUserRole
{
    public const string User = "user";
    public const string Admin = "admin";
    public const string SuperAdmin = "superadmin";

    /// <summary>任意后台人员：普通管理员或超级管理员。</summary>
    public static bool IsStaff(string? role) => role is Admin or SuperAdmin;

    public static bool IsSuper(string? role) => role == SuperAdmin;
}

/// <summary>MHOP 用户账号状态：active（可用）/ disabled（停用）。</summary>
public static class MhopUserStatus
{
    public const string Active = "active";
    public const string Disabled = "disabled";

    public static bool IsValid(string? status) => status is Active or Disabled;
}

/// <summary>
/// MHOP 平台用户（论坛 / 心理评估 / 管理后台共用）。对应 Python 后端的 users 表。
/// 表名加 mhop_ 前缀，避免与身份域及其它已有域的 Users 表冲突。
/// 角色与账号状态的转换规则收在本聚合内，非法转换抛 <see cref="DomainRuleException"/>。
/// </summary>
[Table("mhop_users")]
public class MhopUser : IHasDomainEvents
{
    /// <summary>Badge 列宽（与 [MaxLength] 保持一致）。</summary>
    public const int MaxBadgeLength = 64;

    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>邮箱验证码登录；可为空（存量为 NULL）。</summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>手机号；发帖前必须绑定（不做短信验证）。</summary>
    [MaxLength(20)]
    public string? Phone { get; set; }

    /// <summary>Casdoor 统一身份认证用户 ID（OAuth2/OIDC 登录时绑定；本地账号为 NULL）。</summary>
    [MaxLength(100)]
    public string? CasdoorId { get; set; }

    /// <summary>admin / superadmin / user</summary>
    [Required]
    [MaxLength(16)]
    public string Role { get; set; } = MhopUserRole.User;

    /// <summary>
    /// 普通管理员被授予的后台模块权限码 JSON 数组，如 ["dashboard","review"]；
    /// 仅 role=admin 有意义，superadmin 隐式拥有全部权限。空字符串表示未授权。
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string Permissions { get; set; } = string.Empty;

    /// <summary>active / disabled</summary>
    [Required]
    [MaxLength(16)]
    public string Status { get; set; } = MhopUserStatus.Active;

    /// <summary>头像 URL 路径</summary>
    [Required]
    [MaxLength(255)]
    public string Avatar { get; set; } = string.Empty;

    /// <summary>管理员设置的用户标识（如「认证咨询师」「志愿者」）</summary>
    [Required]
    [MaxLength(64)]
    public string Badge { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ---------------- 领域事件承载 ----------------
    // 用接口 + [NotMapped] 集合而非实体基类：避免 EF 把基类纳入类型层级、要求主键或引入判别列。

    private readonly List<IDomainEvent> _domainEvents = new();

    /// <summary>尚未派发的领域事件；[NotMapped] 保证 EF 不把它当列。</summary>
    [NotMapped]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <inheritdoc />
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>供实体行为登记领域事件（第 2 步起使用）。</summary>
    private void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>是否后台人员（普通管理员或超级管理员）。</summary>
    public bool IsStaff => MhopUserRole.IsStaff(Role);

    /// <summary>是否超级管理员（隐式拥有全部模块权限）。</summary>
    public bool IsSuperAdmin => MhopUserRole.IsSuper(Role);

    /// <summary>账号是否可用。</summary>
    public bool IsActive => Status == MhopUserStatus.Active;

    /// <summary>普通用户 → 普通管理员，并整体覆盖其模块授权。</summary>
    public void Promote(PermissionSet permissions)
    {
        if (IsStaff) throw new DomainRuleException("该用户已是管理员");
        Role = MhopUserRole.Admin;
        Permissions = permissions.Serialize();
        AddDomainEvent(new UserRoleChanged(this));
    }

    /// <summary>普通管理员 → 普通用户（清空模块授权）。</summary>
    public void Demote(int operatorUserId)
    {
        if (Role != MhopUserRole.Admin)
            throw new DomainRuleException("该用户不是可降级的管理员（超级管理员请使用「取消超管」）");
        if (Id == operatorUserId) throw new DomainRuleException("不能取消自己的管理员权限");
        Role = MhopUserRole.User;
        Permissions = string.Empty;
        AddDomainEvent(new UserRoleChanged(this));
    }

    /// <summary>提升为超级管理员；保留原 permissions 列，便于日后取消超管时恢复模块授权。</summary>
    public void PromoteSuper()
    {
        if (IsSuperAdmin) throw new DomainRuleException("该用户已是超级管理员");
        Role = MhopUserRole.SuperAdmin;
        AddDomainEvent(new UserRoleChanged(this));
    }

    /// <summary>超级管理员 → 普通管理员，返回其原保留的模块授权。</summary>
    /// <param name="operatorUserId">发起操作的超管 Id（不允许取消自己）。</param>
    /// <param name="isLastActiveSuperAdmin">该用户是否为最后一个可用的超级管理员。</param>
    public PermissionSet DemoteSuper(int operatorUserId, bool isLastActiveSuperAdmin)
    {
        if (!IsSuperAdmin) throw new DomainRuleException("该用户不是超级管理员");
        if (Id == operatorUserId) throw new DomainRuleException("不能取消自己的超级管理员身份");
        if (isLastActiveSuperAdmin) throw new DomainRuleException("系统至少需要保留一个超级管理员");
        var restored = PermissionSet.Parse(Permissions);
        Role = MhopUserRole.Admin;
        AddDomainEvent(new UserRoleChanged(this));
        return restored;
    }

    /// <summary>停用账号：不允许停用自己，也不允许停用最后一个可用的超级管理员。</summary>
    public void Disable(int operatorUserId, bool isLastActiveSuperAdmin)
    {
        if (Id == operatorUserId) throw new DomainRuleException("不能停用当前登录的管理员");
        if (isLastActiveSuperAdmin) throw new DomainRuleException("系统至少需要保留一个可用的超级管理员");
        Status = MhopUserStatus.Disabled;
        AddDomainEvent(new UserStatusChanged(this));
    }

    /// <summary>启用账号。</summary>
    public void Enable()
    {
        Status = MhopUserStatus.Active;
        AddDomainEvent(new UserStatusChanged(this));
    }

    /// <summary>设置用户标识（trim，超长截断）；返回实际落库值。</summary>
    public string SetBadge(string? badge)
    {
        var value = (badge ?? string.Empty).Trim();
        Badge = value.Length <= MaxBadgeLength ? value : value[..MaxBadgeLength];
        AddDomainEvent(new UserBadgeChanged(this));
        return Badge;
    }

    /// <summary>分配模块权限（仅普通管理员；superadmin 隐式拥有全部权限）。</summary>
    public void SetPermissions(PermissionSet permissions)
    {
        if (Role != MhopUserRole.Admin)
            throw new DomainRuleException("仅普通管理员可分配模块权限（超级管理员隐式拥有全部权限）");
        Permissions = permissions.Serialize();
        AddDomainEvent(new UserPermissionsChanged(this));
    }
}
