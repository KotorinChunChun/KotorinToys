# OperationRecorderの開発フォルダー整理・本体参照更新

2026-11-03。ユーザーの整理依頼に従い、本PCのPROJECT_RULEと本体の文書方針へ合わせた。OperationRecorderは独立Gitを維持し、製品版は0.1.6のままとする。

## 変更

- 子の本体をsrc/OperationRecorder、テストと専用fixtureをtests、配布資産をtemplates/scripts、旧PowerShell入口をtools/legacy-recorderへ移した。
- READMEを利用者向けへ整理し、docs/USERS_GUIDE.md・DEVELOPERS_GUIDE.mdとdev/README.mdを正本/目録にした。開発用ビルドをdev/scriptsへ移し、生成先をartifacts/releaseへ集約した。
- 親の開発ビルドと共通配布のProjectパスを更新し、本体同梱マニュアルのコピー元をdocs/USERS_GUIDE.mdへ変更した。既存配布先operation-recorder/MANUAL.mdは互換コピーとして維持する。対応する既存検証スクリプトの参照も更新した。
- 採用子コミット：6a51daa3683ee49d333262d810c6e5556b5c7927

## 検証

子の3プロジェクトのRelease、管理用Debug、親本体とテストのDebugコンパイルに成功（NuGetAudit=false、警告0/エラー0）。子のself-contained発行・単体ZIP生成を行い、アイコン、テンプレート、config、スクリプト、3か所のマニュアルを照合した。単体ZIP生成時の通常NuGet監査には通信不可のNU1900警告があり、オンライン脆弱性照合は未完了。

親のOperationRecorder説明書同梱分岐を取り出したコピー確認、現行文書・同梱文書のリンク、変更PowerShellの構文、Project参照の実在を確認した。機能試験は前回指定を引き継いで実行していない。製品EXEの起動、GUI、公開、導入済みプログラムの更新も実施していない。

子は追跡済み116ファイルを移動し、C#97ファイルは本体不変・テストの探索パスだけ修正。旧生成物101ファイルを子artifactsへ退避してSHA-256一致を確認した。詳細と生ログの所在は子のdev/history/v0.1.6/v0.1.6-imp.mdを参照する。他製品と親の既存未コミット差分は取り込まない。

## 整理後の再ビルド確認

2026-11-03の追加依頼で、子の本体・xUnit・専用fixtureを新規出力先へDebug/Releaseの両構成でRebuildし、警告0/エラー0を確認した。通常通信でNuGetを強制再評価しており、監査を無効化していない。単体ZIPの生成と主要同梱物の確認も成功したが、既存portable復元領域にはNU1900警告が残った。製品コードに修正が必要な不具合は検出されていない。動作テスト・起動・公開・導入更新は未実施。結果を記録した子コミット383e75fを採用する。
