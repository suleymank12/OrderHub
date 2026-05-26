namespace OrderHub.OrderService.Domain.ValueObjects;

/// <summary>
/// Para değeri value object'i: <see cref="Amount"/> + <see cref="Currency"/>. Negatif tutar
/// reddedilir; farklı currency'ler toplanamaz. Immutable (record → value equality).
/// Aritmetik operatörler CA2225 uyumu için <see cref="Add"/>/<see cref="Multiply"/> ile eşlenir.
/// </summary>
public sealed record Money
{
    /// <summary>Tutar (negatif olamaz).</summary>
    public decimal Amount { get; }

    /// <summary>Para birimi.</summary>
    public Currency Currency { get; }

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Money üretir. Negatif tutar → <see cref="ArgumentOutOfRangeException"/> (VO precondition).</summary>
    public static Money Create(decimal amount, Currency currency)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Money amount cannot be negative.");
        }

        return new Money(amount, currency);
    }

    /// <summary>Belirtilen currency'de sıfır tutarlı Money.</summary>
    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>Aynı currency'deki iki Money'i toplar. Farklı currency → <see cref="InvalidOperationException"/>.</summary>
    public Money Add(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Currency != other.Currency)
        {
            throw new InvalidOperationException(
                $"Cannot add money of different currencies ({Currency} and {other.Currency}).");
        }

        return new Money(Amount + other.Amount, Currency);
    }

    /// <summary>Tutarı negatif olmayan bir adetle çarpar. Negatif adet → <see cref="ArgumentOutOfRangeException"/>.</summary>
    public Money Multiply(int quantity)
    {
        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity cannot be negative.");
        }

        return new Money(Amount * quantity, Currency);
    }

    /// <summary>Money dizisini toplar. Dizi boş → <see cref="InvalidOperationException"/> (currency belirlenemez).</summary>
    public static Money Sum(IEnumerable<Money> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Money? total = null;
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            total = total is null ? item : total.Add(item);
        }

        return total ?? throw new InvalidOperationException("Cannot sum an empty money sequence.");
    }

    /// <summary><see cref="Add"/> için operatör.</summary>
    public static Money operator +(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Add(right);
    }

    /// <summary><see cref="Multiply"/> için operatör.</summary>
    public static Money operator *(Money money, int quantity)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Multiply(quantity);
    }
}
