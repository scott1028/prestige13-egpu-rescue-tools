# Linux 原始資料（2026-11-16）

[← 回到 Linux 文件](../README.md)

這裡是 2026-11-16 排除故障時留下的原始紀錄，查問題時可以直接 `grep`。整理過的結論在 [guide.md](../guide.md)。

```text
evidence/
├─ logs/          每次開機的 journal 摘錄，加上 egpu-fix.log
└─ measurements/  Gen3 時的 PCIe 流量量測
```

## 去掉了哪些資訊

放進 repo 前，逐行換掉了認得出這台機器和使用者的資訊，行數沒有變：

| 原本 | 換成 |
|---|---|
| 主機名稱 | `host` |
| 帳號（`sudo`、登入 session、systemd 跳脫過的路徑裡） | `user` |
| 家目錄、工作資料夾 | `~`、`~/work` |
| Claude Code 的暫存資料夾 | `/tmp/scratchpad` |
| GPU UUID | `GPU-<uuid>` |

systemd 程式實例名稱裡的十六進位 ID（例如 `app-nvidia\x2dsettings@e5ea….service`）每次啟動都不一樣，認不出機器，所以保留。

## 各檔案內容

journal 檔名是那次開機的時間。

| 檔案 | 時間 | 內容 | 對應 guide |
|---|---|---|---|
| `logs/journal-2026-11-16-0841.log` | 08:41–09:27 | 裝驅動、BAR1 放不下、第一版修法失敗、nouveau 重試 1624 次 | 故障 1、容易踩的坑 1 |
| `logs/journal-2026-11-16-0928.log` | 09:28–09:49 | `CmpltTO`、GSP 逾時、Xid 175 / 154 | 故障 2 |
| `logs/journal-2026-11-16-0951.log` | 09:51–10:11 | 白色花點時的 `CmpltTO`、10:03 Xid 79 | 故障 3 |
| `logs/journal-2026-11-16-1012.log` | 10:12–10:38 | 關 C2/C3 後 12 分鐘沒有 `CmpltTO`、10:36 熱拔卡死 | 故障 3、5 |
| `logs/journal-2026-11-16-1039.log` | 10:39–10:49 | 鎖記憶體時脈、`72-egpu-clocks.rules` 驗證 | 故障 4 |
| `logs/journal-2026-11-16-1039-part2.log` | 10:39–11:49 | 同一次開機補抓的行：KWin 對 5090 的 `eglInitialize` 失敗 40 次（10:42:04–11:20:14）、11:20 重新登入、11:48 portal 逾時 | 故障 6、切換使用方式 |
| `logs/journal-2026-11-16-1149.log` | 11:49–12:01 | 接著 eGPU 開機，三個自動修正在開機時套用；用 C 模式登入（有 `egpu-mode confirm`）。11:54 的 TestUFO 結果不在 journal 裡 | 切換使用方式 |
| `logs/journal-2026-11-16-2002.log` | 20:02–20:20 | 測試 A、B、C 時每次登出、登入的 session、使用者管理程式有沒有沿用。B 的 `glxgears` 不在 journal 裡 | 切換使用方式 |
| `logs/egpu-fix.log` | 09:43 | `egpu-fix.sh` 的輸出，含當時各段 PCIe 連線狀態 | 故障 2 |
| `measurements/gpu-pcie2.txt` | 10:45–10:49 | `nvidia-smi dmon -s t -o T`，每秒一筆 `rxpci` / `txpci`（MB/s） | 故障 4 |
| `measurements/gpu-gen2.csv` | 10:45–10:49 | 同一段時間每秒的 timestamp、P-state、PCIe gen、記憶體時脈 | 故障 4 |

## journal 是怎麼篩的

到 `1039.log` 為止的五個檔，是當天只留下 `egpu-`、`NVRM`、`nvidia`、`CmpltTO`、`AER`、`BAR 1`、`nouveau`、`pciehp`、`thunderbolt`、`Xid` 等相關的行，並去掉大量重複的 `rpcSendMessage failed`、`nvdEngineDumpCallbackHelper` 和 KWin 回報提示。

後面三個檔是事後從系統 journal 補抓的，沒有去掉重複的行。篩選條件多了 KWin、EGL、桌面 portal 和登入 session：

```text
egpu|NVRM|nvidia|CmpltTO|AER|BAR 1|nouveau|pciehp|thunderbolt|Xid|kwin|EGL|xdg-desktop-portal|NoReply|Timeout was reached|New session|Removed session|user@1000|User Manager
```

`1039-part2.log` 用這組條件篩同一次開機，再去掉 `1039.log` 已經有的行，兩個檔合起來才是完整的一次開機。

## 沒有放進來的資料

- Claude Code 的對話紀錄：有本機路徑和帳號資訊，只留在本機。重點都整理進 guide.md 了。
- 從對話紀錄救回的腳本原始版本：和 [`tools/egpu-linux/`](../../../tools/egpu-linux/) 的版本只差結尾一個換行，不重複放。

## 遺失的資料

- 10:29–10:34 Gen1 時的量測檔（`gpu-pcie.txt`、`gpu-gen.csv`）在 10:39 重開機時被清掉了。數字（卡在 809–833 MB/s）記在 guide 的故障 4。
- 第一次裝驅動時的 `install.log` 放在 `/tmp`，一樣遺失了。重點都在 `logs/journal-2026-11-16-0841.log`。
