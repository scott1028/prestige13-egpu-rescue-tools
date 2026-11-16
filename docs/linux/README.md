# Linux：GC34X eGPU on Ubuntu

[← 回到首頁](../../README.md) ｜ [Windows 版](../windows/README.md)

這份筆記記錄怎麼讓 MSI Prestige 13 AI+ Evo 在 Ubuntu 26.04 上穩定使用 ASUS ROG XG Mobile GC34X（RTX 5090 Laptop GPU），以及 2026-11-16 一路遇到的故障和修法。

eGPU 像是走廊另一頭的辦公室：畫面資料要先經過 Thunderbolt 這條走廊送過去，GPU 才能顯示到外接螢幕。

```text
筆電記憶體 ──畫面資料（PCIe over TB4）──► RTX 5090 ──HDMI──► 外接螢幕
```

**一定要先關機再拔 eGPU。** 熱拔會讓整個桌面卡死，只能重開機。

## 需要什麼

- 筆電：MSI Prestige 13 AI+ Evo A2VMG（Lunar Lake，BIOS `E13Q3IMS.112`），接 TB4 port
- eGPU：ASUS ROG XG Mobile GC34X（RTX 5090 Laptop GPU，PCI ID `10de:2c18`）
- Ubuntu 26.04.1 LTS，kernel `7.0.0-43-generic`，KDE Plasma 6.6.6 Wayland
- Secure Boot 關閉：當天是在關閉的狀態下安裝和測試，開啟 Secure Boot 時沒有試過
- 安裝時 eGPU 要接著，`install.sh` 找不到 GPU 會直接停止

## 安裝

所有腳本都在 [`tools/egpu-linux/`](../../tools/egpu-linux/)。在 repo 根目錄執行：

```bash
cd tools/egpu-linux
sudo bash install.sh && sudo bash egpu-fix.sh
sudo bash install-cstate.sh && sudo bash install-clocks.sh
```

跑完後關機、拔掉 eGPU、開機，登入後再插上。這四支腳本當天是分次跑的，還沒有從頭整套重跑驗證過。

## 試試看：插上 eGPU 後確認狀態

插上 eGPU，等外接螢幕亮起來，再登出登入一次，然後執行：

```bash
journalctl -b -t egpu-bar1 -t egpu-cstate -t egpu-clocks --no-pager -o cat
nvidia-smi --query-gpu=pstate,pcie.link.gen.current,clocks.mem --format=csv,noheader
```

重點看三行，其他是 `nvidia-smi` 的一般訊息：

- `egpu-bar1` 有 `BAR1 set to 256 MB`
- `egpu-cstate` 最後一筆是 `limit: deep C-states disable=1`
- `nvidia-smi` 印出 `P4, 3, 9001 MHz`，中間的 `3` 代表 PCIe Gen3

## 四個修正各解決什麼

| 修正 | 解決的問題 | 檔案 |
|---|---|---|
| BAR1 縮成 256 MB | 驅動載不起來（BAR1 放不進 BIOS 預留的位址區段） | `egpu-bar1`、`70-egpu-bar1.rules` |
| Thunderbolt CLx 關閉、GPU 連線上限 Gen3 | 降低連線不穩，但單獨使用不夠 | `egpu-gc34x.conf` |
| 接著 eGPU 時關掉 CPU 的 C2/C3 | 白色花點、GPU 從 bus 掉線（`CmpltTO`） | `egpu-cstate`、`71-egpu-cstate.rules` |
| 鎖住 GPU 記憶體時脈 | 100Hz 卡頓（PCIe 停在 Gen1） | `egpu-clocks`、`egpu-clocks.service`、`72-egpu-clocks.rules` |

## 四件事一定要記得

1. 先關機再拔 eGPU。
2. 插上 eGPU 後要登出再登入一次，不然 KWin 接不上 5090，100Hz 只剩 50 fps。
3. 接著 eGPU 時 CPU 不會進深度省電，eGPU 待機約 16 W，筆電會比較耗電、比較熱。
4. 改了 `/etc/modprobe.d/egpu-gc34x.conf` 之後，要先跑 `sudo update-initramfs -u` 再重開機才會生效。

## 切換使用方式

不用安裝。到 [Releases](https://github.com/scott1028/prestige13-egpu-rescue-tools/releases/latest) 下載 `egpu-mode-x86_64.AppImage`，`chmod +x` 之後雙擊。系統有 `python3-pyqt6` 的話，也可以直接雙擊原始碼 [`tools/egpu-linux/egpu-mode`](../../tools/egpu-linux/egpu-mode)。

| 按鈕 | 做什麼 | 生效方式 |
|---|---|---|
| A. Intel 合成（預設） | Intel 內顯合成桌面，5090 負責輸出 | 登出再登入 |
| B. 用 5090 開啟程式 | 指定的程式用 5090 算圖 | 馬上生效 |
| C. 5090 合成桌面（建議） | 讓 5090 當 KWin 的主卡，實測 100Hz 穩定 100 fps | eGPU 接著的狀態下登出再登入，或接著 eGPU 開機 |

三種都不用重開機。登出後在登入畫面等 30 秒再登入，太快登入可能讓程式卡住。C 的運作方式和黑畫面救援見 [guide.md](guide.md#切換使用方式egpu-mode)。

## 安全

- Secure Boot 關閉。
- IOMMU 保持預設開啟（沒有用 `iommu=pt`），Thunderbolt 的 DMA 保護還在。
- udev 規則會以 root 執行 `/usr/local/sbin/` 下的腳本，規則只比對 eGPU 的 PCI ID `10DE:2C18`。

## 想知道更多

完整內容在 [guide.md](guide.md)：

- [名詞對照](guide.md#名詞對照)
- [資料從筆電到 GPU 怎麼走](guide.md#資料從筆電到-gpu-怎麼走)
- [從頭安裝：檔案放哪、每支腳本做什麼](guide.md#從頭安裝)
- [日常使用：插上、確認、拔掉、選更新率、切換使用方式、桌面卡住時](guide.md#日常使用)
- [故障排除紀錄：六個問題的症狀、原因、證據和修法](guide.md#故障排除紀錄)
- [還沒驗證和還沒試過的](guide.md#還沒驗證和還沒試過的)
- [健康檢查指令](guide.md#健康檢查指令)
- [參考資料](guide.md#參考資料)

當天的原始資料（journal 摘錄、PCIe 量測，已去掉主機名稱和帳號）在 [evidence/](evidence/README.md)。
