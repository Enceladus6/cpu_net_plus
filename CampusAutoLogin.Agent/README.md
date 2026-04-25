# Campus AutoLogin Agent

Windows tray background agent for campus network login.

## Usage

1. Run `CampusAutoLogin.exe`.
2. Right-click the tray icon and choose `打开配置`.
3. Fill `username` and `password`.
4. Right-click the tray icon and choose `立即登录`.

The app keeps running in the tray and checks every 5 minutes.

## Config Location

`%APPDATA%\CampusAutoLogin\config.json`

## Logs

`%APPDATA%\CampusAutoLogin\autologin.log`

## Build Single EXE

```powershell
powershell -ExecutionPolicy Bypass -File .\CampusAutoLogin.Agent\publish-single-exe.ps1
```

Output:

`.\artifacts\CampusAutoLogin\CampusAutoLogin.exe`
