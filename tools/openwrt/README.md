# OpenWrt Hyper-V 一键部署（Windows 11）

这个目录提供一个可直接复用的脚本：在 Hyper-V 上创建并启动 OpenWrt VM（x86_64 EFI 镜像）。

## 前提

- Windows 11 已启用 Hyper-V
- 已有 External vSwitch（比如 `Campus-External`）
- 管理员 PowerShell

## 快速运行

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\openwrt\deploy-openwrt-hyperv.ps1 `
  -VmName "OpenWrt-Lab" `
  -WanSwitch "Campus-External" `
  -MemoryMB 1024 `
  -BaseDir "D:\04Bot\openwrt"
```

## 脚本做了什么

1. 检查 Hyper-V 交换机是否存在且为 External。
2. 自动寻找 `qemu-img.exe`（含常见安装目录），必要时尝试 `winget` 安装 QEMU。
3. 下载 OpenWrt 24.10.0 EFI 镜像并校验 SHA256。
4. 使用 PowerShell/.NET 解压 `.img.gz`，不依赖 `gzip` 命令。
5. 转换为 `fixed` VHDX，并清理压缩/稀疏属性，避免 Hyper-V 挂盘报错。
6. 创建 Gen2 VM、关闭 Secure Boot、关闭自动检查点并启动。

## 默认目录结构（`-BaseDir`）

- `images/`：镜像与 VHDX
- `vm/`：VM 配置
- `logs/`：部署日志

## 常见问题

- 报 `qemu-img.exe not found`：确认 QEMU 安装在 `C:\Program Files\qemu\qemu-img.exe`，或手动把目录加入 PATH。
- 报 VHDX 稀疏/压缩限制：脚本已内置 `compact` + `fsutil sparse setflag 0` 处理，重新执行即可。
