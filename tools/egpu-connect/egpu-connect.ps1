<#
.SYNOPSIS
  Help the ASUS XG Mobile GC34X eGPU connect on MSI Prestige 13 AI+ Evo.

.DESCRIPTION
  1. Disable "Remote Desktop Device Redirector Bus" (one-time, persistent).
     Undo: Enable-PnpDevice -InstanceId 'ROOT\RDPBUS\0000'
  2. Temporarily stop MSI Center S and audio services that veto PCI resource rebalance.
  3. Guide an eGPU replug and wait until the GPU works.
  4. Restore the stopped services and MSI apps.
  Analysis: .tmp/egpu-intermittent-detection-report.md

.PARAMETER DryRun
  Show current state and planned actions only. Changes nothing, needs no admin.
#>
param([switch]$DryRun)

$ErrorActionPreference = 'Stop'

# WQL LIKE patterns: GC34X USB4 router and RTX 5090 Laptop GPU
$RouterIdPattern = 'USB4\\VID_8087&PID_5786%'
$GpuIdPattern = 'PCI\\VEN_10DE&DEV_2C18%'
$RdpBusId = 'ROOT\RDPBUS\0000'

# Stopped in this order, restored in reverse.
# AudioEndpointBuilder -Force also stops Audiosrv and other running dependents.
$VetoServices = @(
    'MSI_Center_S_Service', 'MSI Foundation Service',
    'RtkAudioUniversalService', 'IntelAudioService', 'IgoAudioService',
    'AudioEndpointBuilder'
)
$MsiProcessPattern = '^(MSI_Center|MSI_OLED|MSI_S_LAN|MSIAPService)'

$UnplugTimeoutSec = 120
$ConnectTimeoutSec = 180
# A root-level rebalance often tears the GPU down within seconds, so require it to stay OK.
$StableSec = 15

function Get-EgpuState {
    $devices = @(Get-CimInstance Win32_PnPEntity -Filter "DeviceID LIKE '$RouterIdPattern' OR DeviceID LIKE '$GpuIdPattern'")
    $gpu = $devices | Where-Object { $_.DeviceID -like 'PCI\*' } | Select-Object -First 1
    [pscustomobject]@{
        HasRouter = [bool]($devices | Where-Object { $_.DeviceID -like 'USB4\*' })
        HasGpu    = [bool]$gpu
        GpuCode   = if ($gpu) { [int]$gpu.ConfigManagerErrorCode } else { $null }
    }
}

function Format-EgpuState($state) {
    if (-not $state.HasRouter -and -not $state.HasGpu) { return '未連接' }
    if (-not $state.HasGpu) { return 'USB4 Router 已出現，GPU 尚未出現' }
    if ($state.GpuCode -eq 0) { return 'GPU 正常' }
    "GPU 異常（Code $($state.GpuCode)）"
}

function Get-RunningVetoServices {
    @($VetoServices | Where-Object { (Get-Service -Name $_ -ErrorAction SilentlyContinue).Status -eq 'Running' })
}

function Stop-VetoSources {
    # User-session MSI apps are not restarted by their services, so remember them first.
    $userApps = @(Get-Process | Where-Object { $_.ProcessName -match $MsiProcessPattern -and $_.SessionId -ne 0 -and $_.Path } |
        ForEach-Object Path | Sort-Object -Unique)

    $stopped = [System.Collections.Generic.List[string]]::new()
    foreach ($name in Get-RunningVetoServices) {
        $svc = Get-Service -Name $name
        if ($svc.Status -ne 'Running') { continue }  # already stopped as a dependent
        foreach ($dep in $svc.DependentServices | Where-Object Status -eq 'Running') {
            if (-not $stopped.Contains($dep.Name)) { $stopped.Add($dep.Name) }
        }
        Write-Host "  停止服務 $name"
        Stop-Service -Name $name -Force
        $stopped.Add($name)
    }

    foreach ($proc in Get-Process | Where-Object { $_.ProcessName -match $MsiProcessPattern }) {
        Write-Host "  結束程式 $($proc.ProcessName)"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }

    [pscustomobject]@{ Services = $stopped; UserApps = $userApps }
}

function Restore-VetoSources($saved) {
    for ($i = $saved.Services.Count - 1; $i -ge 0; $i--) {
        $name = $saved.Services[$i]
        try {
            Start-Service -Name $name
            Write-Host "  啟動服務 $name"
        } catch {
            Write-Warning "無法啟動服務 ${name}：$($_.Exception.Message)"
        }
    }
    Start-Sleep -Seconds 5  # services may relaunch some apps themselves
    foreach ($path in $saved.UserApps) {
        if (Get-Process | Where-Object { $_.ProcessName -match $MsiProcessPattern -and $_.Path -eq $path }) { continue }
        Write-Host "  重新開啟 $path"
        # explorer.exe starts the app unelevated in the user session
        Start-Process explorer.exe -ArgumentList "`"$path`""
    }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)

if ($DryRun) {
    $rdp = Get-PnpDevice -InstanceId $RdpBusId -ErrorAction SilentlyContinue
    $rdpText = if (-not $rdp) { '找不到' } elseif ($rdp.ConfigManagerErrorCode -eq 22) { '已停用' } else { '啟用中（會被停用）' }
    $procNames = @(Get-Process | Where-Object { $_.ProcessName -match $MsiProcessPattern } | ForEach-Object ProcessName | Sort-Object -Unique)
    Write-Host '== DryRun：只顯示狀態，不做任何變更 =='
    Write-Host "系統管理員權限：$isAdmin"
    Write-Host "Remote Desktop Device Redirector Bus：$rdpText"
    Write-Host "會暫停的服務：$((Get-RunningVetoServices) -join ', ')"
    Write-Host "會結束的 MSI 程式：$($procNames -join ', ')"
    Write-Host "eGPU：$(Format-EgpuState (Get-EgpuState))"
    exit 0
}

if (-not $isAdmin) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    exit 0
}

$logDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
Start-Transcript -Path (Join-Path $logDir ('egpu-connect-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))) | Out-Null

$isReady = $false
$errorText = $null
$saved = $null
$state = Get-EgpuState
try {
    Write-Host '[1/4] 檢查 Remote Desktop Device Redirector Bus'
    $rdp = Get-PnpDevice -InstanceId $RdpBusId -ErrorAction SilentlyContinue
    if (-not $rdp) {
        Write-Host '  找不到此裝置，略過'
    } elseif ($rdp.ConfigManagerErrorCode -eq 22) {
        Write-Host '  已是停用狀態'
    } else {
        try {
            Disable-PnpDevice -InstanceId $RdpBusId -Confirm:$false -ErrorAction Stop
            Write-Host '  已停用（永久設定；遠端桌面的裝置重導功能將無法使用）'
        } catch {
            # The rdp driver vetoes a live stop; Windows applies the disable after reboot.
            Write-Warning "無法立即停用：$($_.Exception.Message)"
            Write-Warning 'RDP 驅動程式不放手，通常要重開機後才生效；這次先略過，繼續執行。'
        }
    }

    Write-Host '[2/4] 暫停會否決資源重新分配的服務（約 1~3 分鐘沒有聲音）'
    $saved = Stop-VetoSources

    Write-Host '[3/4] 重新連接 eGPU'
    $state = Get-EgpuState
    if ($state.HasRouter -or $state.HasGpu) {
        Write-Host '  請拔掉 eGPU 的線...'
        $deadline = (Get-Date).AddSeconds($UnplugTimeoutSec)
        while (($state.HasRouter -or $state.HasGpu) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 1
            $state = Get-EgpuState
        }
        if ($state.HasRouter -or $state.HasGpu) { Write-Warning '仍偵測到 eGPU，直接繼續' }
        Write-Host '  已拔除。等約 5 秒後，把 eGPU 插回「同一個」port。'
    } else {
        Write-Host '  請把 eGPU 插上（同一次開機盡量都用同一個 port）。'
    }

    $deadline = (Get-Date).AddSeconds($ConnectTimeoutSec)
    $okSince = $null
    $lastText = ''
    while ((Get-Date) -lt $deadline) {
        $state = Get-EgpuState
        $text = Format-EgpuState $state
        if ($text -ne $lastText) {
            Write-Host ('  {0:HH:mm:ss} {1}' -f (Get-Date), $text)
            $lastText = $text
        }
        if ($state.GpuCode -eq 0) {
            if (-not $okSince) { $okSince = Get-Date }
            if (((Get-Date) - $okSince).TotalSeconds -ge $StableSec) { $isReady = $true; break }
        } else {
            $okSince = $null
        }
        Start-Sleep -Seconds 2
    }
} catch {
    # Keep going to finally and Read-Host so the window does not close on errors.
    $errorText = $_.Exception.Message
    Write-Host "發生錯誤：$errorText" -ForegroundColor Red
} finally {
    if ($saved) {
        Write-Host '[4/4] 恢復服務與程式'
        Restore-VetoSources $saved
    }
    Write-Host ''
    if ($isReady) {
        Write-Host 'eGPU 已就緒。' -ForegroundColor Green
    } else {
        $failReason = if ($errorText) { "執行中斷：$errorText" } else { "在 $ConnectTimeoutSec 秒內沒有等到 GPU 正常（最後狀態：$(Format-EgpuState $state)）。" }
        Write-Host $failReason -ForegroundColor Yellow
        Write-Host '可以再執行一次；仍失敗的話，先插好 eGPU 再重開機。'
    }
    Write-Host '注意：使用中途若出現 Code 12（NVIDIA CoInstaller 觸發），此工具無法預防。'
    Stop-Transcript | Out-Null
}

Read-Host '按 Enter 結束'
exit ([int](-not $isReady))
