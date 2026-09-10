# Callback measurements

Run a fresh Release process for every comparison:

```powershell
dotnet run --project benchmarks/CallbackBenchmarks -c Release
```

Reports first proxy creation, warm reuse, and cold/warm creation for 64 distinct
runtime delegate types, including current-thread managed allocation counts.
Keep the runtime, architecture, machine load, and build configuration constant.
These are diagnostic microbenchmarks, not performance guarantees or CI thresholds.
Native thunk allocations are not included in the managed allocation counter.
