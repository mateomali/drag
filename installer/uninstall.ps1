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
    $args = '-NoProfile -ExecutionPolicy Bypass -File ' + (Quote-Arg $PSCommandPath)
    Start-Process -FilePath 'powershell.exe' -ArgumentList $args -Verb RunAs -Wait
    exit $LASTEXITCODE
}

$installDir = Join-Path $env:ProgramFiles 'File Sender'
$startMenuDir = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\File Sender'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'File Sender.lnk'

Get-Process | Where-Object { $_.Path -eq (Join-Path $installDir 'File Sender.exe') } | Stop-Process -Force -ErrorAction SilentlyContinue

& netsh advfirewall firewall delete rule name='File Sender' | Out-Null
& netsh advfirewall firewall delete rule name='File Sender TCP 50505' | Out-Null
& netsh advfirewall firewall delete rule name='File Sender UDP 50506' | Out-Null

Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\FileSender' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $installDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'File Sender desinstalado.'
