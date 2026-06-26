// Container-paylaşımlı fixture'lar + gerçek broker consumer testleri → paralel çalışma port/container
// çakışmasına yol açar; seri çalışma zorunludur. CollectionBehavior global 'using Xunit' ile erişilir.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
