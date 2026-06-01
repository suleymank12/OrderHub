namespace OrderHub.OrderService.Application.Abstractions.Identity;

/// <summary>
/// İçinde bulunulan request'in kimliği doğrulanmış kullanıcısını sağlayan port. Handler'lar müşteri/sahip
/// kimliğini <b>asla request body'sinden</b> okumaz, daima buradan alır → IDOR / mass-assignment koruması (K3).
/// Implementasyon Api katmanında (Faz 1.5, JWT claim'lerinden) gelir; bir port olduğu için Faz 1.3'te
/// implementasyonsuz olması K2 ihlali değildir (port-and-adapter; unit testlerde mock'lanır).
/// </summary>
public interface ICurrentUserService
{
    /// <summary>Kimliği doğrulanmış kullanıcının kimliği. Kimlik yoksa <see cref="Guid.Empty"/>.</summary>
    Guid UserId { get; }

    /// <summary>Request kimlik doğrulamasından geçti mi?</summary>
    bool IsAuthenticated { get; }
}
