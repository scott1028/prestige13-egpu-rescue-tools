# GC34X eGPU on Ubuntu：完整紀錄

[← 回到首頁](../../README.md) ｜ [Linux README](README.md) ｜ [原始資料](evidence/README.md)

這份是 [README](README.md) 的詳細版，順序是：先看名詞，再安裝、日常使用，出事時再查故障紀錄。文中時間都是 2026-11-16 的本機時間（CST）。

## 名詞對照

| 名詞 | 白話解釋 |
|---|---|
| eGPU | 外接顯示卡，這裡指 GC34X 盒子裡的 RTX 5090 Laptop GPU |
| BAR1 | GPU 對 CPU 開放的一段位址，CPU 透過它讀寫 GPU 記憶體。這張卡預設要 32 GB |
| ReBAR（Resizable BAR） | 可以調整 BAR 大小的 PCIe 功能，我們用它把 BAR1 縮成 256 MB |
| TRP0 窗口 | BIOS 替 Thunderbolt port 預留的位址區段，eGPU 的 BAR 都要放進去 |
| GSP | GPU 上的韌體處理器，驅動的很多指令要透過它執行 |
| Xid | NVIDIA 驅動的錯誤代碼。這次出現過 79（GPU 掉線）、154（需要重設 GPU）、175（等 GSP 回應逾時） |
| AER | PCIe 的錯誤回報機制，錯誤會寫進 kernel log |
| `CmpltTO` | Completion Timeout：GPU 向筆電記憶體要資料，等太久沒回應就放棄 |
| C-state | CPU 閒置時的省電深度。C1 是淺睡，C2/C3 是深睡 |
| P-state | GPU 的效能檔位，P0 最快，P8 最省電 |
| PCIe Gen1 / Gen3 | PCIe 連線速度。x4 時 Gen1 實測約 0.8 GB/s，Gen3 理論約 3.9 GB/s |
| D state | 程式卡在 kernel 裡等某件事完成，連 `kill -9` 都殺不掉 |
| DRM | Linux 的顯示驅動框架，`/dev/dri/card*` 就是它提供的裝置 |
| KWin | KDE 的 Wayland compositor，負責合成畫面、管理所有螢幕 |
| persistence mode | 沒有程式在用 GPU 時，NVIDIA 驅動也讓 GPU 保持初始化，時脈鎖定才不會消失 |
| udev 規則 | 裝置插上、拔掉或接上驅動時，系統自動執行的規則 |

## 資料從筆電到 GPU 怎麼走

花點、掉線、卡頓都發生在這條路上，先看清楚它分成哪幾段。

```text
00:07.0  Lunar Lake TB4 root port (8086:a84e)
   │     Thunderbolt link: 20 Gb/s x 2 lanes = 40 Gb/s
01:00.0  JHL9480 Barlow Ridge bridge in the GC34X (8086:5786)
   │
02:00.0  Barlow Ridge downstream port
   │     PCIe x4: Gen1 2.5 GT/s ... Gen3 8 GT/s, follows the GPU P-state
03:00.0  RTX 5090 Laptop GPU (10de:2c18), 03:00.1 is its HDMI audio
```

最容易出事的是最後一段（02:00.0 到 03:00.0）。外接螢幕是 `HDMI-A-2`，一台上下兩片的攜帶式螢幕，系統把它當成一個 2560x2880 的顯示器，可選 30 / 60 / 75 / 100Hz。

## 從頭安裝

### 檔案放哪

[`tools/egpu-linux/`](../../tools/egpu-linux/) 放原始檔，安裝時複製到系統目錄。10:53 用 `cmp` 比對過，系統目錄裡的檔案和這裡完全一致。

| `tools/egpu-linux/` 裡的檔案 | 安裝到 | 由哪支腳本安裝 |
|---|---|---|
| `egpu-bar1` | `/usr/local/sbin/egpu-bar1` | `install.sh`、`egpu-fix.sh` |
| `70-egpu-bar1.rules` | `/etc/udev/rules.d/70-egpu-bar1.rules` | `install.sh` |
| `egpu-gc34x.conf` | `/etc/modprobe.d/egpu-gc34x.conf` | `egpu-fix.sh` |
| `egpu-cstate` | `/usr/local/sbin/egpu-cstate` | `install-cstate.sh` |
| `71-egpu-cstate.rules` | `/etc/udev/rules.d/71-egpu-cstate.rules` | `install-cstate.sh` |
| `egpu-clocks` | `/usr/local/sbin/egpu-clocks` | `install-clocks.sh` |
| `egpu-clocks.service` | `/etc/systemd/system/egpu-clocks.service` | `install-clocks.sh` |
| `72-egpu-clocks.rules` | `/etc/udev/rules.d/72-egpu-clocks.rules` | `install-clocks.sh` |
| `egpu-mode` | 不用安裝，直接執行。選 C 時才寫入 `~/.config/plasma-workspace/env/egpu-mode.sh` 和 `~/.config/autostart/egpu-mode-confirm.desktop`，選 A 時刪掉 | — |
| `appimage/` | 不安裝，只用來把 `egpu-mode` 打包成 AppImage，見[打包成 AppImage](#打包成-appimage) | — |

`install.sh` 和 `egpu-fix.sh` 原本放在 `/tmp` 下的暫存區，重開機後就不見了。這裡的版本是從對話紀錄救回來的最後版本。

### 每支安裝腳本做什麼

| 腳本 | 做的事 |
|---|---|
| `install.sh` | 安裝 `nvidia-driver-595-open` 和 `linux-modules-nvidia-595-open-generic-hwe-26.04`，裝好 BAR1 修正並當場縮一次 BAR1，結果寫進 `install.log` |
| `egpu-fix.sh` | 更新 `egpu-bar1`，裝好 `egpu-gc34x.conf`，跑 `update-initramfs -u`，把連線狀態寫進 `/var/log/egpu-fix.log` |
| `install-cstate.sh` | 裝好 C-state 規則，依 eGPU 目前有沒有接上，馬上套用 `limit` 或 `restore` |
| `install-clocks.sh` | 裝好時脈鎖定，先解除現有鎖定，再模擬一次 bind 事件，確認自動流程有效 |

驅動版本是 595.99.02，09:22:44 由 `install.sh` 安裝。為什麼用這個版本、以後怎麼升級，見[版本和升級指引](../versions.md)。

### 插上 eGPU 時自動發生什麼

插上 eGPU 後，三條 udev 規則會依序動作。

```text
plug in
  ├─ add ────► 70-egpu-bar1.rules   ─► egpu-bar1: BAR1 -> 256 MB, link -> Gen3, PCI rescan
  ├─ add ────► 71-egpu-cstate.rules ─► egpu-cstate limit   (C2/C3 off)
  ├─ remove ─► 71-egpu-cstate.rules ─► egpu-cstate restore (caused by the rescan)
  ├─ add ────► 71-egpu-cstate.rules ─► egpu-cstate limit   (after the rescan)
  └─ bind ───► 72-egpu-clocks.rules ─► egpu-clocks.service ─► persistence mode on, memory clock locked
```

中間那次 `remove` 是 `egpu-bar1` 重新掃描 PCI 造成的。10:42 實際插上時，log 依序是 `limit` → `restore` → `limit`，最後停在 C2/C3 關閉。

### 幾個容易踩的坑

1. 裝完驅動後，`modprobe -r nouveau` 會靜靜地失敗，因為 `/usr/lib/modprobe.d/nvidia-graphics-drivers.conf` 有 `alias nouveau off`，要改用 `rmmod nouveau`。`install.sh` 裡還是 `modprobe -r`，所以 09:23:20 到 09:27:19 之間，NVIDIA 驅動因為 `GPU 0000:03:00.0 is already bound to nouveau` 重試了 1624 次。重開機後 nouveau 已經被封鎖，不影響之後的使用。
2. `/etc/modprobe.d/` 的設定要先跑 `update-initramfs -u` 再重開機才會生效。`thunderbolt` 和 `nvidia` 模組在 initramfs 階段就載入了。
3. Claude Code 的暫存區在 `/tmp`，重開機就會清掉。要留下來的檔案請放進 `tools/egpu-linux/`。

## 日常使用

### 插上 eGPU

開機登入後隨時可以插，當天每次都是登入後才插上。

**插上後要登出再登入一次，不用重開機。** KWin 只在啟動時接上 GPU。如果 KWin 比 5090 早啟動，就不能用 5090 接收畫面，只能改用 CPU 搬，100Hz 會只剩 50 fps，見[故障 6](#6-100hz-只剩-50-fpskwin-比-5090-早啟動1104)。

### 確認狀態

```bash
journalctl -b -t egpu-bar1 -t egpu-cstate -t egpu-clocks --no-pager -o cat
nvidia-smi --query-gpu=pstate,pcie.link.gen.current,clocks.mem,power.draw --format=csv
```

正常的話，`egpu-cstate` 最後一筆是 `limit: deep C-states disable=1`，`nvidia-smi` 是 `P4, 3, 9001 MHz`。10:49 量到的功耗是 15.72 W。

### 拔掉 eGPU

**先關機再拔。** 原因見 [故障 5](#5-熱拔-egpu-讓整個桌面卡死1036)。沒接 eGPU 開機時不會觸發 `limit`，C2/C3 會維持開啟。

### 該選 60Hz 還是 100Hz

外接螢幕每一格畫面都要從筆電記憶體送進 GPU，更新率越高，要送的資料越多。

| 更新率 | 全畫面更新每秒資料量（估算） | Gen3 實測 |
|---|---|---|
| 60Hz | 約 1.8 GB/s | 足夠 |
| 100Hz | 約 3.0 GB/s | 最高量到 2.4 GB/s。TestUFO：KWin 沒接上 5090 時固定 50 fps，接上後約 74 fps，不穩定。改用 C 模式後穩定 100 fps |

估算方式是 2560 × 2880 × 4 bytes ≈ 29.5 MB 一格。切換指令：

```bash
kscreen-doctor output.HDMI-A-2.mode.26   # 2560x2880@60
kscreen-doctor output.HDMI-A-2.mode.27   # 2560x2880@100
```

mode 編號以 `kscreen-doctor -o` 為準。

### 切換使用方式（egpu-mode）

不用安裝，有兩種執行方式：

| 方式 | 怎麼做 | 需要什麼 |
|---|---|---|
| AppImage | 到 [Releases](https://github.com/scott1028/prestige13-egpu-rescue-tools/releases/latest) 下載 `egpu-mode-x86_64.AppImage`，`chmod +x` 後雙擊或執行 | 什麼都不用裝，Python 和 PyQt6 都包在裡面 |
| 直接執行原始碼 | 在 Dolphin 雙擊 `tools/egpu-linux/egpu-mode`，或在終端機執行 `./tools/egpu-linux/egpu-mode` | 系統的 `python3-pyqt6`（當天的機器已經裝了） |

兩種方式的功能一樣，選 C 時寫入的檔案也一樣。AppImage 怎麼打包見[打包成 AppImage](#打包成-appimage)。視窗上方會顯示 5090 有沒有接上、KWin 用哪張卡合成，以及 KWin 有沒有接上 5090。

| 按鈕 | 做什麼 | 生效方式 |
|---|---|---|
| A. Intel 合成（預設） | Intel 內顯合成桌面，5090 讀取畫面後輸出 | 登出再登入 |
| B. 用 5090 開啟程式 | 輸入指令，這個程式用 PRIME offload 在 5090 算圖 | 馬上生效 |
| C. 5090 合成桌面（建議） | 登入時設定 `KWIN_DRM_DEVICES`，讓 5090 當 KWin 的主卡 | eGPU 接著的狀態下登出再登入，或接著 eGPU 開機 |

三種都不用重開機。

11:54 實測 C：TestUFO 在 100Hz 穩定 100 fps，`rxpci` 從約 2000 MB/s 降到 6–20 MB/s，Intel 內顯約 2% 忙碌。`nvidia-smi` 列出 `kwin_wayland`、`plasmashell`、`chrome` 等程式，代表這些程式也改用 5090 算圖，畫面不必再經過 TB4。

20:06–20:13 實際按過 A、B、C 三個按鈕，切換都正常。

**登出後在登入畫面等 30 秒再登入。** logind 的 `UserStopDelaySec` 是預設的 10 秒：登出後 10 秒內再登入，新 session 會沿用舊 session 的 systemd 使用者管理程式（`user@1000.service`）。三次登入的對照：

| 時間 | 登入的模式 | 登出到登入 | 使用者管理程式 | 結果 |
|---|---|---|---|---|
| 11:48 | C | 10 秒 | 沿用舊的 | 桌面 portal 卡在啟動中，每個程式等 25 秒才逾時（`NoReply`、`Timeout was reached`），終端機開不起來 |
| 20:06 | A | 8 秒 | 沿用舊的 | 正常 |
| 20:13 | C | 11 秒 | 新的 | 正常 |

只有「C 加上沿用舊的使用者管理程式」出過問題，而且只遇過一次，還不能確定是不是 C 造成的。等超過 10 秒就不會沿用，等 30 秒比較保險。

11:48 那次的 journal（[`journal-2026-11-16-1039-part2.log`](evidence/logs/journal-2026-11-16-1039-part2.log)，11:47:54–11:49:11）：

```text
11:47:54  logout: xdg-document-portal cgroup can't be killed, KWin stopping
11:47:55  old session D-Bus still activating portal services
11:48:05  new login reuses the old user manager (systemd[21838])
11:48:31  KWin, plasmashell, kded6, dolphin ...: portal NoReply
11:48:33  terminal: "Cannot get portal ... version: Timeout was reached"
11:49:11  user manager stopping; rebooted after this
```

C 怎麼運作、出事怎麼救：

- 選 C 時寫入兩個檔案：登入時由 startplasma 執行的 `~/.config/plasma-workspace/env/egpu-mode.sh`，和 `~/.config/autostart/egpu-mode-confirm.desktop`。選 A 時兩個都刪掉。
- 登入時，env 腳本找到 5090 才設定 `KWIN_DRM_DEVICES`。沒接 eGPU 就不設定，自動用 Intel。
- 設定時會留下 `~/.config/egpu-mode/pending`，桌面起來後由 autostart 項目刪掉。如果下次登入時它還在，代表上次沒有登入成功，env 腳本會刪掉自己和 autostart 項目，切回 A。`egpu-mode status` 會顯示 `⚠ 上次用 C 登入沒有成功`。
- 黑畫面時，按 `Ctrl+Alt+F3` 登入文字終端機，執行 `rm ~/.config/plasma-workspace/env/egpu-mode.sh && sudo systemctl restart sddm`。

### 打包成 AppImage

`tools/egpu-linux/appimage/build-appimage.sh` 用 [python-appimage](https://github.com/niess/python-appimage) 1.4.7，把 `egpu-mode`、Python 3.12 和 PyQt6 包成一個檔案。在 repo 根目錄執行：

```bash
tools/egpu-linux/appimage/build-appimage.sh    # 輸出 dist/egpu-mode-x86_64.AppImage
```

需要 `python3`（含 `venv`）和網路，腳本會下載 python-appimage、Python 基底 AppImage、appimagetool 和 PyQt6。推送 `v` 開頭的 tag 時，GitHub Actions 的 Linux runner 會跑同一支腳本，見[首頁的發行新版本](../../README.md#發行新版本)。

| `appimage/egpu-mode/` 裡的檔案 | 用途 |
|---|---|
| `requirements.txt` | 固定 PyQt6 版本（6.12.1，內含 Qt 6.12.0） |
| `entrypoint.sh` | 用包在裡面的 Python 執行 `egpu-mode`，`-I` 讓它不讀 `PYTHON*` 變數和使用者的套件 |
| `egpu-mode.desktop`、`egpu-mode.png` | 名稱和圖示 |

打包時遇到的問題：

| 問題 | 處理 |
|---|---|
| `--extra-data` 會把後面所有參數都當成它的值，recipe 路徑被吃掉 | recipe 路徑放在 `--extra-data` 前面 |
| 輸出檔名取自 `.desktop` 的 `Name=`，`Name=eGPU mode` 時找不到 `eGPU mode-x86_64.AppImage` | `Name=egpu-mode` |
| 基底 AppImage 的 `.DirIcon` 指向已經刪掉的 `python.png`。appimagetool 會警告，並說之後的版本會拒絕打包 | 先用 `--no-packaging` 建出 AppDir，把 `.DirIcon` 改指向 `egpu-mode.png` 再打包 |
| AppImage 裡的 Python 會設定 `SSL_CERT_FILE`、`TCL_LIBRARY` 等變數，指向 AppImage 掛載的目錄。B 開的程式繼承這些變數的話，關掉 `egpu-mode` 後掛載消失，程式的 HTTPS 會壞掉 | `launch_on_egpu` 在 AppImage 裡執行時，先拿掉 AppImage 的變數和指向掛載目錄的變數 |

2026-11-16 在這台機器（Ubuntu 26.04、KDE Wayland）上驗證過：

- 檔案約 102 MB，PyQt6 帶著完整的 Qt。
- `status` 的輸出和直接執行原始碼完全相同，選 C 寫入的檔案也相同。
- 在 Wayland 上正常載入 Qt 的 `wayland` plugin，畫面和系統的 PyQt6 一樣。
- B 開的程式拿不到 AppImage 的變數。直接執行原始碼時，使用者自己設的 `SSL_CERT_FILE` 會保留。
- 沒有 FUSE 時，設 `APPIMAGE_EXTRACT_AND_RUN=1` 也能執行。

### 桌面卡住時

先看有沒有程式卡在 D state：

```bash
ps -eo pid,stat,wchan:30,cmd | awk '$2 ~ /^D/'
```

如果 `kwin_wayland`、`Xwayland` 或 `systemd-logind` 卡在 D state，只能重開機。先試 `systemctl reboot`。超過 2 分鐘沒反應，就依序按 `Alt+SysRq+S`、`U`、`B`（`/proc/sys/kernel/sysrq` 是 176，允許 sync、remount 和 reboot）。Chrome 的 profile 不用清，`SingletonLock` 重開機後會自己處理。

## 故障排除紀錄

每個故障對應哪個原始 log，見 [evidence/README.md](evidence/README.md#各檔案內容)。當天的經過，時間往右走：

```text
時間 ─────────────────────────────────────────────────────────────►
08:41  BAR1 can't assign; no space, nouveau -12                    故障 1
09:11  first BAR1 fix fails (resource1_resize EINVAL)
09:22  nvidia-driver-595-open installed
09:23  BAR1 fix via setpci + rescan works
09:28  CmpltTO -> GSP timeout -> Xid 175/154, desktop hangs        故障 2
09:43  egpu-fix.sh: CLx off, Gen3 cap
09:53  white sparkles, 10:03 GPU falls off the bus (Xid 79)        故障 3
10:14  C2/C3 off test: 0 CmpltTO in 12 min
10:31  100Hz stutter: PCIe stuck at Gen1, ~810 MB/s                故障 4
10:36  71-egpu-cstate.rules installed, hot unplug freezes KWin     故障 5
10:45  memory clock locked -> Gen3, ~1860 MB/s
10:49  72-egpu-clocks.rules installed and verified
11:04  TestUFO at 100Hz: 50 fps, KWin can't init EGL on the 5090   故障 6
11:20  re-login: KWin gets the 5090, ~74 fps
11:48  mode C login 10 s after logout: portal hangs, terminal won't open
11:49  reboot with the eGPU attached: all three fixes run at boot
11:54  mode C: TestUFO 100 fps, rxpci ~6-20 MB/s
20:06  A button: back to Intel compositing, re-login after 8 s
20:11  B button: glxgears runs on the 5090
20:13  C button: re-login after 11 s, terminal opens fine
```

### 1. 驅動載不起來：BAR1 放不下（08:41）

**症狀：** kernel log 出現下面這些訊息，nouveau probe 失敗，GPU 沒有驅動可用。

```text
pci 0000:03:00.0: BAR 1 [mem size 0x800000000 64bit pref]: can't assign; no space
nouveau 0000:03:00.0: bar: one-time init failed, -12
```

**原因：** GPU 一出現就要求 32 GB 的 BAR1。BIOS 給 TRP0 的區段也剛好 32 GB，但起點沒有對齊 32 GB 的邊界。這就像一台 32 公尺長的車，只能停在每 32 公尺畫一格的停車線上，空間夠長，停車線卻對不上：

```text
address (GB)  160   160.25                             192   192.25
               |------|========= TRP0 window 32 GB =======|------|
               ^ 32 GB aligned                            ^ next 32 GB aligned

BAR1 = 32 GB   must start at 160 or 192: 160 is outside, only 0.25 GB after 192   ✘
BAR1 = 256 MB  fits at the start of the window                                    ✔
```

**第一版修法為什麼失敗：** 寫 `resource1_resize` 回傳 `EINVAL`（09:11:56）。kernel v7.0 的 `reset_resource()` 會把分配失敗的 BAR 連類型一起清掉，kernel 判斷不出 BAR1 是什麼，就拒絕調整。

**修法：** `egpu-bar1` 用 `setpci` 直接把 ReBAR 控制暫存器設成 256 MB，移除 `01:00.0` 後重新掃描 PCI，kernel 就會重新分配一次。它也把 `02:00.0` 的連線設成 Gen3，並關掉硬體自動變速。`70-egpu-bar1.rules` 會在 `80-drivers.rules` 載入驅動之前執行它。09:23:22 第一次成功。

[Windows 分析報告](../windows/egpu-intermittent-detection-report.md#為什麼-gpu-裝不下已驗證resizable-bar)推論 Windows 是驅動啟動後才把 BAR1 放大，但 Linux 的紀錄顯示，GPU 一出現、還沒有任何驅動時，BAR1 就已經是 32 GB。那份報告的推論沒有改，只加了指回這裡的註記。

### 2. GPU 韌體當掉，桌面和 Chrome 一起卡住（09:28）

**症狀：** 登入 KDE 後輸入法失效。之後 Chrome 打不開，視窗一直沒出現。

**經過：** GPU 啟用後約 48 秒開始出錯，接著鎖住 kernel 的 DRM。

```text
09:28:06  egpu-bar1: BAR1 set to 256 MB
09:28:54  CmpltTO (first of several)
09:29:03  GSP heartbeat timed out
09:29:07  Xid 175 (kwin_wayland waits 10 s for a GSP reply)
09:29:08  Xid 154: GPU Reset Required
          -> (sd-close) holds a DRM lock in drm_fb_release, waiting on the dead GPU
          -> Xwayland stuck in drm_release, logind and Chrome stuck in drm_open
```

Xwayland 卡住，依賴它的 fcitx5 輸入法就失效。Chrome 啟動時會打開 NVIDIA 的 DRM 裝置，所以也卡在 D state。Chrome 的 profile 沒有壞。

**處理：** 只能重開機。

**當時加的對策（`egpu-fix.sh`，09:43:50）：** `thunderbolt clx=0` 關掉 Thunderbolt 連線省電，`RMPcieLinkSpeed=0x800002a0` 把 GPU 連線上限設成 Gen3。09:51 重開機後確認生效（`clx` 是 `N`，`RegistryDwords` 有載入）。這兩項單獨使用還不夠，見故障 3。

### 3. 白色花點與 GPU 掉線：CPU 深度省電造成的逾時（09:53–10:26）

**症狀：** 外接螢幕偶爾出現一群白色小點，要等那一塊重畫才會消失。10:03 外接螢幕黑掉。

**證據：**

- 09:53:27 到 09:56:00 之間有 19 次 `CmpltTO`。
- 10:03:43 到 10:03:48 連續 5 次 `CmpltTO`，10:03:49 AER Fatal（`Inaccessible`），10:03:50 出現 Xid 79（GPU has fallen off the bus）和 Xid 154。
- 花點會停在畫面上。HDMI 訊號雜訊只會一閃就消失；會停住，代表錯誤的資料被寫進了 GPU 的畫面緩衝區，也就是 GPU 讀記憶體逾時、拿到錯誤資料。

**原因：** CPU 睡得越沉，叫醒越久。`C3_ACPI` 要 1048 µs 才醒（`C2_ACPI` 是 127 µs）。GPU 的逾時設定是預設的 `50us to 50ms`，實際值由 GPU 自己決定。CPU 還沒醒，GPU 就等不下去了。

**驗證：** 關掉 C2/C3 之後，10:14:15 到 10:26:27 共 12 分鐘沒有任何 `CmpltTO`。實際用了約 15 分鐘，期間還把主螢幕切到 eGPU，沒有花點也沒有黑屏。

**修法：** `71-egpu-cstate.rules` 插上 eGPU 時跑 `egpu-cstate limit`，拔掉時跑 `egpu-cstate restore`。代價是接著 eGPU 時筆電比較耗電、比較熱。

**另一個沒採用的做法：** 把 GPU 的 completion timeout 調長。GPU 的 `DevCap2` 是 `Range AB, TimeoutDis+`，最長可設到 65–210 ms（`DevCtl2` 欄位值 `0x6`）。這個做法可以不犧牲省電，但還沒驗證有效，最後選了已經證實有效的 C-state 方案。

### 4. 100Hz 卡頓：PCIe 停在 Gen1（10:29–10:49）

**症狀：** 外接螢幕設成 100Hz 時捲動不順。

**原因：** GPU 以為自己閒著，留在 P8（180 MHz、使用率 0%），PCIe 也跟著降到 Gen1，像高速公路只開一個車道。外接螢幕的畫面資料是從筆電記憶體送進 GPU 的，這種工作不算在 GPU 使用率裡。

**量測（`nvidia-smi dmon -s t` 的 `rxpci`）：**

| 狀態 | P-state / PCIe | 捲動時收資料速度 |
|---|---|---|
| 10:29–10:34，沒鎖時脈 | P8 / Gen1 | 卡在 809–833 MB/s |
| 只鎖核心時脈 `-lgc 1000,3090` | P5 / Gen2 | 沒有量捲動 |
| 鎖記憶體時脈 `-lmc 9001,14001`，10:45–10:49 | P4 / Gen3 | 中位數 1860 MB/s、p90 2098 MB/s、最高 2396 MB/s |

這次觀察到 GPU 的檔位跟著記憶體時脈走，所以只鎖核心時脈不夠。鎖了記憶體時脈之後，實際捲動明顯變順。

**修法：** `72-egpu-clocks.rules` 在 NVIDIA 驅動接上 GPU 時，啟動 `egpu-clocks.service`，由它執行 `egpu-clocks`：先開 persistence mode，再鎖記憶體時脈。`nvidia-smi` 要等驅動完全載入才能用，所以交給 service 執行，不放在 udev 事件裡直接跑。10:49 模擬 bind 事件驗證：P4、Gen3、記憶體 9001 MHz、核心待機 180 MHz、15.72 W。

### 5. 熱拔 eGPU 讓整個桌面卡死（10:36）

**症狀：** 拔掉 eGPU 後筆電螢幕也不動了。再插回去只聽到 USB 連接的提示音，外接螢幕不亮。

**原因：** 10:36:53 拔線後，NVIDIA 驅動要移除 GPU，但 KWin 還在用 eGPU 的輸出，兩邊互相等。`kwin_wayland` 和兩個 kworker 卡在 D state 超過 80 秒，`03:00.0` 一直沒有從系統移除。KWin 管所有螢幕，所以筆電螢幕也跟著不動。插回去時，只有盒子裡的 USB hub 重新連上。

**處理：** 重開機。以後一律先關機再拔。NVIDIA 的 eGPU 文件也提到，使用中拔掉 eGPU 不保證穩定。

### 6. 100Hz 只剩 50 fps：KWin 比 5090 早啟動（11:04）

**症狀：** 外接螢幕設成 100Hz，TestUFO 固定只有 50 fps。nvidia-settings 顯示 GPU 使用率 0%。

**原因：** KWin 在 10:39:38 啟動，那時 eGPU 還沒插，kernel 記錄 `NVRM: No NVIDIA GPU found`。10:42:04 驅動接上 5090 之後，KWin 對 5090 做 `eglInitialize` 一直失敗（`EGL_NOT_INITIALIZED`），10:42:04 到 11:20:14 共 40 次，都是 10:39 啟動的那個 KWin（pid 2722）。KWin 只好改用 CPU 把每格畫面寫進 5090。一格 29.5 MB，以約 2 GB/s 推算要 15 ms，趕不上 100Hz 的 10 ms。螢幕不支援 VRR，所以每兩次 vblank 才換一格。

**證據：**

| 檢查 | 結果 |
|---|---|
| TestUFO 跑的時候 | `rxpci` 穩定約 1980 MB/s，`sm` 0% |
| 新開的程式 | `eglinfo -p gbm` 能在 5090 上初始化，所以驅動和套件沒問題 |
| 11:20:27 重新登入後 | 新的 KWin 沒有 EGL 錯誤，`nvidia-smi` 列出 `kwin_wayland`，TestUFO 約 74 fps，GPU 使用率約 40% |

**處理：** 插上 eGPU 後登出再登入。重新登入後還是到不了 100 fps，瓶頸還沒確認，見下一節。`egpu-mode` 的 C 模式避開了「每格完整畫面都經過 TB4」這一段，11:54 實測穩定 100 fps，見[切換使用方式](#切換使用方式egpu-mode)。

## 還沒驗證和還沒試過的

還沒驗證的：

- 重新登入後 100Hz 只有約 74 fps，瓶頸是 TB4 頻寬，還是 Intel 和 5090 之間的接力。要在 TestUFO 持續跑的時候，同時量 `rxpci` 和 Intel 內顯的忙碌程度（`/sys/class/drm/card0/device/tile0/gt0/gtidle/idle_residency_ms`）。
- `egpu-mode` 的 C 模式長時間使用的穩定性，以及 C 登入失敗時自動切回 A 的機制（只用模擬測過）。
- `egpu-mode` 的 B 用 `glxgears` 驗證過（20:11），還沒用遊戲或 Blender 測過。

- 登入後才插上 eGPU 時，`egpu-clocks` 能不能自動套用。11:49 接著 eGPU 開機時，三個自動修正都在開機過程中套用了，但「登入後才插上」的實際 bind 事件還沒驗證。
- 拔掉 eGPU 時的 `egpu-cstate restore`。熱拔會卡死，所以沒機會跑到；不過重開機後 C2/C3 本來就是開啟的。
- 先在顯示設定關掉 eGPU 輸出再熱拔，是否就不會卡死。
- 用四支安裝腳本從頭整套重裝。
- GitHub Actions 打包 AppImage 和發行 Release 還沒實際跑過，第一次推送 tag 時要確認。AppImage 也只在這台機器上測過。

如果還是不穩，可以試的方向：

| 方向 | 說明 |
|---|---|
| 調長 GPU completion timeout | 見故障 3，`DevCtl2` 設成 `0x6`（65–210 ms） |
| `pcie_port_pm=off` | 開機參數，避免 PCIe port 進入省電狀態 |
| `NVreg_DynamicPowerManagement=0` | 目前是 `3`，有人在 RTX 5080 加 TB5 的穩定設定裡用 `0` |
| `iommu=pt` | 會降低 Thunderbolt 的 DMA 保護。我推測 Thunderbolt 外接裝置還是會被強制做位址轉換，所以這個參數可能沒效果，但沒有驗證 |

## 健康檢查指令

| 想確認什麼 | 指令 | 正常結果 |
|---|---|---|
| 三個自動修正有沒有跑 | `journalctl -b -t egpu-bar1 -t egpu-cstate -t egpu-clocks` | 有 `BAR1 set to 256 MB`、最後一筆 `limit`、`P4, 3, 9001 MHz` |
| GPU 檔位和 PCIe 速度 | `nvidia-smi --query-gpu=pstate,pcie.link.gen.current,clocks.mem,power.draw --format=csv` | `P4`、`3`、`9001 MHz` |
| 有沒有 completion timeout | `grep CmpltTO /sys/bus/pci/devices/0000:03:00.0/aer_dev_nonfatal` | `CmpltTO 0` |
| 有沒有 GPU 錯誤 | `journalctl -k -b \| grep -E "CmpltTO\|Xid"` | 沒有輸出 |
| C2/C3 狀態 | `cat /sys/devices/system/cpu/cpu0/cpuidle/state*/disable` | 接著 eGPU 時是 `0 0 1 1` |
| Thunderbolt CLx | `cat /sys/module/thunderbolt/parameters/clx` | `N` |
| NVIDIA 連線上限設定 | `grep RegistryDwords /proc/driver/nvidia/params` | 有 `RMPcieLinkSpeed=0x800002a0` |
| 即時 PCIe 流量 | `nvidia-smi dmon -s t` | 捲動時 `rxpci` 明顯超過 830 MB/s |
| 有沒有程式卡死 | `ps -eo pid,stat,wchan:30,cmd \| awk '$2 ~ /^D/'` | 沒有輸出 |
| KWin 有沒有接上 5090 | `nvidia-smi \| grep kwin_wayland` | 有一行 `kwin_wayland`。沒有的話，登出再登入 |
| 目前的使用方式 | `./tools/egpu-linux/egpu-mode status` 或 `./egpu-mode-x86_64.AppImage status` | 沒有 `⚠` 開頭的那一行 |

## 參考資料

- [NVIDIA README 570.169: Configuring External and Removable GPUs](https://download.nvidia.com/XFree86/Linux-x86_64/570.169/README/egpu.html)：使用中拔掉 eGPU 不保證穩定
- [Linux kernel ABI: sysfs-bus-pci](https://www.kernel.org/doc/Documentation/ABI/testing/sysfs-bus-pci)：`resourceN_resize` 的定義
- [Linux v7.0 `drivers/pci`](https://github.com/torvalds/linux/tree/v7.0/drivers/pci)：`reset_resource()`、`rebar.c`、`pci-sysfs.c`
- [NVIDIA open-gpu-kernel-modules 595.99.02 `nvrm_registry.h`](https://github.com/NVIDIA/open-gpu-kernel-modules/blob/595.99.02/src/nvidia/interface/nvrm_registry.h)：`RMPcieLinkSpeed` 的欄位定義
- [RTX 5080 eGPU (USB4/Thunderbolt) on Ubuntu: cuCtxCreate hangs](https://forums.developer.nvidia.com/t/rtx-5080-egpu-usb4-thunderbolt-on-ubuntu-24-04-cuctxcreate-returns-cuda-error-unknown-or-hangs-while-nvidia-smi-works/379169)：同款 `8086:5786` bridge，固定 Gen3 後穩定
- [Working configuration: RTX 5080 + Razer Core X V2 (Thunderbolt 5)](https://forums.developer.nvidia.com/t/working-configuration-rtx-5080-razer-core-x-v2-thunderbolt-5-on-ubuntu-24-04-kernel-6-17-driver-590-48-01-open/366919)：`thunderbolt.clx=0`、`pcie_port_pm=off`、`NVreg_DynamicPowerManagement=0x00` 等設定，作者也說熱插拔不可靠
- [RTX 5060 Ti eGPU: CUDA hard-lock on Linux via Thunderbolt 4](https://forums.developer.nvidia.com/t/rtx-5060-ti-egpu-aorus-ai-box-cuda-hard-lock-on-linux-via-thunderbolt-4/365386)：關 ASPM 和 IOMMU 都沒解決的案例
- [RTX 5060 Ti GSP RPC failure / Xid 79 over Thunderbolt](https://forums.developer.nvidia.com/t/rtx-5060-ti-gsp-rpc-failure-xid-79-over-thunderbolt-egpu-enclosure/379593)：同類型的 GSP 逾時
- [LKML: Thunderbolt 5 PCIe completion timeout](https://lkml.iu.edu/2604.0/04360.html)：建議用 `pcie_port_pm=off` 取代 `pcie_aspm=off`
- [Kernel null pointer dereference in nvidia_modeset during Thunderbolt dock disconnect](https://forums.developer.nvidia.com/t/kernel-null-pointer-dereference-in-nvidia-modeset-during-thunderbolt-dock-disconnect/359280)：拔除時當機的回報
- 本機資料：`journalctl`（2026-11-16 各次開機）、`/var/log/apt/history.log`、`/var/log/egpu-fix.log`、`lspci -vvv`、`nvidia-smi`、`/sys/devices/system/cpu/cpu0/cpuidle/`
- 當天的原始資料（已去掉主機名稱和帳號）：[evidence/](evidence/README.md)
