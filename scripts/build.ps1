param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'Build this project on Windows.'
}
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) {
    throw 'The 64-bit .NET Framework C# compiler is not installed.'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $taskRoot 'artifacts'
}
$taskTests = Join-Path $taskRoot '.build'
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $taskTests | Out-Null
$taskExecutable = Join-Path $OutputDirectory 'ModafenClientXZERO.exe'
$taskSmoke = Join-Path $taskTests 'SmokeChecks.exe'
$taskSource = @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName })
$taskManifest = Join-Path $taskRoot 'src\app.manifest'
$taskReferences = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Net.Http.dll', '/r:System.Web.Extensions.dll')
$taskAppArguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warnaserror+', "/win32manifest:$taskManifest", "/out:$taskExecutable") + $taskReferences + $taskSource
& $taskCompiler @taskAppArguments
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
$taskCheckArguments = @('/nologo', '/target:exe', '/platform:x64', '/optimize+', '/warnaserror+', '/main:Cs2Overlay.SmokeChecks', "/out:$taskSmoke") + $taskReferences + $taskSource + @((Join-Path $taskRoot 'tests\SmokeChecks.cs'))
& $taskCompiler @taskCheckArguments
if ($LASTEXITCODE -ne 0) { throw 'Smoke-check compilation failed.' }
& $taskSmoke (Join-Path $OutputDirectory 'smoke-checks.json')
if ($LASTEXITCODE -ne 0) { throw 'Windows smoke checks failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'QUICKSTART.txt') -Destination $OutputDirectory -Force
$taskHash = Get-FileHash -LiteralPath $taskExecutable -Algorithm SHA256
($taskHash.Hash.ToLowerInvariant() + '  ModafenClientXZERO.exe') | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
[ordered]@{
    version = '0.3.0'
    source_commit = $env:GITHUB_SHA
    built_at_utc = [DateTime]::UtcNow.ToString('o')
    compiler_version = (Get-Item -LiteralPath $taskCompiler).VersionInfo.FileVersion
    executable_sha256 = $taskHash.Hash.ToLowerInvariant()
    in_game_tested = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'build-info.json') -Encoding utf8
Write-Host "Created $taskExecutable"
