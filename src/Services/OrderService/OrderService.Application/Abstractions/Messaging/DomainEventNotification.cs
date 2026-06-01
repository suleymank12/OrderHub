using MediatR;
using OrderHub.Common.Primitives;

namespace OrderHub.OrderService.Application.Abstractions.Messaging;

/// <summary>
/// Domain event'leri (altyapıdan bağımsız <see cref="IDomainEvent"/>) MediatR'ın in-process
/// yayınına köprüler. Domain katmanı MediatR'a bağlanmaz (Clean Architecture / DIP); adaptasyon bu
/// Application-seviyesi wrapper iledir. Infrastructure'daki dispatcher her domain event'i bununla sarıp
/// <c>IPublisher.Publish</c> eder; handler'lar
/// <c>INotificationHandler&lt;DomainEventNotification&lt;OrderCreated&gt;&gt;</c> biçiminde yazılır.
/// </summary>
/// <typeparam name="TDomainEvent">Sarılan domain event tipi.</typeparam>
/// <param name="DomainEvent">Sarılan domain event örneği.</param>
public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification
    where TDomainEvent : IDomainEvent;
