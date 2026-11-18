# KotorinToys - ことりのおもちゃ箱

## 何のためのソフトか

KotorinToysは、画面の撮影や音声入力、ファイル整理などに使うKotoriChunのWindowsアプリを、ひとつの画面で管理するソフトです。やりたい作業に合うアプリを探し、対応するアプリのインストール・起動・更新をまとめて行えます。

## できること

- アプリを選んでインストール・アップデート・アンインストールする。
- アプリの起動状態を確認し、画面を開いたりアプリを停止したりする。
- 「通常」「要更新」「未導入」「すべて」で一覧を切り替える。
- サインイン時に起動するアプリを選ぶ。
- 各アプリの使い方やGitHubページを開く。

## アプリ一覧

やりたい作業からアプリを選べるよう、用途ごとに並べています。「ローカル」の表記があるアプリは開発用の一覧に登録されています。インストールや更新に対応しているかは、KotorinToysの画面で確認できます。

| カテゴリ | アプリ | 何のために使うか |
| --- | --- | --- |
| 撮影 | [SSCapture](https://github.com/KotorinChunChun/SSCapture) | 作業画面を画像や動画で残し、撮影したファイルを整理する |
| 撮影 | [OperationRecorder](https://github.com/KotorinChunChun/OperationRecorder) | PCで行った作業を、画面画像付きの手順書にする |
| 撮影・配信 | [CapchunWindow](https://github.com/KotorinChunChun/CapchunScreen) | 操作画面を録画し、キーやマウスの入力をあとから映像と照らし合わせる |
| 編集 | [ChulipVideo C#版](https://github.com/KotorinChunChun/ChulipVideo-CSharp) | 撮影済みの動画から必要な場面を抜き出し、手順書の画像に使う |
| 編集 | [PPPaint](https://github.com/KotorinChunChun/PPPaint) | 画像を補正し、レイヤーを分けて図形や説明を書き足す |
| 文書 | [MdVisualizer](https://github.com/KotorinChunChun/MdVisualizer) | Markdownの文書や図表を読み、表示を確認しながら編集する |
| 学習 | [ShortcutKeyCounter](https://github.com/KotorinChunChun/ShortcutKeyCounter) | よく使うショートカットを把握し、画面共有で押したキーを伝える |
| デスクトップ整理 | [ChunapTool](https://github.com/KotorinChunChun/ChunapTool) | 指定したウィンドウの位置と大きさを数値で揃える |
| デスクトップ整理 | [ChuwitchWindow](https://github.com/KotorinChunChun/ChuwitchWindow)（ローカル） | 複数のモニターに開いたウィンドウを移動・整列する |
| 配信 | [LiveTextMarker](https://github.com/KotorinChunChun/LiveTextMarker) | 画面共有や配信で、見てほしい文字を下線や強調表示で示す |
| 配信 | [VoiceMixerMicRouter](https://github.com/KotorinChunChun/VoiceMixerMicRouter) | 配信や通話に送るマイクなどの音声入力を切り替える |
| 配信 | [VoiceMixerSpkRouter](https://github.com/KotorinChunChun/VoiceMixerSpkRouter) | アプリやブラウザーの再生音を、選んだ2系統の出力先に送る |
| 配信 | [ScriptSync（読みあわせ）](https://github.com/KotorinChunChun/ScriptSync) | 台本の音声を発話ごとに録り直し、採用したテイクを一本の音声にまとめる |
| 音声 | [EchoType](https://github.com/KotorinChunChun/EchoType) | キーボードで文章を打つ代わりに、話した内容をWindowsアプリへ入力する |
| 音声 | [ReadingRepair](https://github.com/KotorinChunChun/ReadingRepair) | 日本語の読み上げで誤読される語を見つけ、音声合成用の辞書を直す |
| 音声 | [TalkRecorder](https://github.com/KotorinChunChun/TalkRecorder) | マイクとスピーカーの音を同時に録音し、時間を揃えて音源ごとに保存する |
| 音声、文字起こし | [TranscriptionServer](https://github.com/KotorinChunChun/TranscriptionServer) | 音声・動画の文字起こしと話者分離を共通APIで提供する |
| 翻訳・文字認識 | [FocusTranslator](https://github.com/KotorinChunChun/FocusTranslator)（ローカル） | コピーできない画面上の文字を取り出し、翻訳や用語の確認に使う |
| データ管理 | [EveryDB](https://github.com/KotorinChunChun/EveryDB) | 別々のデータに散らばった情報を、一か所から検索する |
| データ管理 | [HttpSelector](https://github.com/KotorinChunChun/HttpSelector) | リンク先を確認し、仕事用や個人用など、使いたいブラウザーのプロファイルで開く |
| データ管理 | [SmartPaste](https://github.com/KotorinChunChun/SmartPaste) | エクスプローラーに貼り付けるファイルの名前を、その場で確認して整理する |
| Office | [OfficeAssist](https://github.com/KotorinChunChun/OfficeAssist) | Excelに画像を貼り付けたり、複数のCSV・TSVをまとめて取り込んだりする |
| AI利用状況 | [AgentTasktrayViewer](https://github.com/KotorinChunChun/AgentTasktrayViewer) | CodexとClaude Codeの利用枠があとどれだけ残っているかを通知領域で確認する |

画面を記録するアプリの選び方は[記録ツールの使い分け](docs/recording-tools.md)を参照してください。

各アプリの独自コードと文書にはMITライセンスが適用されます。著作権表示とライセンス本文は各アプリのREADMEから確認できます。第三者のコンポーネントや素材には、それぞれのライセンスが適用されます。

## インストール

対応環境は **Windows 11・x64** です。ダウンロードと公開版の確認にはインターネット接続を使います。

1. [GitHub Releases](https://github.com/KotorinChunChun/KotorinToys/releases)で、利用するリリースの説明を確認します。
2. `KotorinToys-<バージョン>-setup.exe`をダウンロードして実行します。
3. インストールしたKotorinToysを開きます。

本体のインストーラーには必要な.NETランタイムを同梱しています。ソースの取得や開発環境の準備は不要です。開発版と表示されるリリースは、その説明と未確認事項も確認してください。

## はじめて使う

1. 「未導入」または「すべて」で、使いたいアプリを選びます。
2. 「インストール」を押します。本体から導入できる配布物が公開されていない場合、このボタンは押せません。
3. 導入後、「起動 / 設定を開く」でアプリを開きます。
4. 新しいバージョンを探すには「状態を更新」を押します。「要更新」に表示されたアプリは「アップデート」で更新できます。

アプリを終了するときは「停止させる」を押します。起動済みのアプリをもう一度開くと、既存の画面を表示します。操作の詳細は[使い方](docs/USERS_GUIDE.md)をご覧ください。

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
