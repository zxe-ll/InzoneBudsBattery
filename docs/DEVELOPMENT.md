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
- 左、右、ケースの残量はそれぞれbyte 14、16、18。
- 片耳使用時の未接続側は`0xFF`で、残量なし（`null`）として扱う。
- チェックサムは`sum(byte[5] ... byte[18]) mod 256`をbyte 19と比較する。
- HID読み取りはDalamudの描画スレッド外で継続する。
- HIDハンドルは共有アクセスで開き、INZONE Hubとの共存を維持する。
- 接続直後と設定間隔ごとにReport ID `02`のSony HCI電池GET要求を送信する。
- GET要求へ応答しない場合や書込み共有オープンに失敗した場合は、Input Reportの受動待受へ戻る。
- 再接続やアンロードとGET送信が競合して発生する`ObjectDisposedException`は正常なキャンセルとして扱う。
- 切断時は列挙からやり直し、アンロード時は読み取りをキャンセルしてハンドルを破棄する。

## ネイティブHID実装

初期版で使用したHidSharp 2.1.0は、FF14プロセス内でHID監視用ウィンドウクラスの登録が競合すると、`HidSharp RegisterClass failed`を未処理例外として発生させることがありました。

バージョン0.2.0.0以降はHidSharpを削除し、`WindowsHid.cs`からWindows APIを直接呼び出します。0.2.1.0以降は、アンロードや再接続に伴う正常な非同期読み取りキャンセルもエラーとして記録しません。

Sony HCI電池GETのフレーム形式は、MIT Licenseで公開されている[LINZONE HubのAiroha実装](https://pkg.go.dev/github.com/patyhank/linzone-hub/internal/protocol/airoha)を参考にし、Windows実機の応答で確認しています。

## 検証ツール

- `probes/InzoneBudsHidProbe`: HidSharpを使用した初期プロトコル調査用。プラグイン本体からは独立しています。
- `probes/NativeHidSmokeTest`: プラグインと同じネイティブHID実装をリンクして、列挙、共有Read/Writeオープン、Sony HCI電池GET、`HidD_GetInputReport`を確認します。

検証済みの結果は[VALIDATION.md](VALIDATION.md)に記録しています。
