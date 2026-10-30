$ErrorActionPreference='Stop'
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sdk=Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\UnionMetadata' -Filter Windows.winmd -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
if(!$sdk){throw 'Windows SDK metadata required for build; end users do not need the SDK.'}
$contracts=Get-ChildItem -LiteralPath 'C:\Program Files (x86)\Windows Kits\10\References\10.0.26100.0' -Recurse -Filter '*.winmd' | Where-Object Name -in @('Windows.Foundation.UniversalApiContract.winmd','Windows.Foundation.FoundationContract.winmd')
$contractArgs=@($contracts | ForEach-Object { '/r:'+ $_.FullName })
$out=Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $out | Out-Null
& (Join-Path $framework 'csc.exe') /nologo /optimize+ /target:winexe /platform:x64 /out:"$out\TerminalDeskNext.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Runtime.dll /r:"$framework\System.Runtime.WindowsRuntime.dll" @contractArgs "$PSScriptRoot\MediaTransport.cs" "$PSScriptRoot\Collectors.cs" "$PSScriptRoot\Program.cs" "$PSScriptRoot\Batteries.cs" "$PSScriptRoot\Tray.cs" "$PSScriptRoot\Pairing.cs" "$PSScriptRoot\FirstRun.cs"
if($LASTEXITCODE){throw 'Companion compilation failed'}
Write-Output 'Built TerminalDeskNext.exe (not installed or launched).'
