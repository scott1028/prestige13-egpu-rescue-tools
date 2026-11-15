# eGPU 時有時無偵測分析報告

- 機器：MSI Prestige 13 AI+ Evo A2VMG（Core Ultra 288V），BIOS E13Q3IMS.112，Windows 11 build 26300
- eGPU：ASUS ROG XG Mobile GC34X（RTX 5090 Laptop）
- 分析日期：2026-11-15
- 事件時間軸：同目錄 `egpu-event-timeline.txt`

## 結論

問題不在線材或接觸不良，而是 **PCIe 資源（MMIO 位址空間）分配**：

1. BIOS 給 Thunderbolt root port 預留的空間裝不下 RTX 5090 Laptop GPU。所以每次接上 eGPU，Windows 都要對整個 PCI root 做一次資源重新分配（rebalance），先停掉 **17 個內建裝置**（Wi‑Fi、ME、ISH、音效、USB4 Host Router 等），搬完位址再重啟。
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

## 為什麼 GPU 裝不下（推測，尚未驗證）

最可能是 **Resizable BAR**：24 GB 顯存開了 ReBAR 後，BAR1 會要 32 GB，再加上 BAR3 的 32 MB 就超過 BIOS 能預留的 32 GB 上限，只能整個 root 搬家。

驗證方式：eGPU 正常運作時，打開裝置管理員 → GPU → 資源，看最大的記憶體區段是不是約 32 GB；或到 NVIDIA 控制面板 → 系統資訊看「Resizable BAR」。目前無法從這邊確認，因為 WMI 和 `pnputil` 都不會顯示 4 GB 以上的大型位址區段。

## 建議測試（低風險 → 高風險，一次只改一項）

1. **停用 `Remote Desktop Device Redirector Bus`**（裝置管理員 → 系統裝置）。它擋掉了全部 43 次第二階段的 rebalance，eGPU 社群也常用這招處理 Code 12。代價是遠端桌面的裝置重導（印表機、USB 轉送）不能用，隨時可以改回來。
2. **插 eGPU 之前先關掉 MSI Center S、停止播放聲音**，排除 PNP0C14 和 Realtek 音效的否決。
3. **同一次開機都插同一個 port。** 如果 eGPU 消失，先等 1~2 分鐘讓它自己重連，不要馬上換到另一個 port。拔除前先在 Armoury Crate 退出。
4. **BIOS 層級（真正的根因）**：Os Native Resource Balance、Reserved Memory、PMemory 對齊這些選項都在隱藏選單裡。改它們需要解鎖 MSI 隱藏選單或改 NVRAM，有開不了機的風險。在沒有救援方案之前**不建議**動；比較實際的做法是拿這些證據回報給 MSI。

## 未驗證項目

- [ ] ReBAR 是不是 GPU 裝不下的原因（要等 eGPU 正常時查看 BAR 大小）
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
