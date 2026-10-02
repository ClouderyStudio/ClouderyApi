namespace ClouderyApi.Shared.Domain;

/// <summary>
/// 能承载待派发领域事件的实体。刻意用接口而非实体基类：基类会被 EF 纳入类型层级，
/// 要求主键或引入判别列，从而改变数据模型；接口 + [NotMapped] 集合对 EF 完全透明。
/// </summary>
public interface IHasDomainEvents
{
    /// <summary>当前实体内尚未派发的领域事件（不映射到数据库）。</summary>
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    /// <summary>派发完成后清空事件列表。</summary>
    void ClearDomainEvents();
}
