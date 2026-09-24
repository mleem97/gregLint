using Xunit;

// Console-capturing CLI tests share global Console.Out: run sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
