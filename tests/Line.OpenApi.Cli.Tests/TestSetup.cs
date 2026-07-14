// Credential resolution reads process-global environment variables, so disable parallelization
// to keep those tests deterministic.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
