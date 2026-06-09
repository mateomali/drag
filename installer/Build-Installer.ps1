$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $repoRoot 'installer\build'
$stagingDir = Join-Path $buildDir 'staging'
$outputDir = Join-Path $repoRoot 'dist'
$sedPath = Join-Path $buildDir 'FileSenderInstaller.sed'
$targetPath = Join-Path $outputDir 'FileSender-Setup.exe'
$msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'

if (-not (Test-Path $msbuild)) {
    $msbuild = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
}
if (-not (Test-Path $msbuild)) {
    throw 'No se encontro MSBuild de Visual Studio Build Tools.'
}

Remove-Item -LiteralPath $buildDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $targetPath -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stagingDir, $outputDir | Out-Null

& $msbuild (Join-Path $repoRoot 'FileSender.sln') /p:Configuration=Release /m
if ($LASTEXITCODE -ne 0) {
    throw "MSBuild fallo con codigo $LASTEXITCODE."
}

$releaseDir = Join-Path $repoRoot 'FileSender\bin\Release'
Copy-Item -LiteralPath (Join-Path $releaseDir 'File Sender.exe') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $releaseDir 'File Sender.exe.config') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $releaseDir 'Tools\croc-win7-x64.exe') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $releaseDir 'Tools\croc-win7-x86.exe') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'librerias\ndp472-runtime-offline.exe') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.cmd') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.cmd') -Destination $stagingDir -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $stagingDir -Force

$files = @(
    'install.cmd',
    'install.ps1',
    'uninstall.cmd',
    'uninstall.ps1',
    'File Sender.exe',
    'File Sender.exe.config',
    'croc-win7-x64.exe',
    'croc-win7-x86.exe',
    'ndp472-runtime-offline.exe',
    'README.md'
)

$fileStrings = for ($i = 0; $i -lt $files.Count; $i++) {
    'FILE{0}="{1}"' -f $i, $files[$i]
}
$sourceFiles = for ($i = 0; $i -lt $files.Count; $i++) {
    '%FILE{0}%=' -f $i
}

$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=File Sender fue instalado.
TargetName=$targetPath
FriendlyName=File Sender Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=install.cmd
UserQuietInstCmd=install.cmd
$($fileStrings -join "`r`n")
[SourceFiles]
SourceFiles0=$stagingDir
[SourceFiles0]
$($sourceFiles -join "`r`n")
"@

Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII

$iexpress = Start-Process -FilePath 'iexpress.exe' -ArgumentList @('/N', '/Q', $sedPath) -PassThru
$deadline = (Get-Date).AddMinutes(5)
$stagingBytes = (Get-ChildItem -LiteralPath $stagingDir -File | Measure-Object -Property Length -Sum).Sum
$minimumInstallerBytes = [int64]($stagingBytes * 0.50)
while ((Get-Date) -lt $deadline) {
    if ((Test-Path $targetPath) -and (Get-Item $targetPath).Length -ge $minimumInstallerBytes) {
        break
    }
    Start-Sleep -Seconds 2
}

if (-not (Test-Path $targetPath) -or (Get-Item $targetPath).Length -lt $minimumInstallerBytes) {
    if (-not $iexpress.HasExited) {
        Stop-Process -Id $iexpress.Id -Force -ErrorAction SilentlyContinue
    }
    throw "No se genero $targetPath."
}

Get-Item $targetPath | Select-Object FullName,Length,LastWriteTime
