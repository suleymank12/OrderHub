namespace OrderHub.Common.Results;

/// <summary>
/// Bir işlemin başarı/başarısızlık sonucunu temsil eder. Beklenen iş sonuçlarını
/// exception fırlatmadan ifade etmek için Application katmanında kullanılır (Domain
/// invariant ihlallerinde <c>DomainException</c> fırlatılır). Geçersiz durumlar
/// (başarı + hata, ya da başarısızlık + <see cref="Error.None"/>) ctor'da engellenir.
/// </summary>
public class Result
{
    /// <summary>Geçersiz başarı/hata kombinasyonunu engelleyen korumalı ctor.</summary>
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>İşlem başarılı mı?</summary>
    public bool IsSuccess { get; }

    /// <summary>İşlem başarısız mı?</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Başarısızlıkta hata; başarıda <see cref="Error.None"/>.</summary>
    public Error Error { get; }

    /// <summary>Değer taşımayan başarılı sonuç.</summary>
    public static Result Success() => new(true, Error.None);

    /// <summary>Başarısız sonuç.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Değer taşıyan başarılı sonuç.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Değer tipli başarısız sonuç.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// Başarı durumunda bir <typeparamref name="TValue"/> taşıyan sonuç. Başarısızken
/// <see cref="Value"/>'ya erişim <see cref="InvalidOperationException"/> fırlatır
/// (sessizce <c>default</c> dönmez).
/// </summary>
/// <typeparam name="TValue">Başarı durumunda taşınan değerin tipi.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    /// <summary>Yalnızca <see cref="Result"/> factory'lerinden üretilir.</summary>
    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>Başarıdaki değer; başarısızsa erişim exception fırlatır.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The value of a failed result cannot be accessed.");
}
