# prestige13-egpu-rescue-tools

**[Windows](docs/windows/README.md)** ｜ **[Linux](docs/linux/README.md)**

讓 MSI Prestige 13 AI+ Evo A2VMG 穩定使用 ASUS ROG XG Mobile GC34X（RTX 5090 Laptop GPU）的工具，以及在 Windows 和 Linux 上除錯的完整紀錄。

## 選你的系統

| 系統 | 工具 | 解決的問題 | 從這裡開始 |
|---|---|---|---|
| Windows 11 | `egpu-tray.exe` 常駐工作列、`egpu-connect` 救援腳本 | eGPU 時有時無、Code 12、Code 22 | [docs/windows/README.md](docs/windows/README.md) |
| Ubuntu 26.04 + KDE Plasma | udev 自動修正、`egpu-mode` 切換合成桌面的 GPU | 驅動載不起來、白色花點和 GPU 掉線、100Hz 卡頓或只剩 50 fps | [docs/linux/README.md](docs/linux/README.md) |

升級 NVIDIA 驅動或 kernel 之前，先看 [docs/versions.md](docs/versions.md)：本專案用的版本、為什麼用這些版本、升級前後要做什麼。

## 兩邊共同的根因

RTX 5090 Laptop 開著 ReBAR 時，BAR1 要 32 GB，BIOS 替 Thunderbolt port 預留的位址區段放不下它。

- Windows 每次插上都要重新分配整個 PCI root 的位址（rebalance）。只要有一個裝置否決，eGPU 就偵測不到，見 [Windows 分析報告](docs/windows/egpu-intermittent-detection-report.md)。
- Linux 上用 udev 規則，在驅動載入前把 BAR1 縮成 256 MB，就放得下了，見 [Linux guide 故障 1](docs/linux/guide.md#1-驅動載不起來bar1-放不下0841)。

## 下載

到 [Releases](https://github.com/scott1028/prestige13-egpu-rescue-tools/releases/latest) 下載：

| 檔案 | 系統 | 說明 |
|---|---|---|
| `egpu-tray.exe` | Windows | 雙擊後按「安裝」，見 [Windows README](docs/windows/README.md#五分鐘上手) |
| `egpu-mode-x86_64.AppImage` | Linux | `chmod +x` 後雙擊，不用安裝，見 [Linux README](docs/linux/README.md#切換使用方式) |

Linux 的 udev 修正要用 `tools/egpu-linux/` 裡的腳本安裝，沒有放進 Release。

## 發行新版本

推送 `v` 開頭的 tag，`.github/workflows/release.yml` 會同時編譯兩個檔案，建立同名的 Release：

```text
git push origin v1.0.0 ──┬─► Windows runner: build.cmd ──────────► egpu-tray.exe ─────────────┐
                         └─► Linux runner: build-appimage.sh ──► egpu-mode-x86_64.AppImage ─┴─► Release v1.0.0
```

```bash
git tag v1.0.0
git push origin v1.0.0
```

細節見 [Windows guide 發行新版本](docs/windows/guide.md#發行新版本)和 [Linux guide 打包成 AppImage](docs/linux/guide.md#打包成-appimage)。

## Repo 結構

```text
.github/workflows/release.yml   推送 v* tag 時編譯兩個系統的檔案並發行
tools/
├─ egpu-tray/      Windows 工作列程式（C#）
├─ egpu-connect/   Windows 救援腳本（PowerShell）
└─ egpu-linux/     Linux udev 修正、安裝腳本、egpu-mode
   └─ appimage/    把 egpu-mode 打包成 AppImage
docs/
├─ versions.md  用到的版本、升級驅動和 kernel 的步驟和注意事項
├─ windows/     README、guide、分析報告、事件時間軸
└─ linux/       README、guide、evidence/（去掉個人資訊的原始 log 和量測）
```

## 授權

目前沒有授權檔（LICENSE）。
