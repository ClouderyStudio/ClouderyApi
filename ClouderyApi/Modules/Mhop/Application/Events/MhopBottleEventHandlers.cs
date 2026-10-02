using ClouderyApi.Modules.Mhop.Domain.Events;
using ClouderyApi.Shared.Domain;

namespace ClouderyApi.Modules.Mhop.Application.Events;

/// <summary>瓶子被扔出 → 排队送 AI 初筛（火后不理，不阻塞响应）。</summary>
public sealed class BottleThrownHandler(MhopContentReviewService review)
    : IDomainEventHandler<BottleThrown>
{
    public Task HandleAsync(BottleThrown domainEvent, CancellationToken cancellationToken = default)
    {
        review.QueueBottleReview(domainEvent.Bottle.Id);
        return Task.CompletedTask;
    }
}

/// <summary>瓶子消息发出 → 排队送 AI 初筛（火后不理，不阻塞响应）。</summary>
public sealed class BottleMessageSentHandler(MhopContentReviewService review)
    : IDomainEventHandler<BottleMessageSent>
{
    public Task HandleAsync(BottleMessageSent domainEvent, CancellationToken cancellationToken = default)
    {
        review.QueueMessageReview(domainEvent.Message.Id);
        return Task.CompletedTask;
    }
}
