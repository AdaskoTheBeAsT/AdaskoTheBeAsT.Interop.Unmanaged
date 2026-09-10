# Consumer smoke tests

`NativeSmoke` is an ordinary consumer without `InternalsVisibleTo`. It exercises
typed/raw lookup, static handles, callback scopes, an `UnmanagedCallersOnly`
callback, and a rooted retained callback. Lifecycle failures run in an isolated
process and must not unwind into an unload while native threads are active.

Build the fixture first:

```powershell
dotnet build test/unit/AdaskoTheBeAsT.Interop.Unmanaged.Test -c Release -f net10.0
dotnet publish samples/NativeSmoke -c Release -r win-x64 --self-contained true -p:SmokePublishMode=Trimmed -o artifacts/trimmed
test/Run-NativeSmoke.ps1 -Executable artifacts/trimmed/NativeSmoke.exe -Library test/unit/AdaskoTheBeAsT.Interop.Unmanaged.Test/bin/Release/net10.0/native/fixture.dll
dotnet publish samples/NativeSmoke -c Release -r win-x64 --self-contained true -p:SmokePublishMode=Aot -o artifacts/aot
test/Run-NativeSmoke.ps1 -Executable artifacts/aot/NativeSmoke.exe -Library test/unit/AdaskoTheBeAsT.Interop.Unmanaged.Test/bin/Release/net10.0/native/fixture.dll
```

For Linux use `linux-x64`, `NativeSmoke`, and `libfixture.so`; for macOS use
`osx-x64` (or the appropriate ARM64 RID), `NativeSmoke`, and `libfixture.dylib`.
NativeAOT needs the platform's native linker/toolchain.

Use `SmokePublishMode`, not the global `PublishTrimmed`/`PublishAot` property:
global publish properties otherwise also reach legacy project-reference targets.
The generic callback's trimmed JIT example explicitly preserves its known
`Func<int,int>.Invoke` metadata. Two narrowly scoped warning suppressions cover
that guarded example and the expected NativeAOT rejection tests, not library code.

To test the produced package rather than a project reference:

```powershell
dotnet build src/AdaskoTheBeAsT.Interop.Unmanaged -c Release -p:Version=3.0.1-local
dotnet pack src/AdaskoTheBeAsT.Interop.Unmanaged -c Release -p:Version=3.0.1-local -o artifacts/packages
test/Validate-Package.ps1 -Directory artifacts/packages
$packages = (Resolve-Path artifacts/packages).Path
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@ | Set-Content artifacts/NuGet.config
dotnet restore samples/NativeSmoke -r win-x64 --configfile artifacts/NuGet.config -p:SmokePackageVersion=3.0.1-local -p:SmokePublishMode=Aot
dotnet publish samples/NativeSmoke -c Release -r win-x64 --self-contained true --no-restore -p:SmokePackageVersion=3.0.1-local -p:SmokePublishMode=Aot -o artifacts/package-aot
```

`RetainedCallback` stores its callback root in `_binder`. It calls the fixture's
synchronous unregister before clearing that field. The external module owner
must stay undisposed, and callers must serialize registration disposal.
