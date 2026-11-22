# Benchmarks (large-mod optimisation)

Method: standalone harness (`tools/BenchmarkHarness`, .NET 8, Linux VM) that
replicates the original and the optimized lookup code paths shape-for-shape,
plus a real archive file (5,000 small files + one 20 MiB file, 21.0 MiB zip,
5,001 entries). Result counts (matches/hits) were identical for old vs new
in every test — the optimisations change cost, not results.

| Test | Original | Optimized |
|---|---|---|
| Install archive matching, one 5,000-file mod (linear `FirstOrDefault` vs dictionary) | 382 ms | 3 ms |
| Real zip: matching all 5,001 entries | 394 ms | 6 ms |
| `FindModsWithSameFile` for 500 files against a 60-mod / 30,000-file cache (all-mods×all-files scan with `ToLower()` per comparison vs indexes built once) | 3,304 ms | 167 ms |
| `GetArchiveFiles` parent linking, 5,200 entries (`List.Find` per file vs directory dictionary) | 13 ms | 1 ms |
| CRC of the same 20 MiB installed file across 10 check passes (re-hash every time vs memo + stamp-validated cache) | 897 ms | 77 ms |

Not captured by these numbers (the actual user-visible fix): installs/scans
no longer run on the UI thread, so the window no longer goes
"Not Responding", and a throttled progress bar + Cancel button are shown.

Reproduce: create a zip like the one above, then
`dotnet run -c Release --project tools/BenchmarkHarness`
(the harness expects `/tmp/bench/large_mod.zip`; adjust the path in Program.cs).
