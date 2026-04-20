@echo off
setlocal

set "PS_SCRIPT=%~dp0auto_login.ps1"
set "PS_CFG=%~dp0autologin-config.json"

if not exist "%PS_CFG%" (
  echo Missing config: %PS_CFG%
  exit /b 2
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" -ConfigPath "%PS_CFG%"
exit /b %errorlevel%
