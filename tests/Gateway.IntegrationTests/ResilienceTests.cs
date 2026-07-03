using System.Net;
using OrderHub.Gateway.IntegrationTests.Fixtures;

namespace OrderHub.Gateway.IntegrationTests;

/// <summary>
/// Faz 6 6b-1 — gateway→downstream resilience (per-cluster circuit-breaker + per-attempt timeout, RETRY YOK).
/// ★ Deterministik (5d-7 flaky dersi): CB kanıtının çapası <b>downstream istek sayısı</b> — circuit OPEN olunca
/// istek downstream'e ULAŞMAZ (WireMock hit sayısı ARTMAZ). "Sayaç dondu = CB devrede", timing'e bağlı değil.
/// Her test AYRI cluster kullanır → per-cluster CB state izole (test'ler birbirini kirletmez).
/// </summary>
[Collection(ResilienceAppCollection.Name)]
public sealed class ResilienceTests(ResilienceAppFixture app)
{
    [Fact]
    public async Task CircuitBreaker_AfterRepeatedTransientFailures_OpensAndShieldsDownstream()
    {
        const string cluster = "res-a";
        app.StubStatus(cluster, 500); // transient → CB sayar
        var client = app.CreateClient();

        // Tekrarlı 500 gönder; circuit OPEN olduğunda istek downstream'e ulaşmaz (hit sayısı sabit kalır).
        HttpResponseMessage? shielded = null;
        for (var i = 0; i < ResilienceAppFixture.MinimumThroughput * 3; i++)
        {
            var before = app.DownstreamHits(cluster);
            var response = await client.GetAsync($"/{cluster}/x");
            if (app.DownstreamHits(cluster) == before) // istek downstream'e GİTMEDİ → CB OPEN
            {
                shielded = response;
                break;
            }
        }

        shielded.Should().NotBeNull("tekrarlı transient fail sonrası circuit OPEN olup downstream'i korumalı");
        ((int)shielded!.StatusCode).Should().BeGreaterThanOrEqualTo(500, "CB OPEN fast-fail bir sunucu hatası döndürmeli");

        // ★ İkinci kez doğrula: OPEN iken bir istek daha → downstream hit sayısı ARTMAZ (kesin fast-fail kanıtı).
        var hitsWhenOpen = app.DownstreamHits(cluster);
        var extra = await client.GetAsync($"/{cluster}/x");
        app.DownstreamHits(cluster).Should().Be(hitsWhenOpen, "CB OPEN iken istek downstream'e GİTMEMELİ (fast-fail)");
        ((int)extra.StatusCode).Should().BeGreaterThanOrEqualTo(500);
    }

    [Fact]
    public async Task CircuitBreaker_IsPerCluster_OpenOnOneDoesNotAffectAnother()
    {
        const string failing = "res-b";
        const string healthy = "res-c";
        app.StubStatus(failing, 500);
        app.StubStatus(healthy, 200);
        var client = app.CreateClient();

        // res-b circuit'ini OPEN et (downstream'e ulaşmayan ilk isteğe kadar).
        for (var i = 0; i < ResilienceAppFixture.MinimumThroughput * 3; i++)
        {
            var before = app.DownstreamHits(failing);
            await client.GetAsync($"/{failing}/x");
            if (app.DownstreamHits(failing) == before)
            {
                break;
            }
        }

        // ★ res-c AYRI circuit → hâlâ sağlıklı: istek downstream'e forward edilir ve 200 döner (izolasyon).
        var healthyBefore = app.DownstreamHits(healthy);
        var response = await client.GetAsync($"/{healthy}/y");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "farklı cluster'ın circuit'i açık olsa da bu cluster çalışmalı");
        app.DownstreamHits(healthy).Should().Be(healthyBefore + 1, "sağlıklı cluster isteği downstream'e forward edilmeli");
    }

    [Fact]
    public async Task AttemptTimeout_SlowDownstream_ReturnsBoundedErrorNotTheDelayedSuccess()
    {
        const string cluster = "res-d";
        // Downstream gecikmesi AttemptTimeout'tan büyük → Polly timeout ÖNCE tetiklenir (gecikmeli 200 dönmez).
        app.StubSlow(cluster, TimeSpan.FromSeconds(ResilienceAppFixture.AttemptTimeoutSeconds + 3));
        var client = app.CreateClient();

        var response = await client.GetAsync($"/{cluster}/z");

        // Tek yavaş çağrı = 1 fail (CB açılmaz, throughput<min) → saf timeout kanıtı: 200 DEĞİL, bounded hata.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK, "AttemptTimeout gecikmeli 200'ü kesmeli");
        ((int)response.StatusCode).Should().BeGreaterThanOrEqualTo(500, "timeout bounded bir sunucu hatası döndürmeli");
    }
}
