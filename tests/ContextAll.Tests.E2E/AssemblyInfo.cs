using Xunit;

// Disable test parallelization across the E2E suite to guarantee zero port collisions
// and prevent race conditions on process-wide environment variables.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
