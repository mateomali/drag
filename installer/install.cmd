@echo off
setlocal

set "BOOTSTRAP_DIR=%TEMP%\FileSenderInstaller-%RANDOM%%RANDOM%"
mkdir "%BOOTSTRAP_DIR%" >nul 2>nul
xcopy /y /q "%~dp0*" "%BOOTSTRAP_DIR%\" >nul

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%BOOTSTRAP_DIR%\install.ps1" -SourceDir "%BOOTSTRAP_DIR%"
exit /b %ERRORLEVEL%
