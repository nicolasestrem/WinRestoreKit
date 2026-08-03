using Xunit;

// Many tests in this assembly mutate process-wide mutable state that has no per-test isolation
// seam: Data.DataRootDir is a public static field, Theme.Current is a static instance, and several
// tests write directly to the real HKCU\Software\WinRestoreKit registry key. xUnit parallelizes
// test CLASSES by default (each class not grouped into a shared collection runs as its own
// collection, concurrently with every other collection), so two classes racing to set
// Data.DataRootDir to different temporary directories, or to read and restore the same registry
// value, produce failures that never reproduce when either class runs alone. Every fixer in this
// repository's history has verified green running its own filtered test class; the failures this
// attribute prevents were interference between classes, not defects in any one of them.
//
// Disabling collection-level parallelism trades away concurrent test execution for correctness.
// The whole suite runs in about a second either way, so the trade costs nothing observable and
// removes an entire category of order-dependent, hardware-dependent flakiness.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
