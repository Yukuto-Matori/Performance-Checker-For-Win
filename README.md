# Performance-Checker-For-Win

Windows 11 向けの C# / .NET PC 診断・性能ログツール。

## 使い方

### ZIP版を使う（推奨）

GitHub Release から `Performance-Checker-For-Win-win-x64.zip` を取得して任意の場所に展開する。配布版は self-contained / single-file のため、別途 .NET Runtime は不要。

GitHub Actions の通常CIでは同じZIPがArtifactとして生成され、`v*` タグを付けたCIではGitHub Releaseにも自動添付される。

レジストリを変更せず、そのまま `PerformanceChecker.exe` を起動できる。

### GUI / タスクトレイ

`PerformanceChecker.exe` を起動するとタスクトレイに常駐する。

- **ログ表示**: `performance-log.yaml` を別プロセスの既定のテキストアプリで開く
- **監視: ON/OFF**: 定期監視を停止・再開する
- **アプリ＆監視終了**: 監視を停止して終了する

起動時にはハードウェア情報を1回取得し、監視OFF→ONでも同一起動中は再取得しない。

### CLI

```powershell
PerformanceChecker.exe --cli
PerformanceChecker.exe --cli --interval 10 --duration 3600
```

- `L`: YAMLログを別プロセスで開く
- `S`: 即時スナップショット
- `Q`: 終了
- `--interval <seconds>`: サンプリング間隔
- `--duration <seconds>`: 自動終了時間。0または未指定は無期限

## ログ

ログは `PerformanceChecker.exe` と同じフォルダの `performance-log.yaml` に保存される。YAML先頭にはAI向けの読み方、単位、欠損値、クラッシュ調査のポイントをコメントとして記載する。

そのままChatGPTなどに貼り付けて、PC状態やクラッシュ原因の分析に利用できる。

## 起動時に取得する情報

- CPU名 / 物理Core数 / Logical Processor数 / 最大クロック / OS情報
- GPU名 / Driver Version / VRAM / PNP Device ID
- 物理ディスクModel / Serial Number（取得可能な場合）/ Interface / Media Type / 容量 / Status
- 論理ドライブ容量 / 空き容量
- 物理RAM総容量

今後、CrystalDiskInfoに近いNVMe / SATA SMART・Health情報を拡張する。

## 定期監視

- CPU使用率 / 温度 / 電力（取得可能な場合）
- CPU使用率上位3プロセス
- メモリ使用率
- GPU使用率 / 温度 / VRAM / 電力（取得可能な場合）
- Network送受信量・速度
- Disk Read / Write / Busy（取得可能な場合）

## クラッシュ調査

問題のPCで長時間監視する。

```powershell
PerformanceChecker.exe --cli --interval 60 --duration 28800
```

これは60秒間隔で8時間監視する例。アプリがクラッシュしたら、YAMLのクラッシュ前後の時刻とCPU / GPU / RAM / Disk / Network / プロセスを相関させる。

Windows Event Logでは特に以下を確認する。

- `WHEA-Logger`
- `Application Error`
- `Application Hang`
- `Display` / `nvlddmkm`
- `Disk`
- `Ntfs`
- `stornvme`
- `Kernel-Power`

YAMLだけで故障を確定することはできないため、Event Viewer、ミニダンプ、Reliability Monitorなどと組み合わせて調査する。

## PC買い替え時の比較

同じ作業・負荷を旧PCと新PCで実施し、CPU使用率/温度/電力、GPU使用率/温度/VRAM、メモリ、Disk I/O、Network、高負荷プロセスを比較する。最大性能だけでなく、同じ作業時の温度・電力・負荷率・I/O状態を見ることで安定性も比較できる。

## Portable / Install / Uninstall

### Portable

ZIPを展開して `PerformanceChecker.exe` を直接起動できる。レジストリ登録やWindowsサービス登録は行わない。

### インストール

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

既定のインストール先は `%LOCALAPPDATA%\PerformanceCheckerForWin`。

### アンインストール

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

サービスやレジストリ登録を行わないため、アプリを終了してインストールディレクトリを削除すればアンインストールできる。

## GitHub Release / 配布版の作成

通常の `main` / `feature/**` へのpushではCIとArtifact生成だけを行う。

正式な配布版を作るときは、`v` で始まるGitタグを作成してpushする。

```powershell
git tag v0.1.0
git push origin v0.1.0
```

`v*` タグでは通常のRestore / Build / Publish / ZIP生成に加えて、GitHub Releaseを自動作成し、以下を添付する。

```text
Performance-Checker-For-Win-win-x64.zip
SHA256SUMS.txt
```

`SHA256SUMS.txt` にはZIPのSHA-256ハッシュが記録されるので、配布ファイルの完全性確認に利用できる。

Release名はタグから自動的に `Performance-Checker-For-Win v0.1.0` のように生成され、GitHubの自動Release Notesも生成される。

## 必要環境

### 配布版

- Windows 11 x64
- .NET Runtime不要

### 開発環境

- Windows 11 x64
- .NET 10 SDK

Hardware sensor accessについては、PCやセンサーによって管理者権限が必要になる場合がある。

## 開発者向けBuild

```powershell
dotnet restore
dotnet build -c Release
```

Portable single-file:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

GitHub ActionsではPublish結果をZIPにまとめ、Artifactへ保存する。`v*` タグの場合はReleaseにも添付する。

## AI分析

`performance-log.yaml` をそのままAIへ渡し、以下を中心に分析させる。

1. CPU温度・使用率・電力の異常
2. GPU温度・使用率・VRAM・電力の異常
3. メモリ逼迫
4. クラッシュ直前の高負荷プロセス
5. Disk I/OやSSD関連の異常
6. Network負荷
7. 時刻を基準にした複数センサーの同時異常
8. WHEAなどハードウェアエラーを示す兆候

## Planned / Next

- NVMe / SATA SMART・Health情報の起動時取得
- Windows Event Log / WHEA の時系列収集
- CPU thermal / power-limit / throttling 状態の強化
- GPU詳細センサーの強化
- SSD Healthを含むAI向け診断サマリー
- 長時間監視用のログローテーション
- PC買い替え前後を比較するサマリー生成
