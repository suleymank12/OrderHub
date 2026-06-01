// Test paralelizasyonu KAPALI. Gerekçe: ApiTestFactory connection string/secret'ı process-global env var
// ile veriyor (minimal hosting eager-config kısıtı — bkz. ApiTestFactory xml-doc). Paralel koleksiyonlar
// (Database + Api) aynı anda koşarsa env var'lar yarışabilir. Seri koşum bu riski tamamen ortadan kaldırır;
// integration testlerde container süresi baskın olduğundan paralellik kaybı ihmal edilebilir.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
