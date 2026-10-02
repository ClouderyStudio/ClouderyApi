using ClouderyApi.Shared.Domain;

namespace ClouderyApi.Modules.Mhop.Domain.Events;

/// <summary>
/// 用户角色发生迁移（普通用户 / 普通管理员 / 超级管理员之间）。
/// 载荷带实体引用：后台编排在同一请求内完成写入，订阅者读 <c>User.Id</c> 与最新角色即可。
/// </summary>
public sealed record UserRoleChanged(MhopUser User) : IDomainEvent;

/// <summary>普通管理员的模块权限被整体覆盖。</summary>
public sealed record UserPermissionsChanged(MhopUser User) : IDomainEvent;

/// <summary>账号启用 / 停用。</summary>
public sealed record UserStatusChanged(MhopUser User) : IDomainEvent;

/// <summary>用户标识（badge）被设置。</summary>
public sealed record UserBadgeChanged(MhopUser User) : IDomainEvent;
