// Fixture process-global env var (JWT secret + YARP destination + rate-limit) set eder → assembly genelinde
// test paralelizasyonu kapalı (env yarışı olmasın; ApiTestFactory deseni).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
