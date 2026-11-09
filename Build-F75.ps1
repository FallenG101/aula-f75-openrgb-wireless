$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The x64 Windows .NET Framework C# compiler was not found.' }
$bridge = Join-Path $PSScriptRoot 'F75-Wireless-Bridge.exe'
$diagnostic = Join-Path $PSScriptRoot 'F75-Receiver-Diagnostics.exe'
$sourceFiles = @('F75-HidTransport.cs','F75-Wireless-Bridge.cs','F75-Bridge-Tests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compiler /nologo /target:winexe /platform:x64 /main:F75WirelessBridge "/out:$bridge" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Companion build failed. Close any running copy before rebuilding.' }
& $compiler /nologo /target:winexe /platform:x64 "/out:$diagnostic" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'F75-Receiver-Diagnostics.cs')
if ($LASTEXITCODE -ne 0) { throw 'Diagnostic build failed. Close any running copy before rebuilding.' }
$testProcess = Start-Process -FilePath $bridge -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($testProcess.ExitCode -ne 0) { throw 'Offline verification failed; inspect F75-self-test.txt.' }
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'F75-self-test.txt')
