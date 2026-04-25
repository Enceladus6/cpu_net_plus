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
2. Double-click the tray icon or right-click `打开主界面`.
3. Open `设置`, fill `学号` / `密码`, then click `保存`.
4. Open `主页` and click `立即登录`.

The app stays in the tray and checks every 5 minutes.

## Config

`%APPDATA%\CampusAutoLogin\config.ini`

Default profiles:

- `sushe`: `172.17.253.3`
- `lab-p`: `192.168.199.21`, skips `04:00-04:15`

## Logs

`%APPDATA%\CampusAutoLogin\autologin.log`
