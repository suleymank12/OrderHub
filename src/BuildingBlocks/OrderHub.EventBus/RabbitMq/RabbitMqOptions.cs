namespace OrderHub.EventBus.RabbitMq;

/// <summary>
/// RabbitMQ bağlantı ayarları. Değerler servis composition root'unda configuration'dan (env/compose)
/// bağlanır; buradaki varsayılanlar yalnızca <b>local dev</b> içindir. <c>guest/guest</c> RabbitMQ'nun
/// bilinen out-of-box dev kimliğidir (gerçek secret değil); prod'da env üzerinden override edilir (K3).
/// </summary>
public sealed record RabbitMqOptions
{
    /// <summary>Broker host adı.</summary>
    public string Host { get; init; } = "localhost";

    /// <summary>AMQP portu.</summary>
    public ushort Port { get; init; } = 5672;

    /// <summary>Sanal host.</summary>
    public string VirtualHost { get; init; } = "/";

    /// <summary>Kullanıcı adı (prod'da env'den).</summary>
    public string Username { get; init; } = "guest";

    /// <summary>Parola (prod'da env'den; repo'da gerçek değer tutulmaz).</summary>
    public string Password { get; init; } = "guest";
}
