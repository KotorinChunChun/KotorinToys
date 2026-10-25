# 管理連携と製品別の検証

[開発者ガイド](../DEVELOPERS_GUIDE.md)へ戻る。共通のCLI／IPCは[管理契約](../../contracts/README.md)、本体の通常回帰は[検証の入口](../testing.md)が正本です。

## 共通の進め方

1. 採用した子のtool.json、独立Git、実際の管理EXE、設定・履歴の保存先を確認します。
2. 通常起動の画面表示、startupによる無表示、既存PID維持、show、status、stopを別々に確認します。
3. 専用設定・架空データを使い、既存プロセスがあれば開始しないなど、各検証の前提を守ります。
4. 正常終了の拒否や未確認を成功扱いにしません。試験所有PIDだけを正常停止し、未保存作業・録画・音声を保護します。
5. 実OS導入、実サインイン、録画・音声・外部サービス接続は、それぞれ別の受入として記録します。

新規の文書名・引数・配布先を推測しません。README、manual、shortcutsは実在する正本を参照し、ショートカットがない製品へ架空の一覧を要求しません。

## テスト実行ファイル

```powershell
cd C:\develop\KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/integration-check
```

以下のスイッチは生成したKotorinToys.Tests.exe用であり、製品EXEの共通CLIではありません。対象の子ビルドと試験条件を確認してから実行します。

| 対象 | スイッチ／入口 | 前提と区別 |
| --- | --- | --- |
| CapchunWindow・ChulipVideo・ChunapTool | --new-tools-ipc、--new-tools-panel | 専用設定・passive等で実録画や外部窓操作を抑止。パネル表示は製品機能の受入ではない |
| ShortcutKeyCounter | --shortcut-ipc、--shortcut-panel | Debugの専用DBを使用し入力Hook・オーバーレイ等を抑止。既存通常プロセスがある場合は実行しない |
| HttpSelectorの開発IPC | --http-selector-ipc | target/managementの隔離Debug。正式MSIXの導入確認とは別 |
| HttpSelectorのMSIX | --http-selector-msix | 署名付き実パッケージの登録が必要。導入済みユーザーデータを使うため別途退避・受入条件を確認 |
| EveryDB | --everydb-ipc | 子の専用管理ビルドを使用。通常DB・WebView2プロファイルと分離 |
| VoiceMixerMicRouter | --mic-router-ipc | 専用設定で送信停止・全出力OFF・RVC無効。実音声の受入ではない |
| VoiceMixerSpkRouter | --spk-router-ipc | 専用設定と停止状態で管理連携を確認。実デバイス・拡張は別 |
| OperationRecorder | --operation-recorder-ipc、--operation-recorder-panel | 専用設定。操作の記録そのものは別の受入 |
| TimecardOCR | 子のREADMEと管理連携ガイド | C#ランチャーとPython／OCRモデルの準備を区別。画像・校正・出力は専用データで検証 |
| ScriptSync | 子の管理連携ガイド | 画面内包の管理用EXEと単体MSIX／Web版を区別。原稿・録音は試験素材へ流用しない |

例：

```powershell
./artifacts/integration-check/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe --new-tools-ipc
```

旧製品の`--self-test`は固定9件を前提とするため、現行catalog全件の検証入口として使いません。前提が一致しないまま正常な全件試験と案内しないでください。

## 管理用ビルドと個別資料

- C#追加3件は各子のartifacts/management、OperationRecorderも専用artifactsへビルドします。
- HttpSelectorは署名付きMSIXが通常利用の正本です。管理試験用EXEへ正式起動をフォールバックしません。
- Mic／SpkのRust出力とSpkの管理配置コピーはbuild.ps1で確認します。モデル・利用者設定は本体へ同梱しません。
- ScriptSyncの管理用ビルドは子のbuild-debug-ScriptSync-kotorintoys.ps1で行います。単体配布のMSIX識別を変更しません。
- 子の検証内容と配布方法は、各独立リポジトリの開発者ガイド・管理連携ガイドを参照します。

## 登録・説明書の同梱

`test-voice-router-registration.ps1`と`test-operation-recorder-registration.ps1`は、専用出力へ本番の説明書同梱処理を実行して内容を確認します。EXEの発行・導入・実音声・実記録の検証ではありません。`--operation-recorder-registration-tests`は定義と操作可否、`--local-catalog-tests`はローカル一覧とキャッシュ保持を確認します。

`--voice-router-public-check`は実GitHubの公開台帳を読む試験です。通信制限・未照合・匿名取得と認証付き取得を区別し、実インストールの成功とは扱いません。

## 結果の配置

生ログ・動画・生成結果は対象版の`dev/history/vX.Y.Z/logs/<実行識別子>/`へ保存します。過去の件数を再掲せず、今回実行した試験と未実施の境界を版別報告へ記録してください。
