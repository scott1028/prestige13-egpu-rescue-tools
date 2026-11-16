# eGPU 時有時無偵測分析報告

- 機器：MSI Prestige 13 AI+ Evo A2VMG（Core Ultra 288V），BIOS E13Q3IMS.112，Windows 11 build 26300
- eGPU：ASUS ROG XG Mobile GC34X（RTX 5090 Laptop）
- 分析日期：2026-11-15
- 事件時間軸：同目錄 `egpu-event-timeline.txt`

## 結論

問題不在線材或接觸不良，而是 **PCIe 資源（MMIO 位址空間）分配**：

1. BIOS 給 Thunderbolt root port 預留的空間裝不下 RTX 5090 Laptop GPU。原因是 GPU 開著 ReBAR，BAR1 要 32 GB（已用 `nvidia-smi` 證實），比 BIOS 能預留的上限還大。所以每次接上 eGPU，Windows 都要對整個 PCI root 做一次資源重新分配（rebalance），先停掉 **17 個內建裝置**（Wi‑Fi、ME、ISH、音效、USB4 Host Router 等），搬完位址再重啟。
2. 這個 rebalance 只要有一個裝置「否決」（veto），整個就失敗，eGPU 會偵測不到或出現 Code 12。會否決的有：被某個 service 開著的 ACPI WMI 裝置、內建 Realtek 音效、eGPU 自己的 HDMI 音效、Remote Desktop bus。
3. 就算 rebalance 成功，**75 次裡有 74 次**會在 2~3 秒內把 USB4 Router 和 TBT 通道一起拆掉，eGPU 消失。要等它重新連上、而且走的是同一個已經擴大過空間的 port，才真正能用。

所以看起來很隨機：結果取決於當下哪個 service 正抓著裝置、你插哪個 port、插拔的時機。重開機比較容易成功，是因為開機初期 service 還沒抓住那些裝置，不會否決。

## 證據

### 環境（從系統讀到的）

- eGPU：ASUS ROG XG Mobile **GC34X**（RTX 5090 Laptop，Barlow Ridge TB5 控制器，裝置名 `USB4 Router (2.0)`）
- 筆電端：Lunar Lake 內建 `USB4 Root Router (1.0)`，由 Windows 內建的 connection manager 管理。這兩個 port 是 **USB4 v1 / TB4 40G，不是 TB5**；GC34X 是以 40G 模式運作。
- Port 1 → `TRP0 (00:07.0)`，Port 3 → `TRP1 (00:07.1)`

### 事件紀錄統計（2025-11-08 ~ 2026-11-15）

| 現象 | 次數 |
|---|---|
| 整個 root 的 rebalance 成功（停掉再重啟 17 個裝置） | 75 |
| 　└ 成功後 2~3 秒內 USB4 Router 被判定 failing 而拆掉 | **74** |
| 整個 root 的 rebalance 被否決而失敗 | 36 |
| 單一 port 的 rebalance 成功（這時 GPU 才真的能用） | 32 |
| 使用中途變成 Code 12、要求重開機 | 11 |
| 換 port 時 GPU 舊的裝置紀錄還沒移除，造成重複（809） | 11 |
| 拔除時 GPU 被程式佔用，移除被否決 | 17 |

### 誰否決了 rebalance

| 裝置 | 次數 | 說明 |
|---|---|---|
| `ROOT\RDPBUS` | 43 | Remote Desktop 匯流排，第二階段的 rebalance 每次都被它擋掉 |
| `ACPI\PNP0C14\0` | 28 | ACPI WMI 裝置，被某個 Windows service 開著（推測是 MSI Center S / MSI Foundation / Intel DTT / IPF 其中之一，**未確認**） |
| Realtek 內建音效 | 9 | 音效 service 正在使用 |
| eGPU 的 NVIDIA HD Audio | 6 | 只出現在使用中途那類 Code 12 |

### 目前 TBT root port 的位址空間（eGPU 未接）

| Root port | MMIO 區段 | 64-bit prefetchable 區段 |
|---|---|---|
| TRP0 `00:07.0` | 96 MB（`0x88000000–0x8DFFFFFF`） | 工具看不到（見「未驗證項目」） |
| TRP1 `00:07.1` | 96 MB（`0x82000000–0x87FFFFFF`） | 工具看不到（見「未驗證項目」） |

### eGPU 接上時的實測（2026-11-15）

| 項目 | 實測值 | 來源 |
|---|---|---|
| 顯存（FB Memory Total） | 24463 MiB | `nvidia-smi -q -d MEMORY`（驅動程式 621.24） |
| BAR1（BAR1 Memory Usage Total） | **32768 MiB** | 同上 |
| BAR0 | 64 MB（`0xB8000000–0xBBFFFFFF`） | WMI `Win32_DeviceMemoryAddress` |
| BAR3 | 32 MB（`0x300E000000–0x300FFFFFFF`） | 同上 |
| TRP0 的 MMIO 區段 | 不在 BIOS 原本給的 `0x88000000–0x8DFFFFFF`，WMI 只列出 `0xB8000000–0xBBFFFFFF` | 同上 |

TRP0 的區段被 rebalance 搬過。WMI 列出的區段不完整：eGPU 的 HD Audio 在 `0xBC0FC000`，落在那段外面。

### BIOS（E13Q3IMS.112，從 Setup 選單資料解出的預設值）

| 選項 | 預設值 |
|---|---|
| ITBT Root Port 0/1 Reserved Memory | **96 MB**（和現在實測的 96 MB 完全吻合） |
| Reserved PMemory | 32768 MB，對齊 2^28；這個選項最大就是 32 GB，對齊最大只能設到 2 GB |
| Extra Bus Reserved | 42 |
| Os Native Resource Balance | **Disabled** |
| USB4 CM Mode | OS Dependent |
| PCIE Resizable BAR Support | Enabled（隱藏選項） |

## 機制

```text
Time ─────────────────────────────────────────────────────────────────────►

User         | 插入 eGPU      |                         | 等待 / 再插一次      |
-------------|----------------|-------------------------|----------------------|
USB4 CM      | 建立 PCIe 通道 |                         | 通道被拆 → 重新建立  |
BIOS 預留    | 96MB + 32GB    | 裝不下 GPU              |                      |
Windows PnP  | GPU 出現       | 整個 root rebalance     | 只需單一 port 調整   |
             |                | 停掉並重啟 17 個裝置    | → GPU 正常 ✔         |
Services     |                | 有人否決 → 失敗 ✘       |                      |
             |                | 沒人否決 → 成功，但     |                      |
             |                | USB4 Router 被拆 ✘      |                      |
```

**使用中途的 Code 12**：連上 5~20 分鐘後，Windows 的 Device Setup Manager 會執行 NVIDIA CoInstaller（`nvdispco64.exe`），GPU 因此重啟並需要再做一次 rebalance。這次被 eGPU 自己的 HD Audio 否決，於是出現 Code 12 / `0xC0000018` 並要求重開機。我逐筆核對了 11/03 和 08/10 兩次，其餘幾次的事件型態相同。

## 為什麼 GPU 裝不下（已驗證：Resizable BAR）

24 GB 顯存開了 ReBAR 後，BAR1 是 32 GB。BIOS 替 TBT root port 預留的可預取空間放不下它，只能整個 root 搬家。

| | GPU 需要（可預取） | BIOS 能給的上限 |
|---|---|---|
| 大小 | BAR1 32 GB + BAR3 32 MB | Reserved PMemory 最多 32 GB |
| 起點對齊 | BAR1 要對齊 32 GB | 最多 2 GB（預設 2^28 = 256 MB） |

理論上只有預留區段剛好從 32 GB 的倍數開始、而且 BAR3 被放到別的區段時才放得下；事件記錄顯示這台電腦每次插上都要 rebalance。
就算解鎖隱藏選單，把這兩個選項調到上限，還是放不下。
如果 BAR1 維持 256 MB，GPU 總共只要約 64 MB + 288 MB。Windows 是在顯示卡驅動程式啟動時，才把 BAR1 放大到最大值（reference06）。

> 2026-11-16 在 Linux 上看到不同的結果：GPU 一出現、還沒有任何驅動時，BAR1 就已經要 32 GB。用 `setpci` 把 BAR1 縮成 256 MB 後就放得下，見 [Linux guide 故障 1](../linux/guide.md#1-驅動載不起來bar1-放不下0841)。Windows 上沒有試過在驅動啟動前縮小 BAR1。

驗證方式：WMI 和 `pnputil` 都不會顯示 4 GB 以上的大型位址區段，所以改用 `nvidia-smi -q -d MEMORY`，讀到 BAR1 Total = 32768 MiB。

### 試過關閉 eGPU 的 ReBAR：沒有效果

NVIDIA 驅動程式 `nvlddmkm.sys`（621.24）裡有一個沒有公開文件的設定名稱 `FiltereGPUResizableBAR1`，和 `Usb4DisplayControl`、`EGPULockReOrderingWAR` 這些 eGPU 設定放在一起。

| 步驟 | 結果 |
|---|---|
| 在 GPU 的 driver key `HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0001` 寫入 DWORD 1 | 寫入成功 |
| 22:20:27 重新插上 eGPU，驅動程式重新啟動 | GPU 正常 |
| `nvidia-smi -q -d MEMORY` | BAR1 還是 32768 MiB |

這個位置和值沒有效果，實驗後已經刪掉。其他 registry 位置或值沒有試。

## 同一次開機再插同一個 port

一次開機裡，只有第一次插上需要整個 root 的 rebalance。之後插回同一個 port，只在那個 port 底下調整，又快又不會被否決。

2026-11-15 開機後的 Kernel-PnP 事件 1020（成功）和 1021（失敗）：

```text
Time ↓
20:54:51  開機
20:55:05  整個 root（ACPI\PNP0A08）rebalance 成功：18 個裝置，2920 毫秒
20:59:12  只在 TRP0 底下 rebalance 成功：6 個裝置，7 毫秒
22:05:04  只在 TRP0 底下 rebalance 成功：6 個裝置，8 毫秒
22:08:33  只在 TRP0 底下 rebalance 成功：6 個裝置，6 毫秒
22:20:27  只在 TRP0 底下 rebalance 成功：6 個裝置，7 毫秒
22:33:15  rebalance 失敗：第一階段被 eGPU 的 NVIDIA HD Audio 否決，第二階段被 ROOT\VMS_VSMP\0000 否決
22:33:36  只在 TRP0 底下 rebalance 成功：6 個裝置，6 毫秒
```

這支持「同一次開機都插同一個 port」的建議。

## NVIDIA「Disconnect GPU」會留下 Code 22

NVIDIA 視窗裡的「Disconnect GPU」會把 GPU 停用，插回去也不會恢復。2026-11-15 22:13 前後用它退出 eGPU 之後，GPU 一直是 Code 22。

| 位置 | 值 |
|---|---|
| `HKLM\SYSTEM\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_2C18&SUBSYS_225B1043&REV_A1\5EC18BAFDA2DB04800` 的 `ConfigFlags` | 0 |
| `HKLM\SYSTEM\CurrentControlSet\Hardware Profiles\Current\System\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_2C18&SUBSYS_225B1043&REV_A1\5EC18BAFDA2DB04800` 的 `CSConfigFlags` | **1** |

`Enable-PnpDevice` 只清 `ConfigFlags`，所以沒有用。把 `CSConfigFlags` 改回 0 再拔插就恢復了。egpu-tray 的「重新啟用 GPU」做的就是這件事，細節見 [guide](guide.md#gpu-被停用code-22時)。

## 建議測試（低風險 → 高風險，一次只改一項）

1. **停用 `Remote Desktop Device Redirector Bus`**（裝置管理員 → 系統裝置）。它擋掉了全部 43 次第二階段的 rebalance，eGPU 社群也常用這招處理 Code 12。代價是遠端桌面的裝置重導（印表機、USB 轉送）不能用，隨時可以改回來。
2. **插 eGPU 之前先關掉 MSI Center S、停止播放聲音**，排除 PNP0C14 和 Realtek 音效的否決。
3. **同一次開機都插同一個 port。** 如果 eGPU 消失，先等 1~2 分鐘讓它自己重連，不要馬上換到另一個 port。拔除前先關掉正在用 eGPU 的程式，不要用 NVIDIA 的「Disconnect GPU」（見上面）。
4. **BIOS 層級**：Os Native Resource Balance、Reserved Memory、PMemory 對齊這些選項都在隱藏選單裡。改它們需要解鎖 MSI 隱藏選單或改 NVRAM，有開不了機的風險。而且 Reserved PMemory 和對齊調到上限也放不下 32 GB 的 BAR1，Os Native Resource Balance 的效果則不知道。所以**不建議**動；比較實際的做法是拿這些證據回報給 MSI。

## 未驗證項目

- [v] ReBAR 是不是 GPU 裝不下的原因：是。eGPU 接上時 `nvidia-smi` 顯示 BAR1 為 32768 MiB
- [x] 關閉 eGPU 的 ReBAR：在 GPU driver key 寫入 `FiltereGPUResizableBAR1=1` 沒有效果，其他位置或值沒有試
- [ ] TBT root port 是否真的有 32 GB 的 prefetchable 區段（WMI / `pnputil` 不顯示大型區段，需在裝置管理員 → 檢視 → 依連線顯示資源 確認）
- [ ] 到底是哪個 service 開著 `ACPI\PNP0C14\0`
- [ ] BIOS 實際生效的值：讀的是選單預設值，NVRAM 裡的實際值沒讀；不過 96 MB 和實測一致
- [ ] 11/14 的三次異常重開：有一次 bugcheck `0x7E`、留下 2 個 minidump，WHEA 記錄是 Intel CrashLog。那天 eGPU 完全沒有連上的紀錄，所以和 eGPU 有沒有關係還不能判斷；讀 minidump 需要系統管理員權限
- [ ] 下載的 UEFIExtract / IFRExtractor 被自動模式的安全檢查擋下，沒有執行。BIOS 資料是用只依賴 Python 標準函式庫的自寫解析器解出

## 參考資料

- reference01: https://learn.microsoft.com/en-us/windows/win32/api/cfg/ne-cfg-pnp_veto_type （PNP_VETO_TYPE：veto 4 = WindowsService，1 = LegacyDevice）
- reference02: https://learn.microsoft.com/en-us/windows-hardware/drivers/install/cm-prob-normal-conflict （Code 12）
- reference03: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-erref/596a1078-e883-4972-9bbc-49e60bebca55 （NTSTATUS 0xC0000018）
- reference04: https://github.com/LongSoft/UEFITool/releases/tag/A76 、https://github.com/LongSoft/IFRExtractor-RS/releases/tag/v1.6.1 （已下載但未執行）
- reference05: 本機資料：Windows 事件記錄 Kernel-PnP/Device Management、System、DeviceSetupManager；BIOS 檔 `src/E13Q3IMS.112/E13Q3IMS.112`（Setup 模組的選單資料）；存取日期 2026-11-15
- reference06: https://learn.microsoft.com/en-us/windows-hardware/drivers/display/resizable-bar-support （WDDM v2 之後，Windows 會在韌體初始化之後重新協商 GPU 的 BAR 大小）
- reference07: 本機資料：`nvidia-smi -q -d MEMORY`、WMI `Win32_DeviceMemoryAddress`、`nvami.inf_amd64_f0c41c1943d1ce2d\nvlddmkm.sys` 的字串、Kernel-PnP 事件 1020 / 1021、registry `CSConfigFlags`；存取日期 2026-11-15
