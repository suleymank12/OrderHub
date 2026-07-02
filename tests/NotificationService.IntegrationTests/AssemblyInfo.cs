// Container paylaşımlı fixture'lar (SQL + Kafka) → testler seri çalışmalı
// (paralel container/port çakışması yok). CollectionBehavior, global 'using Xunit'
// (tests/Directory.Build.props) ile erişilir.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
