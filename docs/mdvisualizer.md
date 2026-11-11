# MdVisualizerの導入と管理

MdVisualizerは、Markdownや図表の閲覧・編集に使うアプリです。KotorinToys 0.1.16以降では、GitHubで公開されている署名付きMSIXを取得できます。本体を更新したら「状態を更新」を押し、MdVisualizerを選んでインストールしてください。

「このMSIXの固定製品識別には未対応です」と表示された場合は、本体の版を確認してください。0.1.11などの旧本体では一覧に表示できてもMdVisualizerを導入できません。「本体アップデート」で0.1.16以降へ更新してください。

常駐や自動起動は行いません。「起動」は画面を開き、既に起動していれば同じプロセスの画面を復元します。最後の画面を閉じると終了します。「停止」は全画面へ正常終了を依頼し、未保存・保存中・編集中の場合は終了しません。

更新しても、既存MSIXのIdentity `com.mdvisualizer.viewer`と設定は保持されます。本体は、MdVisualizerが正常に終了したことを確認できない場合、更新を中止します。管理連携に未対応の旧版を使っている場合は、作業を保存してからMdVisualizerを終了してください。Windowsが署名を信頼できない場合、証明書の登録は別の操作になります。本体は証明書を自動登録しません。

0.0.19.4以降は、設定の「SharePointのURL」にHTTPSのMarkdownファイルURL、WikiライブラリまたはフォルダーURLを指定できます。ライブラリ・フォルダーの場合は一覧からMarkdownを選びます。HTMLページや共有リンクではなく、対象ライブラリ内のURLを使用します。実際の認証・閲覧権限は組織の設定に従います。

同じ設定画面で直前の受信URL・日時・経路・拒否理由を確認し、原文をコピーできます。受信URLはメモリー内だけに保持します。ショートカットは各ボタンのツールチップに表示します。

[MdVisualizerの公開版](https://github.com/KotorinChunChun/MdVisualizer/releases)、[使い方](https://github.com/KotorinChunChun/MdVisualizer/blob/main/docs/USERS_GUIDE.md)も参照してください。
