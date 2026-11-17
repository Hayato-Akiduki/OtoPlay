param([string]$Version)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    if (!$Version) { $Version = ([xml](Get-Content OtoPlayer/OtoPlayer.csproj -Raw)).Project.PropertyGroup.Version }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be x.y.z.' }
    function Run([string]$Command, [string[]]$Arguments) {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Command failed ($LASTEXITCODE)." }
    }
    $publish = "dist/publish-$Version"
    $releases = "dist/releases-$Version"
    $staging = "dist/package-$Version-$([guid]::NewGuid().ToString('N'))"
    Run dotnet @('publish', 'OtoPlayer/OtoPlayer.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', "-p:Version=$Version", '-o', $publish)
    Copy-Item README.md,THIRD-PARTY-NOTICES.txt $publish/
    $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $runtime = Get-ChildItem (Join-Path $packages 'microsoft.netcore.app.runtime.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    Copy-Item (Join-Path $runtime.FullName 'LICENSE.TXT') "$publish/DOTNET-LICENSE.txt"
    Copy-Item (Join-Path $runtime.FullName 'THIRD-PARTY-NOTICES.TXT') "$publish/DOTNET-THIRD-PARTY-NOTICES.txt"
    if (!(Test-Path .tools/vpk.exe)) { Run dotnet @('tool', 'install', 'vpk', '--tool-path', '.tools', '--version', '1.2.161') }
    Run ./.tools/vpk.exe @('pack', '--packId', 'HayatoAkiduki.OtoPlay', '--packVersion', $Version, '--packDir', $publish, '--mainExe', 'OtoPlayer.exe', '--packTitle', '音ならしますよっ！', '--packAuthors', 'Hayato-Akiduki', '--icon', 'OtoPlayer/Assets/app.ico', '--runtime', 'win-x64', '--outputDir', $staging, '--skip-updates')
    [IO.Directory]::CreateDirectory((Join-Path $root $releases)) | Out-Null
    Copy-Item "$staging/*" $releases/ -Force
    Run dotnet @('run', '--project', 'tests/OtoPlayer.Checks', '-c', 'Release', '--', $releases)
    Write-Output "Installer and update feed: $root/$releases"
} finally { Pop-Location }
