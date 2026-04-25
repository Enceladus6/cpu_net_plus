# CampusAutoLogin.exe

This is a zero-install Windows tray agent for campus network login.

## Build

No .NET SDK is required. It uses the built-in .NET Framework C# compiler on Windows.

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\autologin-agent\build.ps1
```

Output:

`artifacts\CampusAutoLogin\CampusAutoLogin.exe`

## Use

1. Run `CampusAutoLogin.exe`.
2. Right-click the tray icon.
3. Open config and fill `username` / `password`.
4. Click `立即登录`.

The app stays in the tray and checks every 5 minutes.

## Config

`%APPDATA%\CampusAutoLogin\config.ini`

Default profiles:

- `sushe`: `172.17.253.3`
- `lab-p`: `192.168.199.21`, skips `04:00-04:15`

## Logs

`%APPDATA%\CampusAutoLogin\autologin.log`
