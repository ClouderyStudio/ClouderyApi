namespace ClouderyApi.Shared.Domain;

/// <summary>
/// 领域事件订阅者。同一事件类型可有多个实现，提交后按注册顺序依次同步等待。
/// 实现约定：只做提交后的连锁动作，不得依赖当前未提交的事务。
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>处理一个已提交的领域事件。</summary>
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
