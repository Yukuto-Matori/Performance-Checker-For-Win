# Performance-Checker-For-Win

Windows 11 向けの C# / .NET CLI PC 診断・性能ログツール。

目的:

- 不定期なアプリクラッシュの原因調査
- CPU / RAM / GPU / SSD / Network の状態記録
- Windows PC 買い替え前後の比較用データ生成
- 生成した YAML をそのまま AI に渡して分析できる形式にする

## Planned features

- 起動時のハードウェア情報取得
- 1 分間隔のリソース監視
- CPU / GPU 温度・使用率
- Top 3 CPU 使用プロセス
- RAM 使用率
- Network I/O
- Disk I/O
- NVIDIA GPU / VRAM 情報
- Windows Event Log / WHEA の収集
- YAML 出力
- `L` キーでログを Notepad に表示
- AI 向け YAML 読み取りコメント

## Requirements

- Windows 11 x64
- .NET 10 SDK / Runtime
- Hardware sensor access のため、必要に応じて管理者権限で実行

## Build

```powershell
dotnet restore
dotnet build -c Release
```

## Run

```powershell
dotnet run -c Release
```

Release 配布用:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## Controls

- `L` : 現在の YAML ログを Notepad で開く
- `S` : 即時スナップショット
- `Q` : 終了

既定のサンプリング間隔は 60 秒。将来的に CLI オプションで変更可能にする。
