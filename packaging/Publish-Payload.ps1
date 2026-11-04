param(
    [Parameter(Mandatory)][string]$PayloadDir,
    [Parameter(Mandatory)][string]$WorkDir,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot -Parent) 'Mozhou.Desktop/Mozhou.Desktop.csproj'
$destination = [IO.Path]::GetFullPath($PayloadDir)
$workspace = [IO.Path]::GetFullPath($WorkDir)
if ((Test-Path -LiteralPath $destination) -and (Get-ChildItem -LiteralPath $destination -Force | Select-Object -First 1)) {
    throw 'Choose a new or empty payload directory. User files must never enter an installer.'
}
$runtime = '8.0.33'
$buildOptions = @(
    ('-p:ArtifactsPath=' + (Join-Path $workspace 'build')),
    '-p:DefaultItemExcludesInProjectFolder=bin/**%3Bobj/**',
    ('-p:RuntimeFrameworkVersion=' + $runtime), '-p:SelfContained=true',
    '-p:UseSharedCompilation=false', '-m:1', '/nodeReuse:false'
)
if (!$NoRestore) {
    & dotnet restore $project --runtime win-x64 --source https://api.nuget.org/v3/index.json --packages (Join-Path $workspace 'packages') --disable-parallel @buildOptions
    if ($LASTEXITCODE -ne 0) { throw 'Windows runtime pack restore failed.' }
}
& dotnet publish $project -c Release --runtime win-x64 --no-restore --self-contained true --output $destination @buildOptions -p:CopyOutputSymbolsToPublishDirectory=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
foreach ($name in @('Mozhou.exe','Mozhou.dll','hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll','PresentationFramework.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $destination $name) -PathType Leaf)) { throw "Missing required published file $name" }
}
$config = Get-Content -LiteralPath (Join-Path $destination 'Mozhou.runtimeconfig.json') -Raw | ConvertFrom-Json
$desktop = @($config.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' -and $_.version -eq $runtime })
if ($desktop.Count -ne 1 -or $config.runtimeOptions.frameworks) { throw 'Unexpected desktop runtime configuration.' }
$packages = Join-Path $workspace 'packages'
$corePack = Join-Path $packages "microsoft.netcore.app.runtime.win-x64/$runtime"
$desktopPack = Join-Path $packages "microsoft.windowsdesktop.app.runtime.win-x64/$runtime"
Copy-Item -LiteralPath (Join-Path $corePack 'LICENSE.TXT') -Destination (Join-Path $destination 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $corePack 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $destination 'DOTNET-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $desktopPack 'LICENSE') -Destination (Join-Path $destination 'WINDOWSDESKTOP-LICENSE.txt')
