// Container paylaşımlı fixture'lar (SQL + Kafka) + Api factory env-var seam → testler seri çalışmalı
// (paralel container/port ve env-var çakışması yok). CollectionBehavior, global 'using Xunit'
// (tests/Directory.Build.props) ile erişilir.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
