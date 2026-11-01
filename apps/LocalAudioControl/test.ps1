param([switch]$AudioIntegration)
$ErrorActionPreference = 'Stop'
if ($AudioIntegration -and $PSVersionTable.PSEdition -eq 'Core') {
    & (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -NoProfile -File $PSCommandPath -AudioIntegration
    exit $LASTEXITCODE
}
$project = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
& (Join-Path $project 'build.ps1') -AppOnly | Out-Null
$uia = Join-Path (Split-Path $compiler -Parent) 'WPF'
$sources = 'CoreAudio.cs','Settings.cs','Program.cs','UserInterface.cs','MouseCommands.cs','FocusGuard.cs','SmtcMedia.cs','Tests.cs' | ForEach-Object { Join-Path $project $_ }
$test = Join-Path $project 'build\Tests.exe'
& $compiler /nologo /codepage:65001 /target:exe /platform:anycpu /main:LocalAudioControl.Tests /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll ('/reference:'+(Join-Path $uia 'UIAutomationClient.dll')) ('/reference:'+(Join-Path $uia 'UIAutomationTypes.dll')) ('/out:'+$test) @sources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $test (Join-Path $project 'build\qa')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
if ($AudioIntegration) {
    # This optional integration test changes actual audio. Run when no one is adjusting it.
    [void][Reflection.Assembly]::LoadFrom((Join-Path $project 'build\LocalAudioControl.exe'))
    $original0 = [LocalAudioControl.Audio]::DefaultId(0)
    $original1 = [LocalAudioControl.Audio]::DefaultId(1)
    $original2 = [LocalAudioControl.Audio]::DefaultId(2)
    $preferences = [LocalAudioControl.Storage]::Load()
    $saved = @{}
    try {
        foreach ($id in @($preferences.Favorite1,$preferences.Favorite2)) {
            if (-not $id) { throw 'Configure both favorites before the audio integration test.' }
            [LocalAudioControl.Audio]::Select($id)
            $state = [LocalAudioControl.Audio]::State()
            $saved[$id] = $state
            if ($state.DeviceId -ne $id) { throw 'Wrong playback endpoint.' }
            [LocalAudioControl.Audio]::SetPercent(12)
            if ([LocalAudioControl.Audio]::State().Percent -ne 12) { throw 'Volume set failed.' }
            [LocalAudioControl.Audio]::SetPercent(15)
            if ([LocalAudioControl.Audio]::State().Percent -ne 15) { throw 'Volume step failed.' }
            [LocalAudioControl.Audio]::SetMute($false)
            [LocalAudioControl.Audio]::SetPercent(0)
            $zero = [LocalAudioControl.Audio]::State()
            if ($zero.Percent -ne 0 -or -not $zero.Muted) { throw 'Zero volume must mute the endpoint.' }
            [LocalAudioControl.Audio]::SetPercent(3)
            $resumed = [LocalAudioControl.Audio]::State()
            if ($resumed.Percent -ne 3 -or $resumed.Muted) { throw 'Increasing from zero must resume audio at 3%.' }
            [LocalAudioControl.Audio]::SetMute($true)
            [LocalAudioControl.Audio]::SetPercent(2)
            if (-not [LocalAudioControl.Audio]::State().Muted) { throw 'Lowering volume must preserve mute.' }
            [LocalAudioControl.Audio]::SetPercent(5)
            if ([LocalAudioControl.Audio]::State().Muted) { throw 'Raising volume must resume audio even if already above zero.' }
            foreach ($level in @(9,6,3,0,3,0,3,0)) {
                [LocalAudioControl.Audio]::SetPercent($level)
                Start-Sleep -Milliseconds 200
                $settled = [LocalAudioControl.Audio]::State()
                if ($settled.Percent -ne $level -or $settled.Muted -ne ($level -eq 0)) { throw 'Settled volume/mute cycle failed.' }
            }
            [LocalAudioControl.Audio]::SetMute(-not $state.Muted)
            if ([LocalAudioControl.Audio]::State().Muted -eq $state.Muted) { throw 'Mute failed.' }
            if ([LocalAudioControl.Audio]::DefaultId(2) -ne $original2) { throw 'Communications changed.' }
        }
        Write-Output 'Audio selection, volume, mute and communications isolation passed.'
    } finally {
        foreach ($id in $saved.Keys) {
            [LocalAudioControl.Audio]::Select($id)
            [LocalAudioControl.Audio]::SetPercent($saved[$id].Percent)
            [LocalAudioControl.Audio]::SetMute($saved[$id].Muted)
        }
        [LocalAudioControl.Audio]::RestoreOutputs($original0,$original1)
        Write-Output 'Original audio state restored.'
    }
}
