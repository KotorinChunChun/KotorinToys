# PPPaintのコレクション追加

## 依頼と対象

- 依頼: `tools/PPPaint`へ移動済みのChunPaintをPPPaintへ改名し、独立Gitを保ったままKotorinToysへ追加する。
- 親リポジトリの対象版: v0.1.16。既存のMdVisualizer関連作業とは別に、この文書へ計画と検証を記録する。
- 子リポジトリ: `https://github.com/KotorinChunChun/PPPaint`。配布状態は未公開。インストーラー配布やWeb公開は今回の対象に含めない。

## フェーズと受入条件

- [x] 登録: `catalog/tools.json`、`config/tools.json`、`.gitmodules`へ追加する。
- [x] 方針: 非常駐の編集ツールとして`status/show/stop/start`を宣言する。自動起動と無表示常駐を追加しない。
- [x] 試験実装: 製品識別、定義の一致、文書・アイコン、手動起動、停止、自動起動拒否、GitHub参照を検証する登録試験を追加する。
- [x] 実連携試験実装: 隔離設定の実EXEで起動・準備完了・表示・最小化復元・二重起動・正常終了・常駐拒否を確認する。
- [x] 検証: 親ビルド、登録試験、一覧config検証を完了する。既存回帰の未取得子ツールによる制約は下記に記録する。
- [x] 実連携: 子の改名・管理実装・管理用ビルド後に`--pppaint-ipc`を実行する。
- [x] 保存: 子のコミット・push後、親へGitリンクを登録してコミット・pushする。

## 実装

- 管理用EXEは`tools/PPPaint/artifacts/management/PPPaint.exe`、アプリ識別子は`KotorinToys.PPPaint`とする。
- 説明書、ショートカット定義、固有アイコンは子リポジトリ内を参照する。
- 登録試験は他の子リポジトリの取得状況に依存しない形で実施し、全体回帰にも接続する。
- 実連携試験の設定と管理窓口は試験ごとのハッシュで分離し、既存利用中のプロセスへ停止要求を送らない。試験結果は本版の`logs/pppaint-ipc-<識別子>/result.txt`へ保存する。
- `config/tools.json`の既存VoiceMixerSpkRouter定義に同値の`repositoryUrl`が二重宣言されていたため、重複1行を削除する。重複プロパティ禁止の既存検証器で受理できる状態にする。

## 検証結果

- 親のテストプロジェクトを`dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj --no-restore`でビルドし、警告0・エラー0で成功した。
- `KotorinToys.Tests.exe --pppaint-registration-tests`: 22件成功（共通アイコン12件、PPPaintの登録10件）。
- `KotorinToys.Tests.exe --validate-online-catalog config/tools.json`: 17件の一覧定義を既存検証器で受理した。ネットワーク通信・公開は行っていない。
- 今回変更した親ファイルの`git diff --check`は成功した。
- 既存の`--action-availability-tests`、`--online-catalog-tests`、`--repository-button-tests`は、未取得の`tools/TimecardOCR/tool.json`を全体カタログから読み込む段階で停止した。他ツールの配置は今回変更せず、この3経路の全体回帰は未完了として扱う。
- 実IPC試験の初回は、登録と自動起動拒否まで13件成功後、管理用EXEが準備完了前に終了した。Windows Applicationイベントでは`Microsoft.UI.Xaml.dll`、例外`0xc000027b`の起動時障害を確認した。結果は`logs/pppaint-ipc-279a79c74d694c69901625fae1bad042/result.txt`へ保存し、修正後に再確認する。
- この起動障害は`dotnet publish`出力への`App.xbf`、`MainWindow.xbf`、`PPPaint.pri`の不足が原因だった。子のプロジェクトへWinUIリソースを公開出力へ含める処理を追加した後、実EXEの起動、共通タイトル、管理契約2、準備完了を確認した。
- 再試験は17件成功後、`expectedPid:null`の停止要求で応答が返らず停止した（`logs/pppaint-ipc-dac4899cc4ac4ac89d81c9d5bc7b8cfd/result.txt`）。子のJSON解析がnullへ`GetInt32()`を呼ぶ箇所を特定し、拒否応答を返す修正へつなげる。
- 子のnull対応後に実IPC試験を再実行し、27件すべて成功した（`logs/pppaint-ipc-83e88d97290843819580c1e5628c72c0/result.txt`）。通常起動と共通タイトル、版0.2.0・管理契約2・起動経路、準備完了、誤PIDとPID欠落の拒否、未対応要求、既存画面への再起動集約、最小化復元、単体二重起動、正常停止、`--startup`の終了コード2、単体再起動、画面終了時のプロセス終了を確認した。実試験後に試験プロセスを残していない。
- 管理窓口と設定は分離しているが、親の状態検出は同じEXE配置のプロセスも確認するため、管理連携試験と別設定のGUI確認は直列に実施する。
- 未保存の編集がある場合の停止拒否は子側の試験と実操作で確認する。

## ユーザー指示による検証範囲

2026-10-29、追加の機能テストは不要との指示を受けた。完了済みの登録22件・実連携27件とビルド結果を記録し、それ以降の機能試験を追加しない。旧作業のREADME・版別報告・他ツールの変更を本コミットへ混在させず、PPPaintの独立子Gitと親のカタログ・管理試験だけを登録する。


## 採用コミットとpush先

子リポジトリmainのc392c302a4a27463d0d57d9c3ee53e83f9b7df41を採用する。GitHubのmainと同一ハッシュであり、子の作業ツリーはclean。リポジトリは非公開、配布リリースは作成していない。親は作業開始時からのfeature/full-refactoringブランチを維持し、子をmode 160000のGitリンクで登録する。mainへの統合・リリース・導入済みアプリ更新は行わない。
