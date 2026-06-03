// Orchestrator tests mutate AnsiConsole.Console (Spectre global state), so run tests serially.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
