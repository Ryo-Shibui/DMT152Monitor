# Vaisala DMT152 Dew Point Monitor

DMT152をVaisala USBシリアルケーブル（RS-485 / USB）でWindows PCに接続し、以下をリアルタイム表示・グラフ表示・CSV保存するWinFormsソフトです。

- Dew point / frost point temperature (°C)
- H2O concentration (ppb)

既存のManometer測定ソフトと同様に、COM検出 → 接続 → 常時リアルタイムモニター → Manual/Timer測定 → CSV保存、という操作フローにしています。

## 重要：10 Hzについて

DMT152マニュアルでは内部測定周期のため、シリアル出力の最短更新は**約1秒**です。そのため、DMT152から真の10 Hzデータを取得することはできません。

本ソフトは `SEND` コマンドを約1秒間隔で送信して1回ずつ測定値を取得します。同じ値を10回複製したり補間したりせず、CSVの `time` はPCの高分解能 `Stopwatch` による実際の受信時刻差を使います。


## 電源投入直後の待機

DMT152は電源投入時に自己診断・sensor purgeを行います。USBシリアルケーブルだけで給電している場合、USB接続直後は測定値がすぐ出ないことがあります。GUIはCOM接続後に `Connected / Waiting for measurement` と表示し、最初の有効な測定値を最大90秒待ちます。1点目を受信すると `Connected / Monitoring` に切り替わります。

一度測定が始まった後も、単発の応答遅延では直ちに切断せず、最大30秒まで再試行します。USB切断などの実際のI/Oエラーは即座に接続異常として扱います。

## CSV

列は指定どおり3列のみです。

```text
time,Dew point,H2O ppb
0.998421,-62.34,12.375
2.004817,-62.36,12.250
```

既存ファイルは上書きしません。

## グラフ

- X軸: Measurement Time (s)
- 左Y軸: Dew Point (°C)
- 右Y軸: H2O (ppb)
- Dew pointの左Y軸・目盛・軸タイトル・プロット線は同じ色
- H2O ppbの右Y軸・目盛・軸タイトル・プロット線は同じ色
- 2つのY軸は独立にオートスケール

リアルタイム数値表示にも同じ色を使います。

## H2O ppbと圧力補正

DMT152の水分体積濃度計算には測定点の**絶対圧力**が必要です。そのためGUIに `Process Pressure (bara)` を設けています。

接続時に

```text
UNIT M
XPRES <入力した絶対圧力>
```

を送ります。`XPRES`は一時設定なので、装置の永続設定 `PRES` を書き換えません。

GUI入力範囲は0.1–50 bara、初期値は1.01325 baraです。ゲージ圧ではなく、センサー位置の絶対圧を入力してください。

## DMT152との通信

PuTTYは不要です。ソフト自身がWindows COMポートを使用します。

自動接続では以下を順に試します。

1. 4800 baud / 7N1（添付された旧版マニュアルのデフォルト）
2. 19200 baud / 8N1（旧版マニュアルのUSB代替設定。新しいDMT152ガイドではデフォルト）

通信フロー制御はNoneです。

接続後、装置へ概ね以下を設定します。

```text
S
UNIT M
XPRES <pressure bara>
FORM /
```

その後は約1秒間隔で `SEND` を送り、マニュアルに記載された標準形式

```text
Tdf= -2.0 'C H2O= 5152 ppm
```

を取得します。GUIとCSVではH2Oを `ppm × 1000` によりppbへ変換します。これにより、実機で問題が出たカスタム `FORM` と連続出力 `R` に依存しません。

## 電源について

現在のVaisala DMT152ガイドでは、USBサービスケーブルは設定用途には給電可能ですが、**連続測定では別ケーブルから動作電源を供給することが推奨**されています。長時間測定では外部電源を推奨します。

## 必要環境

- Windows 10/11 64 bit
- Visual Studio 2022
- `.NET desktop development` ワークロード
- .NET 8 SDK
- Vaisala USBシリアルケーブルのドライバ

NI-DAQmxは不要です。

## ビルド

1. `DMT152Monitor.sln` をVisual Studio 2022で開く
2. `Release | x64` または `Debug | x64` を選択
3. `DMT152Monitor` をスタートアッププロジェクトにする
4. ビルドして実行

PowerShellなら、リポジトリ直下で以下でもビルドできます。

```powershell
.\build-release.ps1
```

## 基本操作

1. Vaisala USBシリアルドライバをインストール
2. DMT152 Port II（RS-485）とPCをUSBシリアルケーブルで接続
3. ソフトを起動
4. `Detect / Refresh COM Ports`
5. COMポートを選択
6. `Process Pressure (bara)` にセンサー位置の絶対圧を入力
7. `Connect`
8. Dew PointとH2O ppbがリアルタイム更新されることを確認
9. CSV Directory / File Nameを設定
10. ManualまたはTimerを選択
11. `Start Measurement`
12. Manualの場合は `Stop Measurement` で終了

測定停止後もCOM接続は維持され、リアルタイムモニターは継続します。

## 実機使用前の確認

- WindowsでVaisalaケーブルのCOMポートが見えている
- Connectに成功する
- Dew PointとH2O ppbが継続更新される
- 入力した圧力がセンサー位置の絶対圧である
- CSVの時刻が単調増加し、受信1回につき1行保存される

## 検証について

このパッケージには、パーサー、CSV形式、上書き防止、タイマー設定、常時監視＋ログ切替などの自己テストを含めています。

```powershell
.\run-self-tests.ps1
```

ただし、最終的なCOM通信・USBドライバ・DMT152実機との通信は、実際のWindows PCとDMT152で確認してください。


### 実機互換性のための通信方式
旧版ではカスタム `FORM` と `R` 連続出力を使っていましたが、実機ではカスタムFORMの解釈や連続出力開始に互換性問題が確認されました。現在版では `FORM /` で標準形式へ戻し、`SEND` を約1秒間隔でポーリングします。COM通信の確立後は `Connected / Waiting for measurement` と表示し、最初の有効な測定値が得られるまで最大90秒待機します。


## fixed7: 値が変化しない場合の診断

各 `SEND` の前に `STAT` を取得するようにしました。DMT152はセンサパージや自動校正中、計測出力そのものを意図的に固定します。GUIにはその状態と、実際に新しいシリアル応答を受信するたび増加する `reply #` を表示します。明示的な自己診断中の固定値はリアルタイム表示には出しますがCSVには保存せず、`STAT=0000`（Normal measurement）へ戻ると自動的にCSV記録を再開します。
