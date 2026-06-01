namespace OrderHub.Common.Exceptions;

/// <summary>
/// Tüm domain (invariant) ihlali exception'larının soyut tabanı. Doğrudan fırlatılmaz;
/// her servisin Domain katmanı kendi anlamlı türevlerini tanımlar
/// (ör. <c>EmptyOrderException</c>). Api global exception middleware'i bu tip ailesini
/// HTTP status'a (400/409) map'ler. Bir domain exception daima mesaj taşır →
/// parametresiz ctor bilinçli olarak yoktur.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>Mesajlı domain exception.</summary>
    protected DomainException(string message)
        : base(message)
    {
    }

    /// <summary>Mesajlı + iç exception'lı domain exception.</summary>
    protected DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
