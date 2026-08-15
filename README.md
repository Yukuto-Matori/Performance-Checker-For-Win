# Performance-Checker-For-Win

Windows 11 向けの C# / .NET PC 診断・性能ログツール。

## 目的

- 不定期なアプリクラッシュの原因調査
- CPU / RAM / GPU / SSD / Network の状態記録
- Windows PC 買い替え前後の比較用データ生成
- 生成した YAML をそのまま ChatGPT などの AI に渡して分析できる形式にする

## GUI / タスクトレイ

通常起動するとメインウィンドウを表示せず、タスクトレイに常駐する。

- **ログ表示**: `performance-log.yaml` を別プロセスの Notepad で開く
- **監視: ON/OFF**: 1 分間隔の監視を停止・再開する
- **アプリ＆監視終了**: 監視を停止してアプリを終了する

アプリ起動時にはハードウェア情報を一度だけ取得する。監視をOFFにして再度ONにしても、同一起動中は初期ハードウェア情報を再取得しない。

## CLI

既存の CLI モードも利用できる。

```powershell
PerformanceChecker.exe --cli
PerformanceChecker.exe --cli --interval 10 --duration 3600
```

CLI controls:

- `L`: YAMLログをNotepadで開く
- `S`: 即時スナップショット
- `Q`: 終了
- `--interval <seconds>`: サンプリング間隔
- `--duration <seconds>`: 自動終了時間。0または未指定は無期限

## 記録内容

起動時に一度:

- CPU名 / Core数 / Logical Processor数 / 最大クロック
- GPU名 / Driver Version / VRAM / PNP Device ID
- 物理ディスク Model / Serial / Interface / Media Type / Size / Status
- 論理ドライブ容量 / 空き容量
- RAM総容量
- OS情報

監視中は1分ごと:

- CPU使用率 / 温度 / 電力
- GPU使用率 / 温度 / VRAM / 電力
- RAM使用率
- CPU使用率上位3プロセス
- Network送受信速度
- Disk Read / Write / Busy

## YAML / AI分析

ログは実行ファイルと同じフォルダの `performance-log.yaml` に保存される。

ファイル先頭にはAI向けのコメントを含め、単位、欠損値の扱い、クラッシュ調査時に確認すべき Windows Event Log のイベント名などを記載する。

特にクラッシュ原因調査では、ログの時刻と以下のWindowsイベントを相関させることを想定している。

- WHEA-Logger
- Application Error
- Application Hang
- Display / nvlddmkm
- Disk
- Ntfs
- stornvme
- Kernel-Power

## Portable / Install / Uninstall

通常はZIPを展開して `PerformanceChecker.exe` を直接実行できる。

.NETランタイムを別途インストールする必要がないよう、GitHub Actionsでは self-contained / single-file の `win-x64` ZIPを生成する。

レジストリを変更しないインストール:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

既定のインストール先は `%LOCALAPPDATA%\PerformanceCheckerForWin`。

アンインストール:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

インストーラーはWindowsサービスやレジストリ登録を行わない。したがって、アンインストールはアプリプロセスを終了してインストールディレクトリを削除するだけで完了する。

## Build

```powershell
dotnet restore
dotnet build -c Release
```

Portable single-file:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

GitHub Actionsではこのpublish結果をZIPとしてArtifact化する。

## Requirements

- Windows 11 x64
- 開発時: .NET 10 SDK
- 配布版: .NET runtime不要
- Hardware sensor accessについては、PCやセンサーによって管理者権限が必要になる場合がある

## Planned / Next

- NVMe / SATA SMART・Health情報の起動時取得
- Windows Event Log / WHEA の時系列収集
- CPU thermal / power-limit / throttling 状態の強化
- GPU詳細センサーの強化
- SSD Healthを含むAI向け診断サマリー

<!-- CI trigger test: 2026-08-15 -->
