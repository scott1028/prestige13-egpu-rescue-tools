# prestige13-egpu-rescue-tools

一支 Windows 小工具加一份分析報告，讓 ASUS ROG XG Mobile GC34X eGPU 在 MSI Prestige 13 AI+ Evo 上插了就能用，不再時有時無。

## 為什麼 eGPU 時有時無

把 PCIe 位址空間想成停車場。BIOS 只替 Thunderbolt port 畫了 96 MB 的車位，RTX 5090 Laptop 停不進去。
Windows 只好請整排 17 個內建裝置（Wi‑Fi、音效、USB4 Host Router 等）先開走，重畫車位，這叫 rebalance。
只要有一台開不走（例如有服務正在用它），也就是 veto，整次 rebalance 就失敗，eGPU 偵測不到或出現 Code 12。

## 這個工具怎麼救

`egpu-connect` 先把佔著車的服務請走，等你重插 eGPU、GPU 穩定後，再把它們請回來。

```text
暫停會 veto 的服務 ──► 你拔插 eGPU ──► Windows 重畫車位 ──► GPU 連續正常 15 秒 ──► 恢復服務
```

整段約 1~3 分鐘，這段時間沒有聲音。GPU 最多等 180 秒，逾時一樣會恢復服務再結束。

> 工具會永久停用 `Remote Desktop Device Redirector Bus`，遠端桌面的裝置重導（印表機、USB 轉送）會不能用，隨時可以改回來。

## 需要準備什麼

- MSI Prestige 13 AI+ Evo A2VMG（Core Ultra 288V）和 ASUS ROG XG Mobile GC34X（RTX 5090 Laptop）。分析時的環境是 BIOS E13Q3IMS.112、Windows 11 build 26300。
- Windows 內建的 PowerShell（`powershell.exe`），不用另外安裝。
- 系統管理員權限。工具會自己跳出 UAC 詢問；`-DryRun` 不需要。

## 五分鐘上手

不用安裝。在這個資料夾開命令提示字元，先看工具會動到什麼，再正式執行：

```bat
tools\egpu-connect\egpu-connect.cmd -DryRun
tools\egpu-connect\egpu-connect.cmd
```

也可以在檔案總管直接雙擊 `egpu-connect.cmd`。

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

## 遇到哪種情況該怎麼做

| 情況 | 做法 |
|---|---|
| 想先知道工具會停掉哪些東西 | 加 `-DryRun`，只顯示狀態，什麼都不改 |
| 剛插上 eGPU 卻偵測不到，或出現 Code 12 | 執行 `egpu-connect.cmd` |
| 畫面顯示「在 180 秒內沒有等到 GPU 正常」 | 再執行一次；還是失敗，先插好 eGPU 再重開機 |
| 用了 5~20 分鐘後才出現 Code 12 | 工具擋不住這種情況，Windows 會要求重開機 |

## 兩件事一定要記得

1. 同一次開機都插同一個 port。eGPU 消失時先等 1~2 分鐘讓它自己重連，不要馬上換 port。
2. 拔除前先在 Armoury Crate 退出 eGPU。

## 安全與風險

- 工具用系統管理員權限和 `-ExecutionPolicy Bypass` 執行。`egpu-connect.ps1` 只有約 200 行，執行前可以先讀過。
- 停用 `ROOT\RDPBUS\0000` 是永久設定。要改回來，在系統管理員 PowerShell 執行 `Enable-PnpDevice -InstanceId 'ROOT\RDPBUS\0000'`。
- 工具會強制結束 MSI 的相關程式，最後再重新開啟。它不改 BIOS，也不寫 NVRAM。

## 想知道更多

- [docs/guide.md](docs/guide.md)：名詞解釋、四個步驟各做了什麼、等待時間、紀錄檔和結束代碼、限制和特殊情況、改給別的 eGPU 用
- [docs/egpu-intermittent-detection-report.md](docs/egpu-intermittent-detection-report.md)：根因分析、事件統計、誰否決了 rebalance、BIOS 預設值、建議測試、未驗證項目
- [docs/egpu-event-timeline.txt](docs/egpu-event-timeline.txt)：從 Windows 事件記錄整理出的 eGPU 事件時間軸（2025-11-08 ~ 2026-11-15）

## 授權

目前沒有授權檔（LICENSE）。
