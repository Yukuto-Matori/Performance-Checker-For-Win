# Performance-Checker-For-Win

Windows 11 向けの C# / .NET PC 診断・性能ログツール。

## 目的

- 不定期なアプリクラッシュの原因調査
- CPU / RAM / GPU / SSD / Network の状態記録
- Windows PC 買い替え前後の比較用データ生成
- 生成した YAML をそのまま ChatGPT などの AI に渡して分析できる形式にする

## 使い方

### 1. ZIP版を使う（推奨）

GitHub Actions で生成された `Performance-Checker-For-Win-win-x64.zip` を任意の場所に展開する。

この配布版は **self-contained / single-file** なので、別途 .NET Runtime をインストールする必要はない。

レジストリを変更せず、そのまま `PerformanceChecker.exe` を起動できる。

### 2. GUI / タスクトレイモード

通常は `PerformanceChecker.exe` をダブルクリックして起動する。

メインウィンドウは表示せず、Windows のタスクトレイに常駐する。

タスクトレイアイコンのメニューから以下を操作できる。

- **ログ表示**: 実行ファイルと同じフォルダの `performance-log.yaml` を別プロセスの既定のテキストアプリで開く
- **監視: ON/OFF**: 定期監視を停止・再開する
- **アプリ＆監視終了**: 監視を停止してアプリを終了する

起動時にはハードウェア情報を1回だけ取得する。監視をOFFにして再度ONにしても、同一起動中は初期ハードウェア情報を再取得しない。

### 3. ログファイル

ログは次のファイルに保存される。

```text
PerformanceChecker.exe と同じフォルダ\performance-log.yaml
```

YAMLの先頭にはAI向けの読み方・単位・欠損値の扱い・クラッシュ調査時の確認ポイントをコメントとして記載する。

そのため、`performance-log.yaml` の内容をそのまま ChatGPT などに貼り付けて、PCの状態やクラッシュ原因について分析させることを想定している。

### 4. CLIモード

CLIとして起動することもできる。

```powershell
PerformanceChecker.exe --cli
```

監視間隔と実行時間を指定することもできる。

```powershell
PerformanceChecker.exe --cli --interval 10 --duration 3600
```

上記の場合、10秒間隔で3600秒（1時間）監視する。

CLIでは以下のキーを使用する。

- `L`: YAMLログを別プロセスのNotepadで開く
- `S`: 即時スナップショットを取得する
- `Q`: 監視を終了する

オプション:

- `--interval <seconds>`: サンプリング間隔。未指定時は既定値を使用する
- `--duration <seconds>`: 自動終了時間。0または未指定の場合は無期限

## 監視開始時に取得する情報

アプリ起動時に1回だけ、PCの基本構成を取得する。

### CPU

- CPU名
- 物理Core数
- Logical Processor数（Thread数）
- 最大クロック
- OS情報

### GPU

- GPU名
- NVIDIA等のDriver Version
- VRAM容量
- PNP Device IDなどのデバイス識別情報

### メモリ

- 物理RAM総容量

### ディスク / SSD

- 物理ディスクModel
- Serial Number（取得可能な場合）
- Interface
- Media Type
- 容量
- Status
- 論理ドライブ容量
- 空き容量

今後、CrystalDiskInfoに近いSSD診断を目的として、NVMe / SATAのSMART・Health情報も拡張予定。

## 定期監視する情報

既定では一定間隔でPCの状態をサンプリングする。

### CPU

- CPU使用率
- CPU温度（取得可能な場合）
- CPU電力（取得可能な場合）

### プロセス

CPU使用率の高いプロセス上位3件を記録する。

これにより、監視中にバックグラウンド処理などがCPUを占有していなかったか確認できる。

### メモリ

- メモリ使用率

### GPU

- GPU使用率
- GPU温度
- VRAM使用量 / 使用率（取得可能な場合）
- GPU電力（取得可能な場合）

### Network

- ネットワーク送信量 / 送信速度
- ネットワーク受信量 / 受信速度

### Disk I/O

- Disk Read
- Disk Write
- Disk Busy / 使用率（取得可能な場合）

## クラッシュ原因調査での使い方

このツールは、単純なベンチマークだけではなく、**「しばらくPCを使わせておいて、クラッシュ直前に何が起きていたかを見る」**ことを主目的の1つとしている。

例えば、問題のPCで以下のように長時間監視する。

```powershell
PerformanceChecker.exe --cli --interval 60 --duration 28800
```

これは60秒間隔で8時間監視する例。

アプリがクラッシュした場合は、`performance-log.yaml` のクラッシュ前後の時刻を確認し、CPU / GPU / RAM / Disk / Network / プロセスの異常と相関させる。

特にWindows Event Logについて、以下のイベントが発生していないか確認すると原因特定に役立つ。

- `WHEA-Logger`
- `Application Error`
- `Application Hang`
- `Display` / `nvlddmkm`
- `Disk`
- `Ntfs`
- `stornvme`
- `Kernel-Power`

CPU、メモリ、GPU、SSDのどれかを疑う場合でも、ログの時刻を基準に複数の情報を相関させることを推奨する。

## PC買い替え時の性能比較

買い替え前後で同じような負荷をかけながらログを取得し、以下を比較することを想定している。

- CPU使用率と温度
- CPU電力
- GPU使用率、温度、VRAM使用率
- メモリ使用率
- Disk I/O
- Network使用量
- 高負荷プロセス
- CPU / GPU / SSDなどのハードウェア構成

単純な最大性能だけでなく、**同じ作業を行ったときの温度・電力・負荷率・I/O状態**を比較すると、PC全体の安定性を評価しやすい。

## Portable / Install / Uninstall

### Portable

最も簡単なのはZIPを展開して、そのまま `PerformanceChecker.exe` を起動する方法。

レジストリ登録やWindowsサービス登録を行わないため、USBメモリや任意のフォルダからの実行も可能。

### インストール

レジストリを汚さないユーザー単位のインストールが必要な場合は、ZIP内の `install.ps1` を実行する。

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

既定のインストール先は以下。

```text
%LOCALAPPDATA%\PerformanceCheckerForWin
```

### アンインストール

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1
```

Windowsサービスやレジストリ登録を行わないため、基本的にはアプリを終了してインストールディレクトリを削除するだけでアンインストールできる。

## 必要環境

### 配布版

- Windows 11 x64
- .NET Runtime不要

### 開発環境

- Windows 11 x64
- .NET 10 SDK

Hardware sensor accessについては、PCやセンサーによって管理者権限が必要になる場合がある。

## 開発者向けBuild

通常のBuild:

```powershell
dotnet restore
dotnet build -c Release
```

Portable single-file Build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

GitHub ActionsではこのPublish結果をZIPにまとめ、Artifactとして保存する。

## AI分析について

`performance-log.yaml` は、単なる機械向けログではなく、AIによる診断を意識した構造にする。

AIへ渡す場合は、基本的に以下をそのまま貼り付ける。

```text
performance-log.yaml の内容
```

分析時には、特に以下を確認するよう指示するとよい。

1. CPU温度・使用率・電力の異常
2. GPU温度・使用率・VRAM・電力の異常
3. メモリ使用率やメモリ逼迫
4. クラッシュ直前の高負荷プロセス
5. Disk I/OやSSD関連の異常
6. Network負荷
7. 時刻を基準にした複数センサーの同時異常
8. Intel CPUのWHEAなどハードウェアエラーを示す兆候

ただし、YAMLだけでWindowsのハードウェア故障を確定できるわけではない。クラッシュ発生時にはWindows Event Viewerのログ、ミニダンプ、Reliability Monitorなどと組み合わせて調査することを推奨する。

## Planned / Next

- NVMe / SATA SMART・Health情報の起動時取得
- Windows Event Log / WHEA の時系列収集
- CPU thermal / power-limit / throttling 状態の強化
- GPU詳細センサーの強化
- SSD Healthを含むAI向け診断サマリー
- 長時間監視用のログローテーション
- PC買い替え前後を比較するサマリー生成
