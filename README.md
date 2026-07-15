# INZONE Buds Battery

Sony INZONE Buds（WF-G700N）の左右イヤホンと充電ケースのバッテリー残量を、FINAL FANTASY XIV内に表示するDalamudプラグインです。

USBトランシッターへWindowsのSetupAPI／HID APIで直接アクセスします。INZONE Hubと同時に利用でき、外部HIDライブラリや管理者権限は必要ありません。

> [!IMPORTANT]
> 現在は実機検証中の開発版です。
> 
## 機能

- 左右イヤホンとケースの残量表示
- 左右個別または低い側だけのオーバーレイ表示
- DTRバー表示
- 表示位置、文字サイズ、透明度、警告しきい値などの設定
- トランシッター切断後の自動再接続
- 古い値の識別と危険残量通知
- INZONE Hubとの同時利用

トランシッターは接続中も約2～4分間隔でバッテリーレポートを自発送信することを実機で確認しています。プラグインはHID接続を維持し、このレポートを継続して受信します。

## 動作環境

- Windows 10/11 x64
- XIVLauncher / Dalamud API Level 15
- Sony INZONE Buds USBトランシッター（VID `054C`, PID `0EC2`）

## インストール

現時点ではDalamudのDev Pluginとして使用します。

1. [Releases](../../releases)から`latest.zip`を取得して展開する。
2. Dalamud設定の`Experimental`を開く。
3. `Dev Plugin Locations`へ展開した`InzoneBudsBattery.dll`を追加する。
4. Dev Plugins画面から`INZONE Buds Battery`を読み込む。
5. ゲーム内で`/inzone`を実行して設定画面を開く。

GitHub Releaseをまだ作成していない場合は、ソースからビルドしてください。詳しくは[開発ガイド](docs/DEVELOPMENT.md)を参照してください。

## コマンド

| コマンド | 説明 |
| --- | --- |
| `/inzone` | 設定画面の表示切り替え |
| `/inzone status` | 接続状態と直近の残量を通知表示 |
| `/inzone reconnect` | HID接続を閉じて再接続 |
| `/inzone debug` | 生HIDレポートのデバッグログ切り替え |

## バッテリー更新について

起動直後は最初の自発レポートが届くまで`残量取得待ち`になります。取得後は最後の値を保持し、設定した時間を超えると古い値として表示します。

標準の`HidD_GetInputReport`による定期取得は、実機ではReport ID `02`とゼロだけの非バッテリーレポートを返します。そのため、設定画面の`強制更新`が`電池レポート非対応（自発レポート待受中）`となるのは正常です。これは自発レポートの継続受信には影響しません。

## リポジトリ構成

```text
InzoneBudsBattery/
├─ src/InzoneBudsBattery/       Dalamudプラグイン本体
├─ probes/InzoneBudsHidProbe/   初期HIDプロトコル検証ツール
├─ probes/NativeHidSmokeTest/   ネイティブHIDアクセスの検証ツール
├─ docs/DEVELOPMENT.md          ビルドと開発手順
├─ docs/VALIDATION.md           実機検証結果
└─ InzoneBudsBattery.slnx       プラグイン本体のソリューション
```

## 技術情報

- [開発・ビルド手順](docs/DEVELOPMENT.md)
- [実機検証結果](docs/VALIDATION.md)
- [初期HID Probe](probes/InzoneBudsHidProbe/README.md)

## ライセンス

[MIT License](LICENSE)で公開しています。Copyright (c) 2026 zxe-ll.
