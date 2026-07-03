using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace OrderHub.Observability;

/// <summary>
/// Serilog enricher: aktif <see cref="Activity"/>'nin <c>TraceId</c>/<c>SpanId</c>'sini her log satırına property
/// olarak ekler → Seq'te bir trace'in loglarını trace-id ile filtreleyip trace↔log korelasyonu kurulur. Aktif
/// activity yoksa (trace dışı log) sessizce atlar. OTel'in ürettiği W3C trace-id ile aynı değer.
/// </summary>
internal sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
