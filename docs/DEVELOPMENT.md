# 開発ガイド

## 必要環境

- Windows 10/11 x64
- .NET 10 SDK
- XIVLauncher / Dalamud API Level 15の開発環境
- Sony INZONE Buds USBトランシッター（実機確認時）

## ビルド

リポジトリのルートで実行します。

```powershell
dotnet restore .\InzoneBudsBattery.slnx
dotnet build .\InzoneBudsBattery.slnx -c Release --no-restore
```

主な生成物は次の場所へ出力されます。

```text
src\InzoneBudsBattery\bin\Release\InzoneBudsBattery.dll
src\InzoneBudsBattery\bin\Release\InzoneBudsBattery.json
src\InzoneBudsBattery\bin\Release\InzoneBudsBattery\latest.zip
```

`Dalamud.NET.Sdk 15.0.0`がmanifestと配布ZIPを生成します。プラグインはWindowsのSetupAPI／HID APIを直接使用するため、外部HID DLLは同梱しません。

## Dev Pluginとして読み込む

1. FF14をXIVLauncherから起動する。
2. Dalamud設定の`Experimental`を開く。
3. `Dev Plugin Locations`へビルドした`InzoneBudsBattery.dll`を追加する。
4. Dev Plugins画面からプラグインを読み込む。
5. `/inzone`で設定画面を開く。

再ビルド前にはDalamud上でプラグインをアンロードしてください。実行中のDLLや配布フォルダーがロックされていると、DalamudPackagerが既存ファイルを更新できない場合があります。

## 実装の要点

- バッテリーレポートは64バイトInput Reportを持つ`col03`から受信する。
- レポートヘッダーは`02 12 04`。
- 右、左、ケースの残量はそれぞれbyte 14、16、18。
- チェックサムは`sum(byte[5] ... byte[18]) mod 256`をbyte 19と比較する。
- HID読み取りはDalamudの描画スレッド外で継続する。
- HIDハンドルは共有アクセスで開き、INZONE Hubとの共存を維持する。
- 切断時は列挙からやり直し、アンロード時は読み取りをキャンセルしてハンドルを破棄する。

## ネイティブHID実装

初期版で使用したHidSharp 2.1.0は、FF14プロセス内でHID監視用ウィンドウクラスの登録が競合すると、`HidSharp RegisterClass failed`を未処理例外として発生させることがありました。

バージョン0.2.0.0以降はHidSharpを削除し、`WindowsHid.cs`からWindows APIを直接呼び出します。0.2.1.0以降は、アンロードや再接続に伴う正常な非同期読み取りキャンセルもエラーとして記録しません。

## 検証ツール

- `probes/InzoneBudsHidProbe`: HidSharpを使用した初期プロトコル調査用。プラグイン本体からは独立しています。
- `probes/NativeHidSmokeTest`: プラグインと同じ`WindowsHid.cs`をリンクして、列挙、共有オープン、`HidD_GetInputReport`を確認します。

検証済みの結果は[VALIDATION.md](VALIDATION.md)に記録しています。
