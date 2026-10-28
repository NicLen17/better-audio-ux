param(
    [switch]$Test,
    [switch]$Live,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskVendorMetadata = Get-Content -LiteralPath 'ThirdParty\SoundVolumeCommandLine.provenance.json' -Raw | ConvertFrom-Json
$taskVendorHash = (Get-FileHash -LiteralPath 'ThirdParty\SoundVolumeCommandLine\svcl.exe' -Algorithm SHA256).Hash
if ($taskVendorHash -ne $taskVendorMetadata.executableSha256) { throw 'El componente espacial no coincide con la version verificada.' }
$taskLocalSdk = Join-Path $env:LOCALAPPDATA 'BetterAudio\sdk'
$taskDotnet = Join-Path $taskLocalSdk 'dotnet.exe'
if (Test-Path -LiteralPath $taskDotnet) {
    $env:DOTNET_ROOT = $taskLocalSdk
} else {
    $taskCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (!$taskCommand) { throw 'Instala el SDK de .NET 10 desde https://dotnet.microsoft.com/download/dotnet/10.0' }
    $taskDotnet = $taskCommand.Source
}
function Invoke-BetterAudioDotnet {
    & $taskDotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet termino con codigo $LASTEXITCODE" }
}
if ($Test -or $Live) {
    Invoke-BetterAudioDotnet build AudioFlyoutApp.Tests -c Release --nologo
    Invoke-BetterAudioDotnet AudioFlyoutApp.Tests/bin/Release/net10.0-windows/AudioFlyoutApp.Tests.dll
    if ($Live) { Invoke-BetterAudioDotnet AudioFlyoutApp.Tests/bin/Release/net10.0-windows/AudioFlyoutApp.Tests.dll --live }
}
$taskRunning = Get-Process -Name BetterAudio -ErrorAction SilentlyContinue
if ($taskRunning) { throw 'Cierra Better Audio desde el menu de su icono (Salir) antes de publicar.' }
Invoke-BetterAudioDotnet publish AudioFlyoutApp/AudioFlyoutApp.csproj -c Release -r win-x64 --self-contained true '-p:PublishReadyToRun=true' '-p:DebugType=None' -o artifacts/BetterAudio --nologo
Copy-Item -LiteralPath README.md,THIRD-PARTY-NOTICES.md -Destination artifacts/BetterAudio
Copy-Item -LiteralPath docs -Destination artifacts/BetterAudio -Recurse -Force
Copy-Item -LiteralPath licenses/dotnet/LICENSE.txt,licenses/dotnet/ThirdPartyNotices.txt -Destination artifacts/BetterAudio
$taskShell = New-Object -ComObject WScript.Shell
$taskShortcut = $taskShell.CreateShortcut((Join-Path $PSScriptRoot 'Better Audio.lnk'))
$taskShortcut.TargetPath = Join-Path $PSScriptRoot 'artifacts\BetterAudio\BetterAudio.exe'
$taskShortcut.WorkingDirectory = Join-Path $PSScriptRoot 'artifacts\BetterAudio'
$taskShortcut.Description = 'Better Audio - selector rapido de salidas de audio'
$taskShortcut.Save()
if ($Package) {
    $taskZipPath = Join-Path $PSScriptRoot 'artifacts\BetterAudio-win-x64.zip'
    Compress-Archive -Path 'artifacts\BetterAudio' -DestinationPath $taskZipPath -Force
    Write-Host "Paquete: $taskZipPath"
}
Write-Host 'Listo. Abri Better Audio.lnk o artifacts\BetterAudio\BetterAudio.exe'

