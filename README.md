# Vaisala DMT152 Dew Point Monitor

Vaisala DMT152をWindows PCに接続し、露点または霜点温度とH2O濃度をリアルタイム表示、グラフ表示、CSV保存するためのデスクトップソフトです。

PCを買ったばかりでVisual Studioも入っていない状態から使えるように、必要なソフト、装置、配線、ビルド、操作手順をこのREADMEにまとめています。

## 何ができるか

- Vaisala DMT152のシリアル通信値を読み取る
- Dew point / frost point temperatureを表示する
- H2O濃度をppbで表示する
- 手動停止またはタイマー停止でCSV保存する
- 測定中の値を2軸グラフで確認する
- DMT152のsensor purgeやauto-calibration中の固定値を診断表示する

CSVは次の3列です。

```text
time,Dew point,H2O ppb
0.998421,-62.34,12.375
2.004817,-62.36,12.250
```

既存ファイルは上書きしません。

## 専用の装置か

このソフトはVaisala DMT152専用です。DMT152以外の露点計では、その装置が同じシリアルコマンドと出力形式を持たない限り使えません。

準備するもの:

- Vaisala DMT152
- DMT152用のUSBシリアルケーブルまたはRS-485/USB変換ケーブル
- DMT152の動作用電源
- Windows 10またはWindows 11の64 bit PC
- CSV保存先フォルダ

長時間測定では、USBサービスケーブルだけに頼らず、DMT152へ別ケーブルから動作用電源を供給することを推奨します。

## 新品PCで最初に入れるもの

1. Windows Updateを実行する。
2. Visual Studio 2022をインストールする。
3. Visual Studio Installerで `.NET desktop development` ワークロードを選ぶ。
4. .NET 8 SDKをインストールする。Visual Studioのワークロードに含まれていれば追加作業は不要です。
5. Vaisala USBシリアルケーブルのドライバをインストールする。
6. DMT152をPCに接続し、WindowsのデバイスマネージャーでCOMポートが見えることを確認する。

PuTTYやNI-DAQmxは不要です。

## ダウンロードして開く

1. GitHubページの `Code` からZIPをダウンロードする、またはGitでcloneする。
2. ZIPの場合は任意の作業フォルダへ展開する。
3. `DMT152Monitor.sln` をVisual Studio 2022で開く。
4. 画面上部の構成を `Release`、プラットフォームを `x64` にする。
5. スタートアッププロジェクトが `DMT152Monitor` であることを確認する。
6. `Build` -> `Build Solution` を実行する。
7. `Start` またはF5で起動する。

PowerShellでビルドする場合:

```powershell
.\build-release.ps1
```

ビルド後の実行ファイルは通常、次の場所に作成されます。

```text
src\DMT152Monitor\bin\x64\Release\net8.0-windows\DMT152Monitor.exe
```

## DMT152との接続

ソフトはWindowsのCOMポートを直接使います。接続時には次の通信設定を自動で試します。

1. 4800 baud / 7 data bits / no parity / 1 stop bit
2. 19200 baud / 8 data bits / no parity / 1 stop bit

接続後、DMT152には概ね次を送ります。

```text
S
UNIT M
XPRES <pressure>
FORM /
```

測定値は約1秒ごとに `SEND` で取得します。DMT152の内部測定周期の制限により、このシリアル通信では真の10 Hz測定はできません。同じ値を複製して10 Hzに見せる処理はしていません。

## 使い方

1. DMT152とPCをUSBシリアルケーブルで接続する。
2. DMT152に電源を入れる。
3. ソフトを起動する。
4. `Detect / Refresh COM Ports` を押す。
5. DMT152のCOMポートを選ぶ。
6. `Process Pressure (bara)` にセンサー位置の絶対圧を入れる。大気圧付近なら初期値の1.01325で開始できます。
7. `Connect` を押す。
8. Dew PointとH2O ppbが更新されることを確認する。
9. CSVの保存先フォルダとファイル名を設定する。
10. `Manual` または `Timer (s)` を選ぶ。
11. `Start Measurement` を押してCSV保存を開始する。
12. Manualの場合は `Stop Measurement` で終了する。

測定を止めてもDMT152との接続は維持され、リアルタイム表示は継続します。

## H2O ppbと圧力補正

DMT152の露点値そのものには圧力補正は不要ですが、ppmやppbのような体積濃度には測定点の絶対圧が必要です。

このソフトは接続時に `XPRES <pressure>` を送ります。`XPRES` は一時設定なので、DMT152本体の永続設定 `PRES` は書き換えません。

入力する値はゲージ圧ではなく絶対圧です。GUIの入力範囲は0.1から50 baraです。

## 実験前の確認

- WindowsでDMT152のCOMポートが見えている
- `Connect` 後に `Connected / Monitoring` になる
- Dew PointとH2O ppbが継続更新される
- sensor purgeやauto-calibration中は固定値診断が表示される
- 入力した圧力がセンサー位置の絶対圧である
- CSVの時刻が単調増加している
- 既知の条件または別表示器と比べて値が妥当である

## テスト

実機なしで、パーサー、CSV形式、上書き防止、タイマー、監視とログ切替の自己テストを実行できます。

```powershell
.\run-self-tests.ps1
```

または:

```powershell
dotnet run --project .\tests\DMT152Monitor.SelfTests\DMT152Monitor.SelfTests.csproj
```

Pythonが入っている場合は静的チェックも実行できます。

```powershell
python .\tests\static_source_checks.py
```

## トラブルシュート

- COMポートが出ない場合: ケーブルドライバ、USB接続、デバイスマネージャーを確認してください。
- 接続直後に値が出ない場合: DMT152の電源投入直後は自己診断やsensor purgeで最大90秒ほど待つことがあります。
- H2O ppbが期待と違う場合: `Process Pressure (bara)` が絶対圧か確認してください。
- CSVが作れない場合: 保存先フォルダの書き込み権限と、同名ファイルが既に存在しないか確認してください。
- 長時間で通信が不安定な場合: DMT152へ安定した外部電源を供給してください。
