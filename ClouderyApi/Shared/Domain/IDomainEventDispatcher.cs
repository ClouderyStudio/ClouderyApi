namespace ClouderyApi.Shared.Domain;

/// <summary>
/// 领域事件派发器：在 SaveChanges 提交成功后，把聚合上登记的领域事件交给订阅者。
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>派发这些聚合承载的领域事件；无事件时必须零开销、零 IO。</summary>
    Task DispatchAsync(IEnumerable<IHasDomainEvents> sources, CancellationToken cancellationToken = default);
}
