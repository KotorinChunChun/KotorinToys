# 管理機構の検証

追加・更新の作業順と文書更新表は[管理アプリの共通手順](development/managed-app-integration.md)を参照してください。共通管理引数とIPCの正本は[共通管理契約](../contracts/README.md)です。

## 変更に応じた確認範囲

| 変更・確認したいこと | 検証入口と境界 |
| --- | --- |
| 文書のみ | 相対リンク・見出し参照・git diff --check・根拠ソースとの照合。製品ビルド／GUIテストは不要 |
| 配信用config | テストEXEの `--validate-online-catalog config/tools.json` と `--online-catalog-tests`。公開・実導入はしない |
| 新規の標準Inno製品を定義で追加 | `--definition-product-tests`。模擬HTTP・配布物・専用設定で管理／導入導線を検査。実setup受入は別 |
| 起動・停止等の操作可否 | `--action-availability-tests`・`--ui-policy-tests`。未導入・欠落・処理中・未対応と直接呼出し拒否を確認 |
| 自動起動の設定UI／単体連携 | `--startup-form-tests`・`--standalone-startup-tests`。模擬予約と専用設定。実サインインの発火は別 |
| 子の実管理IPC | 下記または子の管理連携ガイドの製品別試験。既存プロセスとデータを保護し、試験所有PIDだけを正常停止 |
| 公開配布物 | [公開前検査](security.md)と製品別のRelease手順。ネットワーク照合・匿名取得・実導入・UI・機能の受入は別々に記録 |

これらのスイッチは **KotorinToys.Tests.exe向け** です。製品EXEのCLIではありません。テストEXEの作成と全回帰の入口は[コード検証](architecture.md#配布物を作らないコード検証)にあります。引数が存在すること、対象採用版、必要な子ソース・依存を確認してから使用してください。未実施・SKIPは合格に数えません。

## 既存の統合試験

通常の設定・予約を変更しない共通検査は、リポジトリルートで次を実行します。

```powershell
cd C:/develop/KotorinToys
dotnet run --project tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/f1-tests
```

本体の管理窓口が使用中でないとき、`-- --host-lifetime`を付けると、本体の無表示起動・画面の開閉・明示終了も検査します。通常起動予約や子ツールの設定は変更しません。

`-- --live`は停止中の3ツールを順に起動し、無表示、同一PID、実画面、前面PID、正常停止を検査します。自分で起動したものはfinallyで正常停止します。前面化には対話操作の権限が必要で、バックグラウンドCLIだけでは失敗する場合があります。この失敗を成功へ補完せず、前面の管理画面で開く／行ダブルクリックを実操作して別の結果を記録します。

本体の`--self-test`は、既存ツールを変更せずSKIPとして記録し、停止中のツールだけを無表示で起動・再起動・正常停止して元へ戻します。SKIPは合格ではありません。結果は`dev/history/v0.1.0/logs/<実行識別子>/`へ保存します。

画面検証時の起動コマンド：

```powershell
cd C:/develop/KotorinToys
dotnet run --project src/KotorinToys/KotorinToys.csproj -- --root C:/develop/KotorinToys
```

管理画面を閉じても管理部と子ツールは継続します。本体の明示終了は通知領域の「KotorinToysを終了」です。
