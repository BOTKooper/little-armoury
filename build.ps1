param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework 4.8 compiler is required (included with Windows 10/11).' }
if ([String]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $PSScriptRoot 'dist' }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sourceFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | Where-Object { $_.Name -notmatch 'Tests\.cs$' } | ForEach-Object { $_.FullName }
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /warn:4 "/out:$outputDirectory\LittleArmoury.exe" "/win32manifest:$PSScriptRoot\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Management.dll /reference:System.ServiceProcess.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LittleArmoury.exe.config') -Destination $outputDirectory
Get-Item -LiteralPath (Join-Path $outputDirectory 'LittleArmoury.exe') | Select-Object FullName, Length
