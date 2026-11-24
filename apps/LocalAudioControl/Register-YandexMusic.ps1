param([switch]$Disable)
$ErrorActionPreference = 'Stop'
$hostName = 'local_audio_control.yandex_music'
$registryKey = "HKCU:\Software\Google\Chrome\NativeMessagingHosts\$hostName"
$manifestPath = Join-Path $PSScriptRoot 'yandex-music-host.json'

if ($Disable) {
    if (Test-Path -LiteralPath $registryKey) { Remove-Item -LiteralPath $registryKey }
    if (Test-Path -LiteralPath $manifestPath) { Remove-Item -LiteralPath $manifestPath }
    return
}

$hostPath = Join-Path $PSScriptRoot 'YandexMusicHost.exe'
if (!(Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw 'YandexMusicHost.exe must be next to this script.'
}
$manifest = @{
    name = $hostName
    description = 'Local Audio Control - Yandex Music'
    path = $hostPath
    type = 'stdio'
    allowed_origins = @('chrome-extension://gaohjpkalnbngfcobkdalgmeibfkfhhk/')
}
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
[Microsoft.Win32.Registry]::SetValue("HKEY_CURRENT_USER\Software\Google\Chrome\NativeMessagingHosts\$hostName", '', $manifestPath)
Write-Output 'Yandex Music host registered. Reload the extension in Chrome.'
