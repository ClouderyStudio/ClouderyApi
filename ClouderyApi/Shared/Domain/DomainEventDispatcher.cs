using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Shared.Domain;

/// <summary>
/// 进程内领域事件派发器（选型 A：SaveChanges 提交后派发，零迁移）。
/// 按事件运行时类型解析 IDomainEventHandler&lt;TEvent&gt;，逐个 await，保证提交后顺序可见。
/// </summary>
public sealed class DomainEventDispatcher(IServiceProvider serviceProvider) : IDomainEventDispatcher
{
    /// <inheritdoc />
    public async Task DispatchAsync(IEnumerable<IHasDomainEvents> sources, CancellationToken cancellationToken = default)
    {
        var sourceList = sources as IReadOnlyList<IHasDomainEvents> ?? sources.ToList();

        // 无事件时直接返回：不解析任何服务、不产生任何 IO（基座空转时零开销）
        var domainEvents = sourceList.SelectMany(source => source.DomainEvents).ToList();
        if (domainEvents.Count == 0) return;

        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in serviceProvider.GetServices(handlerType))
            {
                if (handler is null) continue;
                await ((dynamic)handler).HandleAsync((dynamic)domainEvent, cancellationToken);
            }
        }

        foreach (var source in sourceList)
        {
            source.ClearDomainEvents();
        }
    }
}

/// <summary>
/// 空派发器：不做事。仅供设计时工厂（dotnet ef migrations）构造 MhopDbContext 使用。
/// </summary>
public sealed class NullDomainEventDispatcher : IDomainEventDispatcher
{
    /// <summary>全局唯一实例（无状态）。</summary>
    public static NullDomainEventDispatcher Instance { get; } = new();

    private NullDomainEventDispatcher()
    {
    }

    /// <inheritdoc />
    public Task DispatchAsync(IEnumerable<IHasDomainEvents> sources, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
