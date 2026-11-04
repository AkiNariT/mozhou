param(
    [Parameter(Mandatory)][string]$PayloadDir,
    [Parameter(Mandatory)][string]$OutputDir,
    [Parameter(Mandatory)][string]$IsccPath,
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.8.0',
    [switch]$TestNamespace
)

$ErrorActionPreference = 'Stop'
$payload = (Get-Item -LiteralPath $PayloadDir).FullName
$compiler = (Get-Item -LiteralPath $IsccPath).FullName
$destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDir)
if ($destination -eq $payload -or
    $destination.StartsWith($payload.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must be outside the payload directory.'
}
foreach ($required in 'Mozhou.exe','Mozhou.dll','Mozhou.deps.json','Mozhou.runtimeconfig.json',
        'coreclr.dll','hostfxr.dll','hostpolicy.dll','PresentationFramework.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required) -PathType Leaf)) {
        throw "Self-contained Windows desktop payload is missing $required"
    }
}

# Only the desktop publish layout and named runtime notices may enter the installer.
$culture = '[a-z]{2,3}(?:-[a-z0-9]{2,8})*'
$rootFiles = '^(?:[^\\]+\.dll|Mozhou\.exe|createdump\.exe|Mozhou\.(?:deps|runtimeconfig)\.json|(?:DOTNET|WINDOWSDESKTOP)-(?:LICENSE|THIRD-PARTY-NOTICES)\.txt)$'
foreach ($item in Get-ChildItem -LiteralPath $payload -Recurse -Force) {
    $relative = $item.FullName.Substring($payload.TrimEnd('\').Length + 1)
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Unexpected link: $relative" }
    if ($item.PSIsContainer) {
        if ($relative -match "^$culture$") { continue }
    } elseif ($relative -match $rootFiles -or $relative -match "^$culture\\[^\\]+\.resources\.dll$") {
        continue
    }
    throw "Unexpected item in the release payload: $relative"
}

[IO.Directory]::CreateDirectory($destination) | Out-Null
$arguments = @('/Q', "/O$destination", "/DAppVersion=$Version", "/DPayloadDir=$payload")
if ($TestNamespace) { $arguments += '/DTestNamespace=1' }
& $compiler @arguments (Join-Path $PSScriptRoot 'Mozhou.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }
$name = if ($TestNamespace) { 'MozhouInstallerTest' } else { 'Mozhou' }
Get-Item -LiteralPath (Join-Path $destination "$name-Setup-$Version-x64.exe")
