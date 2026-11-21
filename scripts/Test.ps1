#Requires -Version 7.3

param(
    [string]$SonyClang = "$env:USERPROFILE\Tools\llvm-mingw-20261104-ucrt-x86_64\bin\clang++.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root '.build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
$protocolTest = Join-Path $build 'sony-transport-test.exe'
$native = Join-Path $root 'app/SonyHeadphoneControl/Native/Upstream'

& $SonyClang --target=x86_64-w64-windows-gnu -std=c++20 -fsigned-char -O2 -static `
    (Join-Path $root 'prep/sony-transport-test.cpp') `
    (Join-Path $native 'ByteMagic.cpp') (Join-Path $native 'CommandSerializer.cpp') `
    -lws2_32 -lbthprops -o $protocolTest
if ($LASTEXITCODE -ne 0) { throw 'Protocol test compilation failed.' }
& $protocolTest
if ($LASTEXITCODE -ne 0) { throw 'Protocol tests failed.' }

& dotnet run --project (Join-Path $root 'tests/UiCheck/UiCheck.csproj') -c Debug "-p:SonyClang=$SonyClang"
if ($LASTEXITCODE -ne 0) { throw 'UI regression checks failed.' }
