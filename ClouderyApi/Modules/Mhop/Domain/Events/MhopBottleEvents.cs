using ClouderyApi.Shared.Domain;

namespace ClouderyApi.Modules.Mhop.Domain.Events;

/// <summary>
/// 瓶子被扔出：初始待审核，登记后由订阅者在提交后排队送 AI 初筛。
/// 载荷带实体引用而非 Id：新建实体的自增 Id 在 SaveChanges 之前不可用，
/// 订阅者在提交后读 <c>Bottle.Id</c> 才正确。
/// </summary>
public sealed record BottleThrown(MhopBottle Bottle) : IDomainEvent;

/// <summary>瓶子消息发出：登记后由订阅者在提交后排队送 AI 初筛。</summary>
public sealed record BottleMessageSent(MhopBottleMessage Message) : IDomainEvent;
