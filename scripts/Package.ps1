#Requires -Version 7.3

param(
    [switch]$Zip,
    [string]$SonyClang = "$env:USERPROFILE\Tools\llvm-mingw-20261104-ucrt-x86_64\bin\clang++.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'app/SonyHeadphoneControl/Sony Headphone Control.csproj'
$portable = Join-Path $root 'dist/Sony Headphone Control-win-x64'

& dotnet publish $project -c Release -r win-x64 --self-contained true "-p:SonyClang=$SonyClang" `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o $portable
if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }

$source = Join-Path $portable ('Source-' + [guid]::NewGuid().ToString('N') + '.zip')
$paths = @(& git -C $root -c core.quotePath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Could not list repository source files.' }

$archive = [IO.Compression.ZipFile]::Open($source, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $paths) {
        $path = Join-Path $root $relative
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $path, $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
} finally {
    $archive.Dispose()
}

$archive = [IO.Compression.ZipFile]::OpenRead($source)
try {
    foreach ($required in 'app/SonyHeadphoneControl/Sony Headphone Control.csproj',
        'app/SonyHeadphoneControl/HeadphoneSettingsStore.cs', 'scripts/Package.ps1') {
        if (!$archive.GetEntry($required)) { throw "Missing source file $required" }
    }
} finally {
    $archive.Dispose()
}
[IO.File]::Move($source, (Join-Path $portable 'Source.zip'), $true)

if ($Zip) {
    $destination = Join-Path $root 'dist/Sony Headphone Control-win-x64.zip'
    $temporary = Join-Path $root ('dist/SonyHeadphoneControl-' + [guid]::NewGuid().ToString('N') + '.zip')
    Compress-Archive -LiteralPath $portable -DestinationPath $temporary -CompressionLevel Fastest
    [IO.File]::Move($temporary, $destination, $true)
    Get-Item -LiteralPath $destination | Select-Object FullName, Length
} else {
    Get-Item -LiteralPath $portable | Select-Object FullName
}
