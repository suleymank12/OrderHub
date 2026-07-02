using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OrderHub.OrderProcessingService.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="HashSet{Guid}"/> ↔ JSON <c>string</c> kolon dönüşümü (saga fan-out kümeleri, Karar B). EF Core
/// koleksiyon property'lerini relational kolona kendiliğinden eşleyemez; küme tek bir <c>nvarchar</c> kolonda
/// JSON olarak saklanır. <b>System.Text.Json</b> kullanılır (Newtonsoft.Json yasak, K3). Boş/null JSON → boş küme
/// (saga ilk adımda kümeleri doldurmadan persist edilebilir). Mutasyon tespiti için <see cref="GuidSetValueComparer"/>
/// ile birlikte uygulanır.
/// </summary>
internal sealed class GuidSetJsonConverter : ValueConverter<HashSet<Guid>, string>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public GuidSetJsonConverter()
        : base(
            set => JsonSerializer.Serialize(set, SerializerOptions),
            json => Deserialize(json))
    {
    }

    private static HashSet<Guid> Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<HashSet<Guid>>(json, SerializerOptions) ?? [];
}
