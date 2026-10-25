# 管理機構の検証（開発者向け）

[開発者ガイド](DEVELOPERS_GUIDE.md)へ戻る。子の前提と製品別の手順は[管理連携の検証](development/tool-integration.md)、コードの責務は[アーキテクチャ](architecture.md)を参照してください。

## 通常回帰

リポジトリルートから実行します。結果の件数は、そのときの採用コードと出力で確認して版別報告へ記録します。

```powershell
cd C:/develop/KotorinToys
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj --artifacts-path artifacts/code-tests
./artifacts/code-tests/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe
```

通常のfixtureを使う回帰と、実アプリやOS予約を使う追加試験を区別します。子のソース・定義が必要な検査は、[ソース準備](development/getting-started.md)を完了してから行ってください。

## 実環境を使う追加試験

| 入口 | 対象と条件 |
| --- | --- |
| テストEXEの--host-lifetime | 本体の無表示起動・画面の開閉・明示終了。本体の管理窓口が使用中でないこと |
| テストEXEの--live | 初期3ツール向けの無表示・既存PID・画面・前面化・正常停止。全収録アプリの受入とは別 |
| テストEXEの--scheduler | 専用GUID付きタスクを作成・解除するScheduler試験 |
| test-refactoring.ps1 | 本体と初期3ツールのビルド・試験。Host対象にもScheduler・lifetime試験を含む |

開始前に各入口の前提と副作用を確認し、利用者設定・予約・録画・音声を保護します。既存プロセスのSKIPは合格ではありません。試験が起動したPIDだけを正常停止し、前面化が許可されない環境の失敗を成功へ補完しません。

test-refactoring.ps1の`-Version`は記録版です。省略時の旧既定値へ新しい結果を混在させず、catalogの版等を確認して明示します。[記録版を指定する例](architecture.md#回帰検証)を参照してください。

旧製品の`--self-test`は固定9件と旧出力先を前提とし、現行catalog全件の試験には使いません。現行の推奨入口は上の通常回帰と製品別の試験です。文書更新だけで旧コードを修正したとは扱いません。

## 画面・公開・導入の区分

画面プレビューは[操作改善の設計](development/operation-improvements.md)、導入・自己更新は[配布の手順](development/distribution.md)と各専用仕様を参照します。

ビルド、模擬HTTP、公開メタデータ照合、実ファイル取得、実導入、実GUI、実サインイン、録画・OCR・音声は別の結果として記録します。書類を整理するだけの作業では、これらを形式的に実行しません。

管理画面を閉じても管理部と子アプリは継続します。本体を終了する操作は通知領域の「KotorinToysを終了」です。開発用の画面を開く手順は[専用設定での起動](development/getting-started.md#専用設定での開発起動)にまとめています。