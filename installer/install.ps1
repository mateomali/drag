param(
    [string]$SourceDir = $PSScriptRoot,
    [switch]$Elevated
)

$ErrorActionPreference = 'Stop'

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Quote-Arg([string]$Value) {
    return '"' + ($Value -replace '"', '\"') + '"'
}

if (-not (Test-IsAdmin)) {
    $args = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', (Quote-Arg $PSCommandPath),
        '-SourceDir', (Quote-Arg $SourceDir),
        '-Elevated'
    ) -join ' '
    Start-Process -FilePath 'powershell.exe' -ArgumentList $args -Verb RunAs -Wait
    exit $LASTEXITCODE
}

$installDir = Join-Path $env:ProgramFiles 'File Sender'
$toolsDir = Join-Path $installDir 'Tools'
$startMenuDir = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\File Sender'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'File Sender.lnk'
$appExe = Join-Path $installDir 'File Sender.exe'

Write-Host 'Instalando File Sender...'

$release = 0
$netKey = 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
if (Test-Path $netKey) {
    $release = (Get-ItemProperty $netKey -Name Release -ErrorAction SilentlyContinue).Release
}

if ($release -lt 461808) {
    $runtime = Join-Path $SourceDir 'ndp472-runtime-offline.exe'
    if (-not (Test-Path $runtime)) {
        throw 'Falta ndp472-runtime-offline.exe en el instalador.'
    }
    Write-Host 'Instalando .NET Framework 4.7.2 Runtime...'
    $process = Start-Process -FilePath $runtime -ArgumentList '/q /norestart' -Wait -PassThru
    if ($process.ExitCode -ne 0 -and $process.ExitCode -ne 3010) {
        throw ".NET Framework installer fallo con codigo $($process.ExitCode)."
    }
    if ($process.ExitCode -eq 3010) {
        Write-Host 'El runtime solicito reinicio. La app quedara instalada, pero puede requerir reiniciar Windows.'
    }
}

New-Item -ItemType Directory -Force -Path $installDir, $toolsDir, $startMenuDir | Out-Null

Copy-Item -LiteralPath (Join-Path $SourceDir 'File Sender.exe') -Destination $appExe -Force
Copy-Item -LiteralPath (Join-Path $SourceDir 'File Sender.exe.config') -Destination (Join-Path $installDir 'File Sender.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $SourceDir 'README.md') -Destination (Join-Path $installDir 'README.md') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $SourceDir 'croc-win7-x64.exe') -Destination (Join-Path $toolsDir 'croc-win7-x64.exe') -Force
Copy-Item -LiteralPath (Join-Path $SourceDir 'croc-win7-x86.exe') -Destination (Join-Path $toolsDir 'croc-win7-x86.exe') -Force
Copy-Item -LiteralPath (Join-Path $SourceDir 'uninstall.ps1') -Destination (Join-Path $installDir 'uninstall.ps1') -Force
Copy-Item -LiteralPath (Join-Path $SourceDir 'uninstall.cmd') -Destination (Join-Path $installDir 'uninstall.cmd') -Force

$shell = New-Object -ComObject WScript.Shell
foreach ($shortcutPath in @((Join-Path $startMenuDir 'File Sender.lnk'), $desktopShortcut)) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $appExe
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = $appExe
    $shortcut.Save()
}

& netsh advfirewall firewall delete rule name='File Sender' | Out-Null
& netsh advfirewall firewall add rule name='File Sender' dir=in action=allow program="$appExe" enable=yes profile=private | Out-Null
& netsh advfirewall firewall delete rule name='File Sender TCP 50505' | Out-Null
& netsh advfirewall firewall add rule name='File Sender TCP 50505' dir=in action=allow protocol=TCP localport=50505 profile=private | Out-Null
& netsh advfirewall firewall delete rule name='File Sender UDP 50506' | Out-Null
& netsh advfirewall firewall add rule name='File Sender UDP 50506' dir=in action=allow protocol=UDP localport=50506 profile=private | Out-Null

$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FileSender'
New-Item -Path $uninstallKey -Force | Out-Null
Set-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'File Sender'
Set-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '1.0.0'
Set-ItemProperty -Path $uninstallKey -Name Publisher -Value 'File Sender'
Set-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installDir
Set-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value $appExe
Set-ItemProperty -Path $uninstallKey -Name UninstallString -Value ('"' + (Join-Path $installDir 'uninstall.cmd') + '"')
Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord

Write-Host 'File Sender instalado correctamente.'
Write-Host "Ruta: $installDir"

if ($Elevated -and $SourceDir -like (Join-Path $env:TEMP 'FileSenderInstaller-*')) {
    $cleanup = 'ping 127.0.0.1 -n 3 >nul & rd /s /q "' + $SourceDir + '"'
    Start-Process -FilePath 'cmd.exe' -ArgumentList '/c', $cleanup -WindowStyle Hidden
}
