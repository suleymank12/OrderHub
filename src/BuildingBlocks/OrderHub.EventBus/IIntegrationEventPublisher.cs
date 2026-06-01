namespace OrderHub.EventBus;

/// <summary>
/// Entegrasyon olaylarını mesaj bus'ına yayımlayan soyutlama. Application/Outbox katmanları bu port'a
/// bağlanır; somut taşıma (MassTransit + RabbitMQ) Infrastructure detayıdır (DIP). Outbox processor
/// (OrderHub.Outbox) işlenmemiş mesajları deserialize edip bu port üzerinden yayımlar.
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Verilen entegrasyon olayını <b>runtime (somut) tipiyle</b> yayımlar. Somut tip routing/topology
    /// için zorunludur (exchange adı mesaj tipinden türer); interface üzerinden yayım tüm olayları tek
    /// hedefe düşürürdü.
    /// </summary>
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}
