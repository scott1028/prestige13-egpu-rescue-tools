# egpu-connect 使用指南

這份指南寫給想知道 `egpu-connect` 每一步在做什麼、失敗時該看哪裡的人。只想快點用，看 [README](../README.md) 就夠了。

工具只是繞過問題。真正的根因在 BIOS 預留的位址空間太小，細節見[分析報告](egpu-intermittent-detection-report.md)。

## 先認識幾個名詞

| 名詞 | 白話解釋 |
|---|---|
| eGPU | 外接顯示卡。這裡指 ASUS ROG XG Mobile GC34X，裡面是 RTX 5090 Laptop GPU |
| USB4 Router | eGPU 端的 USB4 / Thunderbolt 控制器，裝置管理員顯示為 `USB4 Router (2.0)`。它出現代表線路接通了，GPU 還在後面 |
| MMIO 位址空間 | 系統分給 PCIe 裝置的記憶體位址範圍。GPU 要先拿到夠大的一塊才能啟動 |
| rebalance | Windows 重新分配 PCIe 位址空間。範圍是整個 PCI root 時，要先停掉再重啟 17 個內建裝置 |
| veto（否決） | rebalance 時有裝置不肯停下來，整次 rebalance 就失敗 |
| Code 12 | 裝置管理員的錯誤碼，意思是裝置找不到足夠的可用資源 |
| Remote Desktop Device Redirector Bus | 遠端桌面用來轉送印表機、USB 裝置的虛擬匯流排，ID 是 `ROOT\RDPBUS\0000`。第二階段的 rebalance 全部 43 次都是它擋掉的 |

## 執行前先看狀態

`-DryRun` 只讀狀態，不改任何東西，也不需要系統管理員權限：

```bat
tools\egpu-connect\egpu-connect.cmd -DryRun
```

畫面會列出這幾項：

- 系統管理員權限：`True` 或 `False`
- Remote Desktop Device Redirector Bus：`找不到`、`已停用`，或 `啟用中（會被停用）`
- 會暫停的服務：目前正在跑、正式執行時會停掉的那些
- 會結束的 MSI 程式
- eGPU：目前狀態，文字和[第 3 步的狀態表](#3-拔插-egpu等-gpu-穩定)相同

## 執行時發生什麼

正式執行分四步，前三步不管成功或出錯，最後都會走到第 4 步恢復。

```text
[1/4] 停用 Remote Desktop Device Redirector Bus（只需做一次）
  │
  ▼
[2/4] 暫停會 veto 的服務、結束 MSI 程式（開始沒有聲音）
  │
  ▼
[3/4] 你拔掉 eGPU → 插回同一個 port → 工具等 GPU 連續正常 15 秒
  │
  ▼
[4/4] 依相反順序啟動服務、重新開啟 MSI 程式（聲音恢復）
```

例外是第 2 步本身出錯，見[幾種特殊情況](#幾種特殊情況)。

### 1. 停用 Remote Desktop Device Redirector Bus

工具用 `Disable-PnpDevice` 停用 `ROOT\RDPBUS\0000`，這是永久設定。

- 已經停用（錯誤碼 22）就顯示「已是停用狀態」；找不到這個裝置就略過。
- RDP 驅動程式有時不肯立刻放手，工具會顯示警告然後繼續。這種情況通常重開機後才生效。
- 要改回來：在系統管理員 PowerShell 執行 `Enable-PnpDevice -InstanceId 'ROOT\RDPBUS\0000'`。

### 2. 暫停服務、結束 MSI 程式

這一步針對報告裡另外兩種 veto：`ACPI\PNP0C14\0`（28 次，推測和 MSI 的服務有關，未確認）和 Realtek 內建音效（9 次）。

工具依下表順序停掉正在跑的服務，沒在跑的跳過：

| 順序 | 服務 |
|---|---|
| 1 | `MSI_Center_S_Service` |
| 2 | `MSI Foundation Service` |
| 3 | `RtkAudioUniversalService` |
| 4 | `IntelAudioService` |
| 5 | `IgoAudioService` |
| 6 | `AudioEndpointBuilder` |

`AudioEndpointBuilder` 用 `-Force` 停，會一起停掉 `Audiosrv` 和其他正在跑的相依服務。工具也記下這些相依服務，第 4 步一併恢復。

接著用 `Stop-Process -Force` 結束名稱符合 `^(MSI_Center|MSI_OLED|MSI_S_LAN|MSIAPService)` 的程式。
結束前，工具先記下在你的工作階段裡跑的那幾支（`SessionId` 不是 0），因為服務重啟時不會幫你把它們開回來。

### 3. 拔插 eGPU，等 GPU 穩定

工具先看 eGPU 有沒有接著，再決定要你拔還是插：

- 偵測得到：請你拔線，每 1 秒檢查一次，最多等 120 秒。時間到還偵測得到，就顯示「仍偵測到 eGPU，直接繼續」。拔掉後等約 5 秒，插回同一個 port。
- 偵測不到：直接請你插上。

然後每 2 秒檢查一次，最多等 180 秒，狀態改變時印出一行：

| 畫面文字 | 意思 |
|---|---|
| `未連接` | USB4 Router 和 GPU 都找不到 |
| `USB4 Router 已出現，GPU 尚未出現` | 線路接通了，GPU 還在等位址 |
| `GPU 正常` | GPU 的錯誤碼是 0 |
| `GPU 異常（Code N）` | GPU 出現了但有錯誤碼，例如 Code 12 |

GPU 要連續 15 秒都是「GPU 正常」才算成功，中間一掉就重新計時。
原因是整個 root 的 rebalance 成功後，75 次裡有 74 次會在 2~3 秒內把 USB4 Router 拆掉，要等它重連才真的能用。

### 4. 恢復服務與程式

- 服務依停止時的相反順序啟動，第 2 步記下的相依服務也包含在內。哪個服務啟動失敗，工具會顯示「無法啟動服務 …」並繼續下一個。
- 等 5 秒讓服務自己帶起程式，再重新開啟第 2 步記下、而且還沒在跑的程式。
- 程式透過 `explorer.exe` 開啟，所以會以一般權限跑在你的工作階段，不會變成系統管理員權限。

## 跑完之後看哪裡

視窗會停在「按 Enter 結束」，方便你看完結果：

| 結果 | 畫面 | 結束代碼 |
|---|---|---|
| 成功 | 綠色「eGPU 已就緒。」 | 0 |
| 逾時 | 黃色「在 180 秒內沒有等到 GPU 正常（最後狀態：…）。」 | 1 |
| 出錯 | 黃色「執行中斷：…」 | 1 |

失敗時另外會提示「可以再執行一次；仍失敗的話，先插好 eGPU 再重開機。」
不管結果如何，最後都會提醒：使用中途若出現 Code 12（NVIDIA CoInstaller 觸發），此工具無法預防。

每次正式執行都會用 `Start-Transcript` 寫一份紀錄到 `tools/egpu-connect/logs/egpu-connect-yyyyMMdd-HHmmss.log`；`-DryRun` 不寫紀錄。

## 幾種特殊情況

1. **第 2 步出錯時服務不會自動恢復。** 停服務的途中一出錯，工具就沒有拿到「停了哪些」的清單，第 4 步會整個跳過。這時重開機，或用 `Start-Service` 手動啟動上面那些服務。
2. **結束代碼只在已經是系統管理員時有意義。** 從一般視窗執行時，工具會另開一個系統管理員視窗，原本的視窗馬上以 0 結束。
3. **用到一半的 Code 12 擋不住。** 連上 5~20 分鐘後，Windows 的 Device Setup Manager 會執行 NVIDIA CoInstaller（`nvdispco64.exe`），GPU 重啟後要再 rebalance 一次。這次 eGPU 自己的 HD Audio 否決了它，於是出現 Code 12 / `0xC0000018` 並要求重開機。

## 改給別的 eGPU 或機器用

設定都在 `egpu-connect.ps1` 開頭，直接改變數：

| 變數 | 目前的值 | 用途 |
|---|---|---|
| `$RouterIdPattern` | `USB4\\VID_8087&PID_5786%` | GC34X 的 USB4 Router，WQL `LIKE` 樣式 |
| `$GpuIdPattern` | `PCI\\VEN_10DE&DEV_2C18%` | RTX 5090 Laptop GPU，WQL `LIKE` 樣式 |
| `$RdpBusId` | `ROOT\RDPBUS\0000` | 第 1 步要停用的裝置 |
| `$VetoServices` | 第 2 步表格裡的 6 個服務 | 要暫停的服務，依序停、反序啟動 |
| `$MsiProcessPattern` | `^(MSI_Center\|MSI_OLED\|MSI_S_LAN\|MSIAPService)` | 要結束的程式名稱，正規表示式 |
| `$UnplugTimeoutSec` | `120` | 等你拔線的秒數上限 |
| `$ConnectTimeoutSec` | `180` | 等 GPU 正常的秒數上限 |
| `$StableSec` | `15` | GPU 要連續正常幾秒才算成功 |

裝置 ID 可以在裝置管理員 → 裝置內容 → 詳細資料 → 硬體識別碼查到。
