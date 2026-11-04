# Run only with installers compiled using -TestNamespace.
param(
    [Parameter(Mandatory)][string]$PreviousSetup,
    [Parameter(Mandatory)][string]$CurrentSetup,
    [Parameter(Mandatory)][string]$NovelFixture,
    [Parameter(Mandatory)][string]$WorkDir,
    [string]$DowngradeProbe
)
$ErrorActionPreference = 'Stop'
function Check($ok, [string]$message) {
    if (!$ok) { throw $message }
    Write-Output "PASS $message"
}
function Run-Setup([string]$file, [string[]]$options) {
    # Both Inno Setup and its uninstaller can launch a second-phase executable.
    $process = Start-Process -FilePath $file -ArgumentList $options -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installer exited with $($process.ExitCode). See QA log in $run." }
}
function Read-Default([string]$path) {
    $key = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if ($key) { return $key.GetValue('') }
}
$previous = Get-Item -LiteralPath $PreviousSetup
$current = Get-Item -LiteralPath $CurrentSetup
foreach ($exe in $previous, $current) {
    if ($exe.Name -notmatch '^MozhouInstallerTest-Setup-\d+\.\d+\.\d+-x64\.exe$') { throw 'This test requires an isolated QA installer.' }
}
$reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MozhouInstallerTest.Desktop_is1'
$product = 'HKCU:\Software\MozhouInstallerTest'
$extension = 'HKCU:\Software\Classes\.mozhou-installertest'
$progId = 'HKCU:\Software\Classes\MozhouInstallerTest.Document'
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'MozhouInstallerTest.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'MozhouInstallerTest.lnk')
)
foreach ($path in @($reg,$product,$extension,$progId) + $shortcuts) {
    if (Test-Path -LiteralPath $path) { throw "An existing QA entry must be inspected first: $path" }
}
$run = Join-Path ([IO.Path]::GetFullPath($WorkDir)) ('upgrade-' + [guid]::NewGuid().ToString('N'))
$first = [IO.Directory]::CreateDirectory((Join-Path $run 'previous setup')).FullName
$appDir = Join-Path $run '程序 App'
$legacy = [IO.Directory]::CreateDirectory((Join-Path $first 'Mozhou-0.7.0')).FullName
$profile = Join-Path $run 'local data'
$data = [IO.Directory]::CreateDirectory((Join-Path $profile 'Mozhou/UserData')).FullName
$fixture = Get-Item -LiteralPath $NovelFixture
$book = Join-Path $run '我的测试手稿 带空格.mozhou-installertest'
Copy-Item -LiteralPath $fixture.FullName -Destination $book
$bookHash = (Get-FileHash -LiteralPath $book).Hash
$settings = Join-Path $data 'mozhou-settings.json'
$shelf = Join-Path $data 'bookshelf.json'
$initialSettings = '{"Model":"fixture-model","LastProjectPath":null}'
[IO.File]::WriteAllText($settings,$initialSettings)
[IO.File]::WriteAllText($shelf,'[]')
[IO.File]::WriteAllBytes((Join-Path $legacy 'Mozhou.exe'),[byte[]]@())
[IO.File]::WriteAllText((Join-Path $legacy 'mozhou-settings.json'),$initialSettings)
$firstExe = Join-Path $first $previous.Name
Copy-Item -LiteralPath $previous.FullName -Destination $firstExe
$silent = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
$appExe = Join-Path $appDir 'Mozhou.exe'
$inside = Join-Path $appDir '我另外存放的小说.mozhou'
$uninstall = Join-Path $appDir 'unins000.exe'
$process = $null
try {
    Run-Setup $firstExe ($silent + @("/DIR=`"$appDir`"", "/LOG=`"$(Join-Path $run 'install.log')`""))
    Check ((Test-Path -LiteralPath $appExe) -and (Test-Path -LiteralPath (Join-Path $appDir 'mozhou-installed.marker'))) 'First install created application and installed-mode marker'
    Check ((Get-ItemPropertyValue $product 'LegacyPortablePath') -eq $legacy) 'Installer found the existing portable profile automatically'
    $beforeVersion = Get-ItemPropertyValue $reg 'DisplayVersion'
    $shell = New-Object -ComObject WScript.Shell
    foreach ($shortcut in $shortcuts) {
        Check ((Test-Path -LiteralPath $shortcut) -and $shell.CreateShortcut($shortcut).TargetPath -eq $appExe) 'Shortcut targets the installed executable'
    }
    Check ((Read-Default $extension) -eq 'MozhouInstallerTest.Document') 'New extension has the expected default'
    $open = Read-Default "$progId\shell\open\command"
    Check ($open -eq ('"' + $appExe + '" "%1"')) 'Shell open quotes the executable and document paths separately'
    Add-Type -Namespace MozhouTest -Name Shell -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern int AssocQueryString(int flags, int kind, string extension, string verb, System.Text.StringBuilder output, ref uint length);
'@
    Copy-Item -LiteralPath $fixture.FullName -Destination $inside
    Run-Setup $current.FullName ($silent + @("/LOG=`"$(Join-Path $run 'upgrade.log')`""))
    $afterVersion = Get-ItemPropertyValue $reg 'DisplayVersion'
    Check (([version]$afterVersion -gt [version]$beforeVersion) -and (Test-Path -LiteralPath $appExe)) 'Upgrade replaced the prior version in its registered directory'
    if ($DowngradeProbe) {
        $guard = Get-Item -LiteralPath $DowngradeProbe
        if ($guard.Name -ne 'GuardProbe-0.7.98-test-only.exe' -or $guard.VersionInfo.ProductName.Trim() -ne 'MozhouInstallerTest') { throw 'Expected a test-namespace guard probe.' }
        $older = Start-Process -FilePath $guard.FullName -ArgumentList $silent -WindowStyle Hidden -PassThru
        if (!$older.WaitForExit(30000)) { throw 'Downgrade probe did not exit.' }
        Check ($older.ExitCode -ne 0 -and (Get-ItemPropertyValue $reg 'DisplayVersion') -eq $afterVersion) 'Older installer cannot replace the newer installed release'
    }
    Check ((Get-Content $settings -Raw) -eq $initialSettings -and (Get-Content $shelf -Raw) -eq '[]') 'Upgrade kept existing bookshelf and preferences'

    $resolved = [Text.StringBuilder]::new(1024)
    [uint32]$length = $resolved.Capacity
    $hr = [MozhouTest.Shell]::AssocQueryString(0,2,'.mozhou-installertest','open',$resolved,[ref]$length)
    Check ($hr -eq 0 -and $resolved.ToString() -eq $appExe) 'Windows Shell resolves the novel to the installed editor'
    # Use the Shell's default verb, as when opening the file from Explorer.
    $start = [Diagnostics.ProcessStartInfo]::new($book)
    $start.UseShellExecute = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $isolated = @{ LOCALAPPDATA=$profile; DOTNET_ROOT=(Join-Path $run 'absent-system-runtime'); DOTNET_ROOT_X64=(Join-Path $run 'absent-system-runtime'); DOTNET_MULTILEVEL_LOOKUP='0' }
    $savedEnv = @{}
    try {
        foreach ($name in $isolated.Keys) { $savedEnv[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name,$isolated[$name]) }
        $launched = [Diagnostics.Process]::Start($start)
        if (!$launched -or $launched.MainModule.FileName -ne $appExe) { throw 'The Shell did not start the expected test editor.' }
        $process = $launched
    } finally { foreach ($name in $savedEnv.Keys) { [Environment]::SetEnvironmentVariable($name,$savedEnv[$name]) } }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($process.HasExited) { throw 'Installed app exited before opening the document.' }
        Start-Sleep -Milliseconds 200
        $prefs = Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json
        $books = @(Get-Content -LiteralPath $shelf -Raw | ConvertFrom-Json)
        $opened = $prefs.LastProjectPath -eq $book -and ($books.Folder -contains $book)
    } until ($opened -or $timer.Elapsed.TotalSeconds -gt 35)
    Check ($opened -and $prefs.Model -eq 'fixture-model') 'Registered document opens and updates only the isolated profile'
    $clr = @($process.Modules | Where-Object { $_.ModuleName -eq 'coreclr.dll' })
    Check ($clr.Count -eq 1 -and $clr[0].FileName -eq (Join-Path $appDir 'coreclr.dll')) 'Running editor loaded the CLR bundled in its install directory'
    $null = $process.CloseMainWindow()
    if (!$process.WaitForExit(5000)) { $process.Kill(); $process.WaitForExit() }
    $savedShelf = (Get-FileHash -LiteralPath $shelf).Hash
    $savedPrefs = (Get-FileHash -LiteralPath $settings).Hash
    Check (Test-Path -LiteralPath $uninstall) 'Registered uninstall program exists'
    # Simulate another program becoming the selected handler AFTER Mozhou was installed.
    Set-Item -LiteralPath $extension -Value 'MozhouInstallerTest.OtherHandler'
    Run-Setup $uninstall ($silent + @("/LOG=`"$(Join-Path $run 'uninstall.log')`""))
    Check (!(Test-Path -LiteralPath $appExe) -and !(Test-Path -LiteralPath $reg)) 'Uninstall removed the installed executable and application entry'
    Check (!(Test-Path -LiteralPath $progId) -and !(Test-Path -LiteralPath $shortcuts[0]) -and !(Test-Path -LiteralPath $shortcuts[1])) 'Uninstall removed its own handler and both shortcuts'
    Check ((Read-Default $extension) -eq 'MozhouInstallerTest.OtherHandler') 'Uninstall respected a subsequently selected handler'
    Check ((Get-FileHash -LiteralPath $inside).Hash -eq $bookHash -and (Get-FileHash -LiteralPath $book).Hash -eq $bookHash) 'Uninstall kept novels both inside and outside the application directory byte-for-byte'
    Check ((Get-FileHash -LiteralPath $shelf).Hash -eq $savedShelf -and (Get-FileHash -LiteralPath $settings).Hash -eq $savedPrefs) 'Uninstall kept bookshelf and settings byte-for-byte'
    $profileKey = Get-Item -LiteralPath $product -ErrorAction SilentlyContinue
    $hasHint = $profileKey -and $profileKey.GetValueNames() -contains 'LegacyPortablePath'
    if ($hasHint) { Write-Output ("Remaining migration hint: " + $profileKey.GetValue('LegacyPortablePath')) }
    Check (!$hasHint) 'Uninstall cleared the obsolete one-time import hint'

    $again = [IO.Directory]::CreateDirectory((Join-Path $run 'library-only setup')).FullName
    $oldLibrary = Join-Path $again 'Mozhou-0.7.0'
    $library = [IO.Directory]::CreateDirectory((Join-Path $oldLibrary 'Library')).FullName
    Copy-Item -LiteralPath $fixture.FullName -Destination (Join-Path $library '旧稿.mozhou')
    [IO.File]::WriteAllBytes((Join-Path $oldLibrary 'Mozhou.exe'),[byte[]]@())
    $againExe = Join-Path $again $current.Name
    Copy-Item -LiteralPath $current.FullName -Destination $againExe
    Run-Setup $againExe ($silent + @("/DIR=`"$appDir`""))
    Check ((Get-ItemPropertyValue $product 'LegacyPortablePath') -eq $oldLibrary) 'First installation detects a legacy directory containing only Library novels'
    Check ((Read-Default $extension) -eq 'MozhouInstallerTest.OtherHandler') 'Reinstallation respects the previous default handler'
    Run-Setup $uninstall $silent
    Check ((Get-FileHash -LiteralPath (Join-Path $library '旧稿.mozhou')).Hash -eq $bookHash) 'Reinstallation and uninstall preserve the legacy Library manuscript'
}
finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
    if (Test-Path -LiteralPath $uninstall) { Run-Setup $uninstall $silent }
    # These isolated keys did not exist before the test. Remove only the known QA values.
    if ((Read-Default $extension) -eq 'MozhouInstallerTest.OtherHandler') {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\.mozhou-installertest',$true)
        try { $key.DeleteValue('', $false) } finally { $key.Dispose() }
    }
    if (Test-Path -LiteralPath $product) { Remove-ItemProperty -LiteralPath $product -Name 'LegacyPortablePath' -ErrorAction SilentlyContinue }
    foreach ($path in $extension,$product) {
        $key = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($key -and $key.SubKeyCount -eq 0 -and $key.ValueCount -eq 0) { Remove-Item -LiteralPath $path }
    }
}
