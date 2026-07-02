using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace OrderHub.OrderProcessingService.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="HashSet{Guid}"/> property'leri için EF Core değer karşılaştırıcısı. Referans tipli koleksiyonlarda
/// EF, snapshot/eşitlik için açık bir comparer ister; aksi halde mutasyonları (küme'ye yeni ProductId ekleme)
/// güvenilir tespit edemez ve sessizce kaybedebilir. Eşitlik küme içeriğine göredir (<c>SetEquals</c>),
/// snapshot <b>derin kopya</b>dır (yeni <see cref="HashSet{Guid}"/>) → orijinal değişse bile snapshot bozulmaz.
/// </summary>
internal sealed class GuidSetValueComparer : ValueComparer<HashSet<Guid>>
{
    public GuidSetValueComparer()
        : base(
            (left, right) => left == null ? right == null : right != null && left.SetEquals(right),
            set => set.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
            set => new HashSet<Guid>(set))
    {
    }
}
