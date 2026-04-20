#Requires -RunAsAdministrator

param(
    [string]$VmName = "OpenWrt-Lab",
    [string]$WanSwitch = "Campus-External",
    [int]$MemoryMB = 1024,
    [string]$BaseDir = "D:\04Bot\openwrt"
)

$ErrorActionPreference = "Stop"

$imgDir = Join-Path $BaseDir "images"
$vmDir = Join-Path $BaseDir "vm"
$logDir = Join-Path $BaseDir "logs"

New-Item -ItemType Directory -Force -Path $imgDir, $vmDir, $logDir | Out-Null

$logFile = Join-Path $logDir ("deploy-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
Start-Transcript -Path $logFile -Append | Out-Null

function Expand-GzipToFile {
    param(
        [Parameter(Mandatory = $true)][string]$SourceGz,
        [Parameter(Mandatory = $true)][string]$DestinationFile
    )

    if (-not (Test-Path $SourceGz)) {
        throw "Gzip source not found: $SourceGz"
    }

    if (Test-Path $DestinationFile) {
        Remove-Item -LiteralPath $DestinationFile -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $input = $null
    $gzip = $null
    $output = $null
    try {
        $input = [System.IO.File]::OpenRead($SourceGz)
        $gzip = New-Object System.IO.Compression.GZipStream($input, [System.IO.Compression.CompressionMode]::Decompress)
        $output = [System.IO.File]::Create($DestinationFile)
        $buffer = New-Object byte[] 1048576
        while (($read = $gzip.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $output.Write($buffer, 0, $read)
        }
    }
    finally {
        if ($output) { $output.Dispose() }
        if ($gzip) { $gzip.Dispose() }
        if ($input) { $input.Dispose() }
    }
}

try {
    Write-Host "[1/7] Checking Hyper-V switch..."
    $switch = Get-VMSwitch -Name $WanSwitch -ErrorAction Stop
    if ($switch.SwitchType -ne "External") {
        throw "Switch '$WanSwitch' is not External."
    }

    Write-Host "[2/7] Ensuring qemu-img is available..."
    $qemuImg = (Get-Command qemu-img.exe -ErrorAction SilentlyContinue)
    if (-not $qemuImg) {
        $candidatePaths = @(
            "C:\Program Files\qemu\qemu-img.exe",
            "C:\Program Files (x86)\qemu\qemu-img.exe"
        )
        foreach ($candidate in $candidatePaths) {
            if (Test-Path $candidate) {
                $qemuImg = [pscustomobject]@{ Source = $candidate }
                break
            }
        }
    }
    if (-not $qemuImg) {
        Write-Host "qemu-img not found. Installing QEMU via winget..."
        winget install --id SoftwareFreedomConservancy.QEMU --accept-package-agreements --accept-source-agreements --silent --scope machine
        $qemuImg = (Get-Command qemu-img.exe -ErrorAction SilentlyContinue)
        if (-not $qemuImg) {
            $candidate = "C:\Program Files\qemu\qemu-img.exe"
            if (Test-Path $candidate) {
                $qemuImg = [pscustomobject]@{ Source = $candidate }
            }
        }
    }
    if (-not $qemuImg) {
        throw "qemu-img.exe not found after installation. Please install QEMU manually, then rerun this script."
    }

    Write-Host "[3/7] Downloading OpenWrt image..."
    $imgGz = Join-Path $imgDir "openwrt-24.10.0-x86-64-generic-ext4-combined-efi.img.gz"
    $imgRaw = Join-Path $imgDir "openwrt-24.10.0-x86-64-generic-ext4-combined-efi.img"
    $imgVhdx = Join-Path $imgDir "openwrt-24.10.0-x86-64-generic-ext4-combined-efi.vhdx"
    $url = "https://mirrors.tuna.tsinghua.edu.cn/openwrt/releases/24.10.0/targets/x86/64/openwrt-24.10.0-x86-64-generic-ext4-combined-efi.img.gz"

    if (-not (Test-Path $imgGz)) {
        curl.exe -L --fail --retry 5 --retry-delay 2 -o $imgGz $url
    }

    Write-Host "[4/7] Verifying SHA256..."
    $actual = (Get-FileHash -Algorithm SHA256 $imgGz).Hash.ToLower()
    $expected = "b0f3ba38bd9d6274fcf7868c02704eaa2c2caee62629b22828f6543dc27d6092"
    if ($actual -ne $expected) {
        throw "SHA256 mismatch. expected=$expected actual=$actual"
    }

    if (Get-VM -Name $VmName -ErrorAction SilentlyContinue) {
        Write-Host "Existing VM '$VmName' found. Removing it before disk conversion..."
        Stop-VM -Name $VmName -TurnOff -Force -ErrorAction SilentlyContinue
        Remove-VM -Name $VmName -Force
    }

    Write-Host "[5/7] Converting img to vhdx..."
    if (-not (Test-Path $imgRaw)) {
        Write-Host "Decompressing image..."
        Expand-GzipToFile -SourceGz $imgGz -DestinationFile $imgRaw
    }

    if (Test-Path $imgVhdx) {
        Remove-Item -LiteralPath $imgVhdx -Force
    }
    & $qemuImg.Source convert -f raw -O vhdx -o subformat=fixed $imgRaw $imgVhdx
    if ($LASTEXITCODE -ne 0) {
        throw "qemu-img convert failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path $imgVhdx)) {
        throw "VHDX conversion failed."
    }
    & compact.exe /U /I $imgVhdx | Out-Null
    & fsutil.exe sparse setflag $imgVhdx 0 | Out-Null

    Write-Host "[6/7] Creating VM..."
    $memoryBytes = [int64]$MemoryMB * 1MB
    New-VM -Name $VmName -Generation 2 -MemoryStartupBytes $memoryBytes -VHDPath $imgVhdx -SwitchName $WanSwitch -Path $vmDir | Out-Null
    Set-VMFirmware -VMName $VmName -EnableSecureBoot Off
    Set-VM -Name $VmName -AutomaticCheckpointsEnabled $false
    Set-VMMemory -VMName $VmName -DynamicMemoryEnabled $false
    Set-VMProcessor -VMName $VmName -Count 2
    Start-VM -Name $VmName | Out-Null

    Write-Host "[7/7] Done."
    Write-Host "VM Name: $VmName"
    Write-Host "WAN Switch: $WanSwitch"
    Write-Host "Next: Open VM console and run initial OpenWrt setup."
}
finally {
    Stop-Transcript | Out-Null
    Write-Host "Log saved to: $logFile"
}
