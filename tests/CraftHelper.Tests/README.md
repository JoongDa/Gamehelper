# CraftHelper automated unit tests

Run from the repository root on Windows with .NET 10:

```powershell
dotnet run --project tests\CraftHelper.Tests\CraftHelper.Tests.csproj -c Release
```

This executable runner links the CraftHelper matcher, configuration, adapter,
settings and the existing `IPSettings` interface. It has no external package or
Core-reader/project dependency, requires no game process and does not load ImGui.
Every named test executes; failures print their assertion and return exit code 1.
The repository's shared runtime identifier is `win-x64`.

Coverage includes AND/OR, exact ordinal IDs, all mod categories, value0/value1,
inclusive numeric bounds, absence, invalid reads/configuration, missing values,
duplicate IDs, Core float values and integer-threshold precision, invalid precedence,
copied list freshness, immutable evidence, strict configuration and saved settings,
NaN slot preservation, malformed adapter data and all-empty live-read handling.

Explicitly valid empty inputs in pure matcher tests are caller-provided fixtures.
They do not certify empty live reads: the adapter conservatively returns Invalid
when all three Core lists are empty. Core has no public completeness flag; a partial
read returning plausible nonempty data cannot be detected by these tests.
Current process memory, `ReadFreshItem` refresh behavior and inventory/stash hover
still require Windows + PoE2 verification. Use `dotnet run`; `dotnet test` does not
execute this console runner.
