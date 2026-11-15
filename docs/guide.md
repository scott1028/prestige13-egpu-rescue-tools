# egpu-tray 與 egpu-connect 使用指南

這份指南寫給想知道 `egpu-tray.exe` 和 `egpu-connect` 每一步在做什麼、失敗時該看哪裡的人。只想快點用，看 [README](../README.md) 就夠了。

工具只是繞過問題。真正的根因是 GPU 開著 ReBAR 之後，要的位址空間比 BIOS 能預留的還大，細節見[分析報告](egpu-intermittent-detection-report.md)。

## 先認識幾個名詞

| 名詞 | 白話解釋 |
|---|---|
| eGPU | 外接顯示卡。這裡指 ASUS ROG XG Mobile GC34X，裡面是 RTX 5090 Laptop GPU |
| USB4 Router | eGPU 端的 USB4 / Thunderbolt 控制器，裝置管理員顯示為 `USB4 Router (2.0)`。它出現代表線路接通了，GPU 還在後面 |
| MMIO 位址空間 | 系統分給 PCIe 裝置的記憶體位址範圍。GPU 要先拿到夠大的一塊才能啟動 |
| BAR1 / ReBAR | BAR1 是 CPU 看 GPU 顯存用的位址窗口。ReBAR（Resizable BAR）開著時，Windows 會把它放大到 32 GB，比 BIOS 能替 Thunderbolt port 預留的上限還大 |
| rebalance | Windows 重新分配 PCIe 位址空間。範圍是整個 PCI root 時，要先停掉再重啟 17 個內建裝置 |
| veto（否決） | rebalance 時有裝置不肯停下來，整次 rebalance 就失敗 |
| Code 12 | 裝置管理員的錯誤碼，意思是裝置找不到足夠的可用資源 |
| Code 22 | 裝置管理員的錯誤碼，意思是裝置被停用了 |
| Remote Desktop Device Redirector Bus | 遠端桌面用來轉送印表機、USB 裝置的虛擬匯流排，ID 是 `ROOT\RDPBUS\0000`。第二階段的 rebalance 全部 43 次都是它擋掉的 |
| 登入排程 | Windows 工作排程器裡名為 `egpu-tray` 的工作。你登入時，它用系統管理員權限啟動工作列程式，所以不會跳 UAC |

## egpu-tray：常駐工作列

`egpu-tray.exe` 常駐在工作列右下角，每 5 秒看一次 eGPU 狀態。出問題時它會通知你，點一下就幫你啟動救援。

### 編譯和安裝

`tools\egpu-tray\build.cmd` 用 .NET Framework 4.8 內建的 `csc.exe`（`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）編譯 `src\*.cs`，輸出到 repo 根目錄的 `dist\egpu-tray.exe`。`dist/` 不會進 git。

編譯時會把 `tools\egpu-connect\egpu-connect.ps1` 嵌進 `.exe`。改過 `egpu-connect.ps1` 之後，要重新編譯、再按一次「安裝」，安裝資料夾裡的那份才會更新。

雙擊 `egpu-tray.exe` 會先跳 UAC，再出現有「安裝」「解除安裝」兩個按鈕的視窗。按「安裝」時，程式依序做這幾件事：

```text
結束舊的工作列程式 ──► 複製 egpu-tray.exe ──► 放出 egpu-connect.ps1 ──► 建立登入排程 ──► 立刻啟動工作列程式
```

檔案放在 `C:\Program Files\egpu-tray\`，egpu-connect 在裡面的 `egpu-connect\`。已經安裝過再按一次「安裝」，就是更新。

登入排程的設定：

| 設定 | 值 | 為什麼 |
|---|---|---|
| 名稱 | `egpu-tray` | 在工作排程器裡用這個名字找 |
| 觸發 | 你登入時，延遲 15 秒 | 等工作列先準備好 |
| 權限 | `InteractiveToken`、`HighestAvailable` | 在你的桌面上用系統管理員權限執行，不跳 UAC |
| 電池 | 用電池時也會啟動，拔掉電源也不會停 | 筆電常常用電池 |
| 執行時間上限 | 沒有 | 要一直常駐 |
| 動作 | `egpu-tray.exe --tray` | `--tray` 是工作列模式；不帶參數就是安裝視窗 |

### 發行新版本

你推送 `v` 開頭的 tag 時，`.github/workflows/release.yml` 會在 GitHub 的 Windows runner 上跑同一支 `build.cmd`，再建立同名的 Release，附上 `egpu-tray.exe` 讓人下載。

```text
你推送 tag v1.0.0 ──► GitHub Actions 編譯 ──► 建立 Release v1.0.0，附上 egpu-tray.exe
```

Release 說明由 GitHub 依 commit 自動產生（`gh release create --generate-notes`）。同一個 tag 已經有 Release 時，建立會失敗，要重發就換一個新的版本號。

```bat
git tag v1.0.0
git push origin v1.0.0
```

### 燈號、選單和通知

| 燈號 | 條件 |
|---|---|
| 灰 | USB4 Router 和 GPU 都找不到 |
| 綠 | GPU 的錯誤碼是 0 |
| 紅 | USB4 Router 或 GPU 出現了，但 GPU 還沒出現或有錯誤碼 |

游標停在圖示上會顯示「eGPU：」加上狀態，文字和 [egpu-connect 第 3 步的狀態表](#3-拔插-egpu等-gpu-穩定)相同。同一時間只會有一個工作列程式在跑。

| 右鍵選單 | 做什麼 |
|---|---|
| 狀態：… | 只顯示，不能點 |
| 重新啟用 GPU（解除 Code 22 停用） | 只在 GPU 被「Disconnect GPU」停用時出現，見[下一節](#gpu-被停用code-22時) |
| 執行 egpu-connect 救援 | 用 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` 執行安裝資料夾裡的 `egpu-connect.ps1`。工作列程式本身已經是系統管理員權限，所以不會再跳 UAC；同一時間只會開一個 |
| 結束 | 只關掉這次的工作列程式，下次登入還是會啟動 |

通知有三條規則：

- 已接上但 GPU 不正常，要連續 20 秒才通知。剛插上 eGPU 的前幾秒常有短暫異常，這樣不會一插就跳通知。
- 同一段異常只通知一次。GPU 恢復正常或拔掉之後，才重新計算。
- 從選單執行的 egpu-connect 還沒結束時不通知，因為救援本來就會要你拔插。

| 通知 | 什麼時候跳 | 點下去會 |
|---|---|---|
| eGPU 異常 | 一般的異常，例如 GPU 沒出現、Code 12 | 執行 egpu-connect 救援 |
| eGPU 被停用了 | Code 22，而且是「Disconnect GPU」造成的 | 重新啟用 GPU |
| egpu-tray 發生錯誤 | 讀狀態或執行動作失敗 | 沒有動作，只顯示錯誤訊息 |

### GPU 被停用（Code 22）時

NVIDIA 視窗裡的「Disconnect GPU」會把 GPU 停用。它把停用記在「目前的硬體設定檔」裡，不是一般的停用旗標，所以拔插不會恢復，`Enable-PnpDevice` 也清不掉。

| 位置 | 值 | 意思 |
|---|---|---|
| `HKLM\SYSTEM\CurrentControlSet\Enum\<裝置執行個體路徑>` 的 `ConfigFlags` | 0 | 一般的停用旗標，`Enable-PnpDevice` 清的是這個 |
| `HKLM\SYSTEM\CurrentControlSet\Hardware Profiles\Current\System\CurrentControlSet\Enum\<裝置執行個體路徑>` 的 `CSConfigFlags` | 1 | 「Disconnect GPU」設的是這個，所以變成 Code 22 |

選單的「重新啟用 GPU」會清掉 `CSConfigFlags` 的停用位元，再執行 `pnputil /enable-device "<裝置執行個體路徑>"`。GPU 還沒恢復的話，會跳通知請你拔插一次。

沒裝 egpu-tray 時，在系統管理員 PowerShell 執行下面這行，再拔插 eGPU：

```powershell
Set-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Hardware Profiles\Current\System\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_2C18&SUBSYS_225B1043&REV_A1\5EC18BAFDA2DB04800' -Name CSConfigFlags -Value 0
```

路徑最後是這張 GPU 的裝置執行個體路徑。換了 GPU 的話，到裝置管理員 → 裝置內容 → 詳細資料 → 裝置執行個體路徑查。

### 解除安裝

再執行一次 `egpu-tray.exe`，按「解除安裝」並確認：

```text
通知工作列程式結束 ──► 刪除登入排程 ──► 刪除 C:\Program Files\egpu-tray\
```

- 工作列程式會在下一次檢查時（最多 5 秒）自己結束；10 秒內沒結束就強制結束。
- 安裝資料夾裡的 egpu-connect 紀錄（`egpu-connect\logs\`）也會一起刪掉。
- 如果你執行的正好是安裝資料夾裡的那份 `egpu-tray.exe`，資料夾會在視窗關掉約 3 秒後才刪除。

## egpu-connect：不安裝也能直接跑

`egpu-connect` 先把佔著車的服務請走，等你重插 eGPU、GPU 穩定後，再把它們請回來。從 egpu-tray 的選單執行或直接執行都可以，以下各節都在講 egpu-connect。

下面的指令都在 repo 根目錄執行。

不用安裝。在這個資料夾開命令提示字元，先看工具會動到什麼，再正式執行：

```bat
tools\egpu-connect\egpu-connect.cmd -DryRun
tools\egpu-connect\egpu-connect.cmd
```

也可以在檔案總管直接雙擊 `egpu-connect.cmd`。

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

## 跑起來會看到什麼

照畫面指示拔掉 eGPU，等約 5 秒插回同一個 port。下面節錄自一次實際執行的紀錄：

```text
[1/4] 檢查 Remote Desktop Device Redirector Bus
  已是停用狀態
[2/4] 暫停會否決資源重新分配的服務（約 1~3 分鐘沒有聲音）
  停止服務 MSI_Center_S_Service
  ...
[3/4] 重新連接 eGPU
  請拔掉 eGPU 的線...
  已拔除。等約 5 秒後，把 eGPU 插回「同一個」port。
  17:30:38 USB4 Router 已出現，GPU 尚未出現
  17:30:41 GPU 正常
  17:30:46 GPU 異常（Code 12）
  17:31:09 USB4 Router 已出現，GPU 尚未出現
  17:31:11 GPU 正常
[4/4] 恢復服務與程式
  啟動服務 AudioEndpointBuilder
  ...

eGPU 已就緒。
```

看到綠色的「eGPU 已就緒。」就能用了，按 Enter 關掉視窗。
GPU 剛正常又掉線很常見，所以工具要等它連續正常 15 秒才算成功。

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
從 egpu-tray 的選單執行時，紀錄寫在 `C:\Program Files\egpu-tray\egpu-connect\logs\`，解除安裝時會一起刪掉。

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

egpu-tray 用同樣的兩個樣式，寫在 `tools/egpu-tray/src/EgpuDevice.cs` 的 `RouterIdPattern` 和 `GpuIdPattern`。改了要一起改，再重新編譯並按一次「安裝」。
