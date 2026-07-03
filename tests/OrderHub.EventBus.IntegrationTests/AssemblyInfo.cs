// Gerçek Kafka container fixture'ı → paralel çalışma port/container çakışmasına yol açar; seri çalışma zorunlu
// (diğer integration assembly'leriyle aynı disiplin). Ayrıca test-runner'da projeler zaten proje-proje sıralı koşar.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
