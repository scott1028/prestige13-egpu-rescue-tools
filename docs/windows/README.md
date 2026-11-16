# Windows：egpu-tray 與 egpu-connect

[← 回到首頁](../../README.md) ｜ [Linux 版](../linux/README.md)

兩支 Windows 小工具加一份分析報告：`egpu-tray.exe` 常駐工作列、隨時看著 ASUS ROG XG Mobile GC34X eGPU；`egpu-connect` 在它連不上時，幫你在 MSI Prestige 13 AI+ Evo 上把它救回來。

## 為什麼 eGPU 時有時無

把 PCIe 位址空間想成停車場。BIOS 替 Thunderbolt port 畫了 96 MB 的小車位和最多 32 GB 的大車位，RTX 5090 Laptop 開著 ReBAR，光是讓 CPU 看顯存的窗口（BAR1）就要 32 GB，再加上別的就停不進去。
Windows 只好請整排 17 個內建裝置（Wi‑Fi、音效、USB4 Host Router 等）先開走，重畫車位，這叫 rebalance。
只要有一台開不走（例如有服務正在用它），也就是 veto，整次 rebalance 就失敗，eGPU 偵測不到或出現 Code 12。

## 這兩支工具怎麼救

```text
egpu-tray.exe ──每 5 秒看一次──► eGPU 狀態 ──異常超過 20 秒──► 跳通知 ──你點一下──► 執行 egpu-connect
egpu-connect  ──暫停會 veto 的服務──► 你拔插 eGPU ──► Windows 重畫車位 ──► GPU 連續正常 15 秒 ──► 恢復服務
```

egpu-connect 整段約 1~3 分鐘，這段時間沒有聲音。GPU 最多等 180 秒，逾時一樣會恢復服務再結束。

> egpu-connect 會永久停用 `Remote Desktop Device Redirector Bus`，遠端桌面的裝置重導（印表機、USB 轉送）會不能用，隨時可以改回來。

## 需要準備什麼

- MSI Prestige 13 AI+ Evo A2VMG（Core Ultra 288V）和 ASUS ROG XG Mobile GC34X（RTX 5090 Laptop）。分析時的環境是 BIOS E13Q3IMS.112、Windows 11 build 26300。
- Windows 內建的 PowerShell（`powershell.exe`）。要自己編譯 `egpu-tray.exe` 的話，還會用到 .NET Framework 4.8 內建的 C# 編譯器（`csc.exe`），都不用另外安裝。
- 系統管理員權限。兩支工具都會自己跳出 UAC 詢問；egpu-connect 的 `-DryRun` 不需要。

## 五分鐘上手

到 [Releases](https://github.com/scott1028/prestige13-egpu-rescue-tools/releases/latest) 下載 `egpu-tray.exe`，雙擊執行，在跳出的視窗按「安裝」。
程式會複製到 `C:\Program Files\egpu-tray\`，之後每次登入都會自動常駐工作列，不會再跳 UAC。

`egpu-tray.exe` 沒有程式碼簽章，從網路下載後第一次執行時，SmartScreen 可能會擋下來。按「其他資訊」→「仍要執行」，或改成自己編譯。自己編譯的話，在 repo 根目錄開命令提示字元：

```bat
tools\egpu-tray\build.cmd
dist\egpu-tray.exe
```

## 裝好之後會看到什麼

工作列右下角會出現一個圓點，可能收在「^」裡。游標停在上面會顯示 eGPU 狀態，按右鍵可以救援或結束。

| 燈號 | 意思 |
|---|---|
| 灰 | 沒接 eGPU |
| 綠 | GPU 正常，可以用了 |
| 紅 | 已接上，但 GPU 沒出現或有錯誤碼（例如 Code 12、Code 22） |

異常持續 20 秒會跳通知，點通知就會執行對應的救援。

## 遇到哪種情況該怎麼做

| 情況 | 做法 |
|---|---|
| 跳出「eGPU 異常」通知，或圖示變紅 | 點通知，或按右鍵選「執行 egpu-connect 救援」 |
| 跳出「eGPU 被停用了」通知（Code 22） | 點通知，或按右鍵選「重新啟用 GPU」；還沒恢復就拔插一次 |
| 沒裝 egpu-tray，想直接救 | 執行 `tools\egpu-connect\egpu-connect.cmd`，也可以在檔案總管直接雙擊 `egpu-connect.cmd` |
| 想先知道 egpu-connect 會停掉哪些東西 | 加 `-DryRun`，只顯示狀態，什麼都不改 |
| 畫面顯示「在 180 秒內沒有等到 GPU 正常」 | 再執行一次；還是失敗，先插好 eGPU 再重開機 |
| 用了 5~20 分鐘後才出現 Code 12 | 工具擋不住這種情況，Windows 會要求重開機 |
| 不想用了 | 再執行一次 `egpu-tray.exe`，按「解除安裝」 |

## 三件事一定要記得

1. 同一次開機都插同一個 port。eGPU 消失時先等 1~2 分鐘讓它自己重連，不要馬上換 port。
2. 拔除前先關掉正在用 eGPU 的程式，再直接拔線。
3. 不要按 NVIDIA 視窗裡的「Disconnect GPU」。它會把 GPU 停用（Code 22），插回去也不會自己恢復。

## 安全與風險

- 兩支工具都用系統管理員權限執行：egpu-connect 用 `-ExecutionPolicy Bypass`，`egpu-tray.exe` 透過登入排程以最高權限常駐。原始碼都在這個 repo（`egpu-connect.ps1` 約 200 行），執行前可以先讀過。
- 停用 `ROOT\RDPBUS\0000` 是永久設定。要改回來，在系統管理員 PowerShell 執行 `Enable-PnpDevice -InstanceId 'ROOT\RDPBUS\0000'`。
- egpu-connect 會強制結束 MSI 的相關程式，最後再重新開啟。兩支工具都不改 BIOS，也不寫 NVRAM；「解除安裝」會移除登入排程和整個安裝資料夾。

## 想知道更多

- [guide.md](guide.md)：名詞解釋、egpu-tray 的編譯、發行新版本、安裝內容、燈號、選單、通知、Code 22 和解除安裝，egpu-connect 四個步驟各做了什麼、跑起來的畫面、等待時間、紀錄檔和結束代碼、限制和特殊情況、改給別的 eGPU 用
- [egpu-intermittent-detection-report.md](egpu-intermittent-detection-report.md)：根因分析、事件統計、誰否決了 rebalance、BIOS 預設值、ReBAR 的驗證與實驗、建議測試、未驗證項目
- [egpu-event-timeline.txt](egpu-event-timeline.txt)：從 Windows 事件記錄整理出的 eGPU 事件時間軸（2025-11-08 ~ 2026-11-15）
