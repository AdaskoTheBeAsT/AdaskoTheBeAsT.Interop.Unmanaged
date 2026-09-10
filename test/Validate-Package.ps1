param([Parameter(Mandatory)][string] $Directory)
$ErrorActionPreference = 'Stop'
$tfms = @('net10.0', 'net9.0', 'net8.0', 'net481', 'net48', 'net472', 'net471', 'net47', 'net462')
$name = 'AdaskoTheBeAsT.Interop.Unmanaged'
$packages = @(Get-ChildItem -LiteralPath $Directory -Filter "$name.*.nupkg")
if ($packages.Count -ne 1) { throw "Expected one runtime package, found $($packages.Count)." }
$zip = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $paths = @($zip.Entries.FullName)
    foreach ($tfm in $tfms) {
        foreach ($extension in @('dll', 'xml')) {
            if ($paths -cnotcontains "lib/$tfm/$name.$extension") { throw "Missing $tfm $extension." }
        }
    }
    if (@($paths | Where-Object { $_ -match '^lib/[^/]+/[^/]+\.dll$' }).Count -ne $tfms.Count) {
        throw 'Unexpected target frameworks or assemblies in the package.'
    }
    if ($paths -match '(^|/)(native|fixture|test|samples|artifacts)(/|[._])|\.(so|dylib)$') {
        throw 'Test or native build artifacts leaked into the runtime package.'
    }
    foreach ($file in @('LICENSE', 'README.md')) {
        if ($paths -cnotcontains $file) { throw "Missing $file." }
    }
    $reader = [IO.StreamReader]::new($zip.GetEntry("$name.nuspec").Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($nuspec.SelectNodes("//*[local-name()='dependency']").Count -ne 0) { throw 'Unexpected runtime package dependencies.' }
    if (!$nuspec.package.metadata.repository.commit) { throw 'Package lacks a Source Link repository commit.' }
}
finally { $zip.Dispose() }

$symbols = [IO.Compression.ZipFile]::OpenRead([IO.Path]::ChangeExtension($packages[0].FullName, 'snupkg'))
try {
    foreach ($tfm in $tfms) {
        $entry = $symbols.GetEntry("lib/$tfm/$name.pdb")
        if (!$entry) { throw "Missing $tfm symbols." }
        $stream = [IO.MemoryStream]::new()
        $source = $entry.Open()
        try { $source.CopyTo($stream) } finally { $source.Dispose() }
        $stream.Position = 0
        $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($stream)
        try {
            $metadata = $provider.GetMetadataReader()
            $sourceLink = $null
            foreach ($id in $metadata.CustomDebugInformation) {
                $info = $metadata.GetCustomDebugInformation($id)
                if ($metadata.GetGuid($info.Kind) -eq [guid]'CC110556-A091-4D38-9FEC-25AB9A351A6A') {
                    $sourceLink = [Text.Encoding]::UTF8.GetString($metadata.GetBlobBytes($info.Value)) | ConvertFrom-Json
                }
            }
            if (!$sourceLink.documents) { throw "Missing Source Link mapping in $tfm." }
            Write-Host "$tfm : DLL, XML docs, portable symbols, and Source Link mapping verified."
        }
        finally { $provider.Dispose(); $stream.Dispose() }
    }
}
finally { $symbols.Dispose() }
Write-Host 'Package contents and dependency groups verified. Remote source checksums require a published commit.'
