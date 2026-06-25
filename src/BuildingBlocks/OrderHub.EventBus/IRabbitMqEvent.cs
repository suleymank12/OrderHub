namespace OrderHub.EventBus;

/// <summary>
/// <b>Command-style</b> integration event → RabbitMQ (MassTransit, point-to-point). ADR-0006 Karar 1/3:
/// "bu iş yapılmalı" semantiği, tek mantıksal tüketici. Routing publisher bu marker'ı (veya işaretsiz event'i)
/// RabbitMQ'ya route eder; <see cref="IKafkaEvent"/> ise Kafka'ya.
/// </summary>
public interface IRabbitMqEvent : IIntegrationEvent;
