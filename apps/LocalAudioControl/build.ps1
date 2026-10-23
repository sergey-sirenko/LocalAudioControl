param([string]$IsccPath, [switch]$AppOnly)
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$repository = (Resolve-Path (Join-Path $project '..\..')).Path
$build = Join-Path $project 'build'
$dist = Join-Path $repository 'dist'
New-Item -ItemType Directory -Path $build,(Join-Path $build 'Commands'),$dist -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$sources = 'AssemblyInfo.cs','CoreAudio.cs','Settings.cs','Program.cs','UserInterface.cs' | ForEach-Object { Join-Path $project $_ }
& $compiler /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /main:LocalAudioControl.Program /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll ('/win32manifest:'+(Join-Path $project 'app.manifest')) ('/out:'+(Join-Path $build 'LocalAudioControl.exe')) @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item (Join-Path $project 'LocalAudioControl.exe.config') $build -Force
& $compiler /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ ('/out:'+(Join-Path $build 'Commands\Favorite1.exe')) (Join-Path $project 'Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Command launcher compilation failed.' }
'Favorite2','VolumeUp','VolumeDown' | ForEach-Object { Copy-Item (Join-Path $build 'Commands\Favorite1.exe') (Join-Path $build ('Commands\'+$_+'.exe')) -Force }
$legacyBuild = Join-Path $build 'LegacyCommands'
New-Item -ItemType Directory -Path $legacyBuild -Force | Out-Null
& $compiler /nologo /codepage:65001 /target:winexe /platform:anycpu ('/out:'+(Join-Path $legacyBuild 'Local-Volume-Up.exe')) (Join-Path $project 'LegacyVolumeLauncher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Legacy volume launcher compilation failed.' }
Copy-Item (Join-Path $legacyBuild 'Local-Volume-Up.exe') (Join-Path $legacyBuild 'Local-Volume-Down.exe') -Force
Get-ChildItem $build -Filter '*.exe' -Recurse | Get-FileHash | Select-Object Path,Hash
if (-not $AppOnly) {
    if (-not $IsccPath) {
        $candidate = Get-Command iscc.exe -ErrorAction SilentlyContinue
        if ($candidate) { $IsccPath = $candidate.Source }
        else {
            foreach ($path in @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),(Join-Path $env:LOCALAPPDATA 'HomeAssistants\tools\InnoSetup\ISCC.exe'))) {
                if (Test-Path $path) { $IsccPath = $path; break }
            }
        }
    }
    if (-not $IsccPath -or -not (Test-Path $IsccPath)) { throw 'Install Inno Setup 6 from jrsoftware.org and pass -IsccPath to ISCC.exe, or use -AppOnly.' }
    & $IsccPath ('/O'+$dist) (Join-Path $project 'installer.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    Get-FileHash (Join-Path $dist 'LocalAudioControl-Setup.exe')
}
