param([switch] $WithoutLegacyWorkaround)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("unmanaged-determinism-" + [guid]::NewGuid().ToString('N'))
$files = @(git -C $root ls-files --cached --others --exclude-standard) |
    Where-Object { $_ -notmatch '^(\.factory|\.vscode|test|samples|benchmarks)/' }
$remote = git -C $root remote get-url origin
try {
    [IO.Directory]::CreateDirectory($scratch) | Out-Null
    $bundle = Join-Path $scratch 'source.bundle'
    $revision = git -C $root rev-parse HEAD
    git -C $root bundle create $bundle HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Local source snapshot failed.' }
    foreach ($name in @('first', 'second')) {
        $copy = Join-Path $scratch $name
        git clone --quiet --no-checkout $bundle $copy
        if ($LASTEXITCODE -ne 0) { throw 'Local clone failed.' }
        git -C $copy checkout --quiet --detach $revision
        if ($LASTEXITCODE -ne 0) { throw 'Scratch checkout failed.' }
        git -C $copy remote set-url origin $remote
        foreach ($file in $files) {
            $source = Join-Path $root $file
            $target = Join-Path $copy $file
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target }
                continue
            }
            [IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
            Copy-Item -LiteralPath $source -Destination $target -Force
        }
        if ($WithoutLegacyWorkaround) {
            Set-Content -LiteralPath (Join-Path $copy 'Directory.Build.targets') -Value '<Project />'
        }
        foreach ($tfm in @('net10.0', 'net462')) {
            dotnet build (Join-Path $copy 'src/AdaskoTheBeAsT.Interop.Unmanaged/AdaskoTheBeAsT.Interop.Unmanaged.csproj') -c Release -f $tfm -p:ContinuousIntegrationBuild=true -p:GeneratePackageOnBuild=false --nologo
            if ($LASTEXITCODE -ne 0) { throw "Clean build failed for $name/$tfm." }
        }
    }
    foreach ($tfm in @('net10.0', 'net462')) {
        foreach ($extension in @('dll', 'pdb')) {
            $relative = "src/AdaskoTheBeAsT.Interop.Unmanaged/bin/Release/$tfm/AdaskoTheBeAsT.Interop.Unmanaged.$extension"
            $first = (Get-FileHash -LiteralPath (Join-Path $scratch "first/$relative")).Hash
            $second = (Get-FileHash -LiteralPath (Join-Path $scratch "second/$relative")).Hash
            if ($first -ne $second) { throw "Non-deterministic $tfm $extension." }
            Write-Host "$tfm $extension : identical SHA256 across independent checkout paths."
        }
    }
}
finally {
    # Only these two script-created clones are removed, never the source working tree.
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
