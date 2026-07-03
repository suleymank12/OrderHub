using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace OrderHub.Gateway.Observability;

/// <summary>
/// Serilog enricher: aktif <see cref="Activity"/>'nin <c>TraceId</c>/<c>SpanId</c>'sini her log satırına ekler →
/// Seq'te gateway loglarını trace-id ile trace'e bağlar. ★ Gateway pure-edge (OrderHub.Observability building
/// block'a ProjectRef YOK) → building block'taki enricher'ın küçük kopyası (JWT/Serilog config kopyalama deseniyle
/// tutarlı). Aktif activity yoksa sessizce atlar.
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
