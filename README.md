# KotorinToys

Windows用の便利なアプリを、ひとつの画面からインストール・起動・更新できるツールです。

## できること

- アプリを選んでインストール・アップデート・アンインストールする。
- 起動中か確認し、画面を開く・停止する。
- 「通常」「要更新」「未導入」「すべて」で一覧を切り替える。
- サインイン時に起動するアプリを選ぶ。
- 各アプリの使い方やGitHubページを開く。

## インストール

対応環境は **Windows 11・x64** です。ダウンロードと公開版の確認にはインターネット接続を使います。

1. [GitHub Releases](https://github.com/KotorinChunChun/KotorinToys/releases)で、利用するリリースの説明を確認します。
2. `KotorinToys-<バージョン>-setup.exe`をダウンロードして実行します。
3. インストールしたKotorinToysを開きます。

本体のインストーラーには必要な.NETランタイムを同梱しています。ソースの取得や開発環境の準備は不要です。開発版と表示されるリリースは、その説明と未確認事項も確認してください。

## はじめて使う

1. 「未導入」または「すべて」で、使いたいアプリを選びます。
2. 「インストール」を押します。対応する公開配布物がないアプリでは押せません。
3. 導入後、「起動 / 設定を開く」でアプリを開きます。
4. 新しい版を確認するときは「状態を更新」を押します。「要更新」に表示されたアプリは「アップデート」で更新できます。

アプリを終了するときは「停止させる」を押します。起動済みのアプリをもう一度開くと、既存の画面を表示します。操作の詳細は[使い方](docs/USERS_GUIDE.md)をご覧ください。

## アプリの例

一覧は配信情報に応じて更新されます。導入・更新などの対応状況は、本体の表示で確認してください。

| アプリ | 用途 |
| --- | --- |
| [LiveTextMarker](https://github.com/KotorinChunChun/LiveTextMarker) | 画面上の文字に下線や強調表示を付ける |
| [SmartPaste](https://github.com/KotorinChunChun/SmartPaste) | ファイルを貼り付けるときに名前を確認・整理する |
| [SSCapture](https://github.com/KotorinChunChun/SSCapture) | 画面を撮影・録画し、保存先やファイル名を管理する |
| [ShortcutKeyCounter](https://github.com/KotorinChunChun/ShortcutKeyCounter) | ショートカットキーの使用回数を集計し、入力を表示する |
| [CapchunWindow](https://github.com/KotorinChunChun/CapchunWindow) | 操作風景と入力イベントを動画・ログに残す |
| [ChulipVideo C#版](https://github.com/KotorinChunChun/ChulipVideo-CSharp) | 動画から手順書用の画像を切り出す |
| [ChunapTool](https://github.com/KotorinChunChun/ChunapTool) | ウィンドウの位置・サイズを調整する |
| [HttpSelector](https://github.com/KotorinChunChun/HttpSelector) | リンクを確認し、ブラウザーのプロファイルを選んで開く |
| [EveryDB](https://github.com/KotorinChunChun/EveryDB) | 複数のデータソースをまとめて検索する |
| [VoiceMixerMicRouter](https://github.com/KotorinChunChun/VoiceMixerMicRouter) | マイクなどの入力を切り替え、複数の音声出力へ送る |
| [VoiceMixerSpkRouter](https://github.com/KotorinChunChun/VoiceMixerSpkRouter) | アプリやブラウザーの音声を複数の再生先へ送る |
| [OperationRecorder](https://github.com/KotorinChunChun/OperationRecorder) | PC操作を記録し、画像付きの手順書を作る |
| [ScriptSync（読みあわせ）](https://github.com/KotorinChunChun/ScriptSync) | 台本を発話ごとに録音し、採用した音声を結合する |

画面を記録するアプリの選び方は[記録ツールの使い分け](docs/recording-tools.md)を参照してください。

## 設定とデータについて

- アップデートや本体からのアンインストールでは、設定・履歴を保持します。
- 自動起動は「本体設定・自動起動」で設定します。アプリを停止する操作とは別です。
- 本体を閉じても、起動した各アプリは動作を続けます。
- アプリによってはFFmpegなどの追加準備が必要です。「使い方を開く」で確認できます。

本体自身の更新は「本体アップデート」から行います。[本体の更新手順](docs/self-update.md)をご覧ください。

## 操作に迷ったら

押せないボタンにマウスを当てるか、一覧の「詳細」を開くと理由を確認できます。録画中・保存中などで停止できないときは、アプリ側の作業を終えてから再操作してください。

- [詳しい使い方](docs/USERS_GUIDE.md)
- [自動起動の設定](docs/startup-settings.md)
- [よくある質問・エラーの対処](docs/FAQ.md)
- [説明書の目次](docs/README.md)

開発に参加する方は[開発者ガイド](docs/DEVELOPERS_GUIDE.md)を参照してください。

作者：KotoriChun