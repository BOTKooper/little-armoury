param([switch]$LivePowerPolicy)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
$sources = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Where-Object { $_.Name -ne 'Program.cs' } | ForEach-Object { $_.FullName }
& $compilerPath /nologo /target:exe /platform:x64 /optimize+ /warn:4 /main:TinyAsus.Tests "/out:$testDirectory\Tests.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.ServiceProcess.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
if ($LivePowerPolicy) { & (Join-Path $testDirectory 'Tests.exe') --live-power-policy }
else { & (Join-Path $testDirectory 'Tests.exe') }
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
