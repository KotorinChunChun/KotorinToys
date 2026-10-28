# MdVisualizerの導入と管理

KotorinToys 0.1.16以降では、MdVisualizerの署名付きMSIXをGitHub公開版から取得できます。本体を更新後、「状態を更新」を押し、MdVisualizerを選んでインストールしてください。Markdown・図表の閲覧と編集に使います。

常駐や自動起動は行いません。「起動」は画面を開き、既に起動していれば同じプロセスの画面を復元します。最後の画面を閉じると終了します。「停止」は全画面へ正常終了を依頼し、未保存・保存中・編集中の場合は終了しません。

既存MSIXのIdentity `com.mdvisualizer.viewer`と設定を維持します。更新前の正常終了を確認できないときは更新を中止するため、管理連携に未対応の旧版は作業を保存して自分で終了してください。Windowsが署名を信頼できない場合の証明書登録は別操作です。本体は証明書を自動登録しません。

[MdVisualizerの公開版](https://github.com/KotorinChunChun/MdVisualizer/releases)、[使い方](https://github.com/KotorinChunChun/MdVisualizer/blob/main/docs/USERS_GUIDE.md)も参照してください。
