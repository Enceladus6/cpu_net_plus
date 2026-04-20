#Requires -RunAsAdministrator

param(
    [string]$TaskName = "AutoLogin_CampusEportal"
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$runner = Join-Path $scriptDir "run_auto_login.cmd"
$cfg = Join-Path $scriptDir "autologin-config.json"

if (-not (Test-Path $runner)) {
    throw "Missing runner: $runner"
}

if (-not (Test-Path $cfg)) {
    throw "Missing config: $cfg (copy from autologin-config.example.json first)"
}

schtasks /create /tn $TaskName /tr $runner /sc minute /mo 5 /f | Out-Null
Start-ScheduledTask -TaskName $TaskName

Write-Host "Task installed: $TaskName"
Get-ScheduledTaskInfo -TaskName $TaskName | Select-Object LastRunTime,LastTaskResult,NextRunTime | Format-Table -AutoSize
