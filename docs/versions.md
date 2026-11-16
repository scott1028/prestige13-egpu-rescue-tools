# 用到的版本和升級指引

[← 回到首頁](../README.md) ｜ [Linux README](linux/README.md) ｜ [Windows README](windows/README.md)

這份記下本專案驗證時用的每個版本、為什麼用這個版本，以及以後升級 NVIDIA 驅動或 kernel 前後要做什麼。Linux 的修正和驅動、kernel 綁得很緊，升級時最容易壞掉。除了 Windows 那一節，版本都是 2026-11-16 在這台筆電上查的。

## 三件事一定要記得

1. **這台機器會自己更新 kernel，也可能自己更新驅動。** unattended-upgrades 開著，2026-11-11 就在背景把 kernel 從 7.0.0-42 升到 7.0.0-43，見[自動更新會動到什麼](#自動更新會動到什麼)。
2. **升級後先重開機，再用 eGPU。** 原因見[升級和重開機](#5-升級和重開機)。
3. **重開機後跑一次[升級後檢查](#6-升級後檢查)，全部正常才算升級完成。**

## 名詞對照

| 名詞 | 白話解釋 |
|---|---|
| 驅動分支（branch） | NVIDIA 驅動的大版本號，例如 580、595、615。套件名稱帶著它（`nvidia-driver-595-open`），所以換分支等於換一個套件 |
| open kernel module | NVIDIA 開放原始碼的 kernel 驅動，套件名稱結尾是 `-open` |
| 預先編好的 kernel 模組 | Ubuntu 替每一版 kernel 編好的 NVIDIA 模組，例如 `linux-modules-nvidia-595-open-7.0.0-43-generic`，不用在本機編譯 |
| DKMS | 在你的電腦上重新編譯 kernel 模組的機制。本專案沒有用 |
| meta 套件 | 自己不裝檔案，只依賴「目前最新那一版」的套件。例如 `linux-generic-hwe-26.04` 永遠指向最新的 kernel |
| 過渡套件（transitional package） | 本身是空的，只依賴另一個套件。Ubuntu 用它把舊分支的使用者帶到新分支，像搬家後留在舊地址的轉寄單 |
| pocket | Ubuntu 套件庫的分區。`resolute` 是發行時的版本，`resolute-updates` 和 `resolute-security` 放之後的更新 |
| unattended-upgrades | Ubuntu 在背景自動安裝更新的服務 |
| GSP 韌體 | GPU 上的韌體，版本要和 kernel 模組完全一樣，套件是 `nvidia-firmware-595-595.99.02` |

## 版本總表

### Linux：系統、驅動和桌面

| 項目 | 版本 | 套件 | 跟本專案的關係 |
|---|---|---|---|
| Ubuntu | 26.04.1 LTS（`resolute`） | — | 所有 Linux 修正都在這版驗證 |
| kernel | `7.0.0-43-generic` | `linux-generic-hwe-26.04` 7.0.0-43.43 | `egpu-bar1` 照 v7.0 的 PCI 行為寫 |
| NVIDIA 驅動 | 595.99.02 | `nvidia-driver-595-open` 595.99.02-0ubuntu0.26.04.1 | `egpu-gc34x.conf`、`egpu-clocks` 都依賴它 |
| NVIDIA kernel 模組 | 595.99.02，Ubuntu 預先編好 | `linux-modules-nvidia-595-open-generic-hwe-26.04` 7.0.0-43.43 | 只有 7.0.0-43 這版 kernel 裝了模組 |
| GSP 韌體 | 595.99.02 | `nvidia-firmware-595-595.99.02` | [故障 2](linux/guide.md#2-gpu-韌體當掉桌面和-chrome-一起卡住0928) 就是它逾時沒回應 |
| egl-wayland | 1.1.21 | `libnvidia-egl-wayland1` 1:1.1.21-1 | KWin 在 5090 上用 EGL 合成（C 模式） |
| KDE Plasma、KWin | 6.6.6 | `plasma-workspace`、`kwin-wayland` 4:6.6.6-0ubuntu0.1 | C 模式靠 KWin 讀 `KWIN_DRM_DEVICES` |
| Mesa（Intel 內顯） | 26.0.8 | `libegl-mesa0` 26.0.8-1ubuntu0.3 | A 模式由 Intel 內顯合成 |
| systemd | 259.9 | `systemd` 259.9-0ubuntu0.1 | logind 的 `UserStopDelaySec` 預設 10 秒，所以登出後要等 30 秒 |
| SDDM | 0.21.0 | `sddm` 0.21.0+git20250502.4fe234b-2ubuntu3 | 登出後在這個登入畫面等 30 秒 |
| 系統的 PyQt6 | 6.10.2 | `python3-pyqt6` 6.10.2-2build5 | 直接執行 `egpu-mode` 原始碼時用 |
| 系統的 Python | 3.14.4 | — | 同上 |

### 硬體和韌體

| 項目 | 版本 | 從哪裡看 | 跟本專案的關係 |
|---|---|---|---|
| 筆電 BIOS | `E13Q3IMS.112`（2025-12-22） | `/sys/class/dmi/id/bios_version` | BIOS 給 Thunderbolt 的位址區段放不下 32 GB 的 BAR1，是 Windows 和 Linux 共同的根因 |
| GPU VBIOS | `98.03.75.00.19` | `nvidia-smi -q` | 沒有直接依賴，升級後比對用 |
| Thunderbolt 連線 | USB4，40 Gb/s（2 lanes × 20 Gb/s） | `boltctl list` | 約等於 PCIe Gen3 x4，所以連線上限設成 Gen3 |
| Secure Boot | 關閉 | `mokutil --sb-state` | 開啟時沒有試過 |

### AppImage 打包和 GitHub Actions

| 項目 | 版本 | 寫在哪裡 |
|---|---|---|
| python-appimage | 1.4.7 | `tools/egpu-linux/appimage/build-appimage.sh` |
| AppImage 裡的 Python | 3.12 | 同上，`--python-version 3.12` |
| AppImage 裡的 PyQt6 | 6.12.1（內含 Qt 6.12.0） | `tools/egpu-linux/appimage/egpu-mode/requirements.txt` |
| appimagetool | `continuous`，沒有固定 | python-appimage 打包時自己下載 |
| Linux runner | `ubuntu-24.04`，Python 3.12（`actions/setup-python@v5`） | `.github/workflows/release.yml` |
| Windows runner | `windows-latest` | 同上 |
| 其他 actions | `actions/checkout@v4`、`actions/upload-artifact@v4`、`actions/download-artifact@v4` | 同上 |

### Windows

這一節的版本來自 2026-11-15 的[分析報告](windows/egpu-intermittent-detection-report.md)和 [Windows README](windows/README.md)。

| 項目 | 版本 | 跟本專案的關係 |
|---|---|---|
| Windows 11 | build 26300 | 分析報告的環境 |
| NVIDIA 驅動 | 621.24 | 分析報告裡 BAR1 是 32768 MiB 的量測，和 `FiltereGPUResizableBAR1` 這個設定名稱，都來自這版 |
| C# 編譯器 | .NET Framework 4.8 內建的 `csc.exe` | `build.cmd` 用它編譯 `egpu-tray.exe` |
| PowerShell | Windows 內建的 `powershell.exe` | 執行 `egpu-connect.ps1` |
| 筆電 BIOS | `E13Q3IMS.112` | 和 Linux 同一版 |

## 為什麼用這些版本

### NVIDIA 驅動：`nvidia-driver-595-open` 595.99.02

| 原因 | 說明 |
|---|---|
| 一定要用 `-open` | RTX 5090 Laptop 是 Blackwell 世代。NVIDIA 說 Blackwell 一定要用 open kernel module，proprietary 版不支援 |
| 選 595 分支 | 2026-11-16 安裝時，`ubuntu-drivers devices` 把 `nvidia-driver-595-open` 標成 `recommended`。當時也有 580 和 615 可以選，兩個都沒有測過 |
| 用 Ubuntu 預先編好的模組，不用 DKMS | 不用在本機編譯。模組的 meta 套件跟著 kernel 一起升級，新 kernel 會直接拿到對應的模組 |
| 修正都在 595.99.02 上驗證 | 當天讀過 595.99.02 的原始碼，確認三件事：BAR1 沒有位址時驅動不接手 GPU，`egpu-bar1` 才能先縮 BAR1 再讓驅動接手；開機後才插上的 GPU 也會拿到顯示裝置；`NVreg_EnableResizableBar` 預設關閉。`RMPcieLinkSpeed=0x800002a0` 的欄位意思也是照 595.99.02 的 `nvrm_registry.h` 寫的 |
| 小版本是 595.99.02 | 不是特別挑的。`apt-get install` 裝的是當時 `resolute-updates` 和 `resolute-security` 裡最新的版本，發行時的版本是 595.58.03 |

### kernel：`7.0.0-43-generic`（HWE）

- 不是特別挑的。裝驅動前，unattended-upgrades 已經在 2026-11-11 把 kernel 從 7.0.0-42 升到 7.0.0-43。
- `egpu-bar1` 的做法照 v7.0 的行為寫：`reset_resource()` 會清掉分配失敗的 BAR，`resource1_resize` 就回 `EINVAL`，所以改用 `setpci` 直接改 ReBAR 暫存器，再重新掃描 PCI。見 [Linux guide 故障 1](linux/guide.md#1-驅動載不起來bar1-放不下0841)。

### 桌面：KDE Plasma 6.6.6 Wayland

- 不是特別挑的，是安裝時系統上的版本。
- [故障 6](linux/guide.md#6-100hz-只剩-50-fpskwin-比-5090-早啟動1104)（KWin 比 5090 早啟動就接不上）和 C 模式（`KWIN_DRM_DEVICES`）都是在 KWin 6.6.6 上觀察和驗證的。

### AppImage 的打包工具

- python-appimage 1.4.7 和 PyQt6 6.12.1：2026-11-16 在本機打包並測過的版本。PyQt6 原本沒寫版本，測完才固定成 6.12.1，CI 打包出來的才會和測過的一樣。
- Python 3.12：當天打包和測試用的版本，選它的原因當時沒有記錄。
- appimagetool 沒有固定：python-appimage 每次都下載 `continuous` 版，本專案管不到。它的行為一改，GitHub Actions 的 `linux` job 最可能先壞。
- runner 和 actions 的版本：寫 workflow 時選的，原因沒有記錄。

### Windows

621.24 和 build 26300 都是分析當時機器上的版本，不是特別挑的。`egpu-connect.ps1` 用 PCI ID `PCI\VEN_10DE&DEV_2C18` 找 GPU，沒有檢查驅動版本。

## 自動更新會動到什麼

unattended-upgrades 像幫你自動補貨的管家，每天在背景裝更新。方便，但它也會在你沒注意時換掉 kernel，驅動也可能一起換掉。

```text
unattended-upgrades (daily; origins: resolute, resolute-security)
  ├─ linux-generic-hwe-26.04 ───────────────────────► new kernel (7.0.0-42 -> 43 on 2026-11-11)
  ├─ linux-modules-nvidia-595-open-generic-hwe-26.04 ► NVIDIA module for that kernel
  └─ nvidia-driver-595-open ────────────────────────► newer 595.x (595.99.02 came from resolute-security)
```

- 設定在 `/etc/apt/apt.conf.d/20auto-upgrades`（`APT::Periodic::Unattended-Upgrade "1"`）和 `50unattended-upgrades`。`Package-Blacklist` 是空的，`apt-mark showhold` 也沒有鎖住任何套件。
- 595.99.02 本身就在 `resolute-security`，所以之後的 595 更新很可能也會自動裝上。

### 分支也可能被換掉

2026-11-16 查到，Ubuntu 已經把幾個舊分支改成過渡套件：

| 舊分支的套件 | 現在依賴 |
|---|---|
| `nvidia-driver-570-open` | `nvidia-driver-580-open` |
| `nvidia-driver-590-open` | `nvidia-driver-595-open` |
| `nvidia-driver-610-open` | `nvidia-driver-615-open` |

這是推論：595 分支停止支援時，`nvidia-driver-595-open` 也可能變成依賴新分支的過渡套件。`apt upgrade` 和 unattended-upgrades 都會裝新的相依套件，2026-11-11 的新 kernel 就是這樣裝上的，所以驅動可能在背景直接換到另一個分支。換分支後，[升級時可能壞掉的地方](#升級時可能壞掉的地方)每一項都要重新確認。

### 想自己決定什麼時候升級

| 做法 | 怎麼做 | 代價 |
|---|---|---|
| 維持現狀（目前的設定） | 不用動 | 安全更新會自動裝好，但 kernel 或驅動可能在你沒注意時換掉 |
| 鎖住驅動和 kernel | 執行下面的 `apt-mark hold` | 這些套件也收不到安全更新，要自己定期照[升級步驟](#升級步驟)解鎖、升級 |

```bash
sudo apt-mark hold nvidia-driver-595-open linux-modules-nvidia-595-open-generic-hwe-26.04 \
  linux-generic linux-image-generic linux-headers-generic \
  linux-generic-hwe-26.04 linux-image-generic-hwe-26.04 linux-headers-generic-hwe-26.04
apt-mark showhold                                                # the 8 packages above
apt -s upgrade 2>/dev/null | grep -E '^Inst .*(nvidia|linux-)'   # held packages never show up here
```

要升級時，先用 `sudo apt-mark unhold` 加上同一串套件名稱解除鎖定。

## 升級步驟

照順序做，每一步都確認過再往下。

```text
1 check it works now ─► 2 record versions ─► 3 see what changes ─► 4 keep a way back
─► 5 upgrade + reboot ─► 6 post-upgrade checks ─► 7 update the docs
```

第 1 步最容易跳過，但最重要：本來就壞掉的話，升級後分不出是誰弄壞的。

### 1. 確認現在是好的

跑一次 [Linux guide 的健康檢查指令](linux/guide.md#健康檢查指令)，全部是正常結果再升級。

### 2. 記下目前的版本

```bash
lsb_release -ds; uname -r
head -1 /proc/driver/nvidia/version
nvidia-smi --query-gpu=driver_version,vbios_version --format=csv,noheader
dpkg-query -W -f='${db:Status-Abbrev} ${Package} ${Version}\n' 'nvidia-driver-*' 'linux-modules-nvidia-*' \
  'linux-generic*' kwin-wayland libnvidia-egl-wayland1 systemd 2>/dev/null | grep '^ii'
```

把輸出和[版本總表](#版本總表)比對。不一樣的話，代表自動更新已經換過版本。

### 3. 看這次會動到哪些套件

```bash
sudo apt update
apt list --upgradable 2>/dev/null | grep -E 'nvidia|linux-(generic|image|headers|modules)'
apt-cache show nvidia-driver-595-open | grep -E '^(Version|Depends):'
```

| 看到什麼 | 怎麼做 |
|---|---|
| 只有 595 的小版本或 kernel 的新版 | 繼續下一步 |
| `nvidia-driver-595-open` 的 `Depends` 只剩另一個分支，或出現新分支的套件 | 換分支。先對照[升級時可能壞掉的地方](#升級時可能壞掉的地方)，逐項看新版有沒有改 |

### 4. 留一條退路

- GRUB 的「Advanced options for Ubuntu」可以選舊的 kernel 開機。目前只有 7.0.0-43 裝了 NVIDIA 模組，7.0.0-42 和 7.0.0-15 沒有。用它們開機時 eGPU 沒有驅動，但筆電螢幕（Intel 內顯）還能用。
- 升級 kernel 後，執行 `dpkg -l 'linux-modules-nvidia-*'`，確認舊 kernel 的 NVIDIA 模組還在，才能把舊 kernel 當成有 eGPU 的退路。
- 驅動降版要把所有 `*-595` 套件一起指定版本，而且只能退回 `apt-cache policy nvidia-driver-595-open` 還列得出來的版本（目前是 595.99.02 和 595.58.03）。這個做法還沒試過。

### 5. 升級和重開機

```bash
sudo apt upgrade
systemctl reboot
```

eGPU 可以接著重開機：11:49 接著 eGPU 開機時，三個自動修正都在開機過程中套用了。**不要熱拔 eGPU。**

為什麼要馬上重開機：NVIDIA 驅動分成 kernel 模組和使用者端程式庫兩半，像同一把鑰匙的兩半，版本號要一模一樣才打得開。apt 只換掉程式庫那一半，舊的模組會一直跑到重開機為止。這段時間 `nvidia-smi` 會回 `Failed to initialize NVML: Driver/library version mismatch`，`egpu-clocks` 就鎖不了時脈。這是 NVIDIA 驅動的一般行為，這台機器還沒遇過。

### 6. 升級後檢查

| 想確認什麼 | 指令 | 正常結果 |
|---|---|---|
| 模組和程式庫版本一樣 | `head -1 /proc/driver/nvidia/version` 和 `nvidia-smi --query-gpu=driver_version --format=csv,noheader` | 兩個版本號相同 |
| 驅動接上 GPU | `lspci -k -d 10de:2c18` | `Kernel driver in use: nvidia` |
| BAR1 修正 | `journalctl -b -t egpu-bar1` 和 `lspci -vv -s 03:00.0 \| grep "Region 1"` | 有 `BAR1 set to 256 MB`，`Region 1` 是 `size=256M` |
| ReBAR 預設值沒變 | `grep EnableResizableBar /proc/driver/nvidia/params` | `EnableResizableBar: 0` |
| 連線上限設定有載入 | `grep RegistryDwords /proc/driver/nvidia/params` | 有 `RMPcieLinkSpeed=0x800002a0` |
| Thunderbolt CLx | `cat /sys/module/thunderbolt/parameters/clx` | `N` |
| 時脈鎖定 | `journalctl -b -t egpu-clocks` | `P4, 3, 9001 MHz` |
| C2/C3 狀態 | `cat /sys/devices/system/cpu/cpu0/cpuidle/state*/disable` | `0 0 1 1` |
| C 模式 | `nvidia-smi \| grep kwin_wayland` | 有一行 `kwin_wayland` |
| B 模式 | 在 `egpu-mode` 用 B 開 `glxgears`，再執行 `nvidia-smi` | 列出 `glxgears` |
| 用一陣子之後 | `journalctl -k -b \| grep -E "CmpltTO\|Xid"` | 沒有輸出 |

最後一項要正常使用 10–15 分鐘，中間放著不動 1–2 分鐘、重複幾次。故障 2 的 GSP 當機就是閒置約 48 秒後發生的。

### 7. 更新文件

全部正常後，更新這份文件的[版本總表](#版本總表)和日期。升級時遇到新的問題，寫進 [Linux guide](linux/guide.md) 的故障排除紀錄。

## 升級時可能壞掉的地方

升級後檢查有一項不正常時，從這裡找原因。

### NVIDIA 驅動

| 依賴的東西 | 用在哪裡 | 升級後可能怎樣 | 怎麼確認 |
|---|---|---|---|
| `RMPcieLinkSpeed=0x800002a0` | `egpu-gc34x.conf` | 這是沒有公開文件的設定，欄位定義取自 595.99.02 的 `nvrm_registry.h`。新版可能改了欄位或拿掉，設定被忽略或意思變了 | 打開新版號的 `src/nvidia/interface/nvrm_registry.h`（[595.99.02 版](https://github.com/NVIDIA/open-gpu-kernel-modules/blob/595.99.02/src/nvidia/interface/nvrm_registry.h)，網址裡的版本號換成新的），比對 `RMPcieLinkSpeed` 的欄位 |
| `EnableResizableBar` 預設是 0 | 驅動預設值 | 新版預設改成開啟時，驅動會試著把 BAR1 放大回 32 GB | `grep EnableResizableBar /proc/driver/nvidia/params` |
| BAR1 沒有位址時驅動不接手 | `egpu-bar1`、`70-egpu-bar1.rules` | 驅動行為變了的話，可能在 `egpu-bar1` 縮 BAR1 之前就接手或失敗 | `journalctl -b -t egpu-bar1`、`lspci -k -d 10de:2c18` |
| 記憶體時脈 9001、14001 MHz | `egpu-clocks`（`nvidia-smi -lmc 9001,14001`） | 595.99.02 回報支援 405、810、9001、11001、14001 MHz。清單變了，鎖定就會失敗，PCIe 掉回 Gen1，100Hz 卡頓 | `nvidia-smi -q -d SUPPORTED_CLOCKS` |
| 封鎖 nouveau 和 nova | 驅動套件裝的 `/usr/lib/modprobe.d/nvidia-graphics-drivers.conf` | 換驅動或移除驅動後，nouveau 或 kernel 裡的 `nova_core` 可能先搶到 GPU | `lspci -k -d 10de:2c18` 的 `Kernel driver in use` |
| PRIME offload 變數 | `egpu-mode` 的 B（`__NV_PRIME_RENDER_OFFLOAD`、`__GLX_VENDOR_LIBRARY_NAME`、`VK_LOADER_DRIVERS_SELECT`） | 程式改回用 Intel 算圖 | 用 B 開 `glxgears`，`nvidia-smi` 看得到它 |

### kernel 和桌面

| 依賴的東西 | 用在哪裡 | 升級後可能怎樣 | 怎麼確認 |
|---|---|---|---|
| v7.0 的 PCI 重新分配行為和 sysfs 路徑 | `egpu-bar1` | 新 kernel 改了 `reset_resource()` 或 `/sys/devices/pci0000:00/<root port>/<upstream>/...` 的結構，BAR1 縮不了 | `journalctl -b -t egpu-bar1`、`lspci -vv -s 03:00.0 \| grep "Region 1"` |
| `thunderbolt clx=0` | `egpu-gc34x.conf` | 新 kernel 拿掉這個參數，設定被忽略 | `cat /sys/module/thunderbolt/parameters/clx` |
| `KWIN_DRM_DEVICES` | `egpu-mode` 的 C | KDE 改了這個變數，C 模式就沒效果 | C 模式下 `nvidia-smi \| grep kwin_wayland` |
| `UserStopDelaySec` 預設 10 秒 | 「登出後等 30 秒」 | 預設值變長，等 30 秒就不夠 | `busctl get-property org.freedesktop.login1 /org/freedesktop/login1 org.freedesktop.login1.Manager UserStopDelayUSec`，目前是 `t 10000000`（微秒） |

## 參考資料

- [NVIDIA Transitions Fully Towards Open-Source GPU Kernel Modules](https://developer.nvidia.com/blog/nvidia-transitions-fully-towards-open-source-gpu-kernel-modules/)：Blackwell 一定要用 open kernel module
- [NVIDIA open-gpu-kernel-modules 595.99.02 `nvrm_registry.h`](https://github.com/NVIDIA/open-gpu-kernel-modules/blob/595.99.02/src/nvidia/interface/nvrm_registry.h)：`RMPcieLinkSpeed` 的欄位定義
- [Linux v7.0 `drivers/pci`](https://github.com/torvalds/linux/tree/v7.0/drivers/pci)：`reset_resource()`、`rebar.c`
- 本機資料（2026-11-16）：`apt-cache policy`、`apt-cache show`、`ubuntu-drivers devices`、`/var/log/apt/history.log`、`/etc/apt/apt.conf.d/`、`/proc/driver/nvidia/params`、`nvidia-smi -q`、`dpkg-query`
