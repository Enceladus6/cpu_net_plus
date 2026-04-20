# Auto Login Toolkit (Windows)

This folder provides a standalone auto-login toolkit for campus eportal/drcom.

## Files

- `auto_login.ps1`: main login script
- `run_auto_login.cmd`: wrapper for Task Scheduler
- `autologin-config.example.json`: config template
- `install-task.ps1`: register/update scheduled task (every 5 minutes)

## Quick Start

1. Copy `autologin-config.example.json` to `autologin-config.json`.
2. Fill your username/password and profile endpoints.
3. Run in **Administrator PowerShell**:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-task.ps1
```

4. Verify:

```powershell
Get-ScheduledTaskInfo -TaskName AutoLogin_CampusEportal
Get-Content .\logs\autologin-$(Get-Date -Format yyyyMMdd).log -Tail 50
```

## Notes

- `preferred_profile` can force one profile, e.g. `lab-p` or `sushe`.
- If `preferred_profile` is empty, script auto-selects by IP prefix / endpoint probe.
- `maintenance_enabled=true` with `04:00-04:15` can skip noisy retries during planned outage windows.
