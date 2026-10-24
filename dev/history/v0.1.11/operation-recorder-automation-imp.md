# OperationRecorder 1.4.1 自動検証の本体側追随

2026-10-24。ユーザーの「Computer Useを使わずコマンドライン等から全機能を動作テストできるようにする」依頼へ対応する。製品側の要求・設計・全ケース・結果は[子1.4.1の実装報告](../../../tools/OperationRecorder/dev/history/v1.4.1/v1.4.1-imp.md)、利用方法は[自動テスト手順](../../../tools/OperationRecorder/docs/testing.md)を正本とする。

- [x] 本体の登録試験と実プロセス試験の期待する開発版を1.4.1へ追随する。公開オンラインconfigは既存の1.3.2を維持する。
- [x] 本体のDebugビルドは警告0・エラー0。通常回帰1,178件、OperationRecorder専用登録19件が成功した。専用ビルド出力は`artifacts/operation-recorder-automation/`。
- [x] 子の独立Gitを維持し、計画`d750fc7`、共有API/CLI/画面分離`54598c3`、記録fixture/runner/台帳`a856249`を採用する。最終gitlinkは`a856249020ca1bae42e8aeba990ddd857e2fe536`。

子では合成編集セッションをGUI/CLIで共有し、専用workspaceのJSON自動操作、隔離プロセス識別子、記録controller/画面のOS境界注入、別PID fixture、一括runner、機能とケースの対応表を整備する。通常アプリが起動中のDesktop試験は入力・撮影前にblockedを返し、そのアプリを停止しない。

最終の`-Suite All`ではCore374件成功・skip0、アプリ/テスト/専用fixtureのRelease buildは警告0・エラー0。ケース表は23機能・254メソッド・414引数行（Core374＋Desktop40）で発見結果と一致し、全機能の対応とケースID一意性を確認した。Desktopは利用者用常駐PID38748を検出し実行前blocked、全体終了3で未実行を合格にしていない。実入力/撮影40件の成功は今回確認していない。試験後の利用者用プロセスは同一PID/開始時刻を維持している。

```powershell
cd C:\develop\KotorinToys\tools\OperationRecorder
pwsh -NoProfile -File .\dev\scripts\Test-OperationRecorder.ps1 -Suite Core
```

実入力と撮影まで行う場合は`-Suite All`、一覧だけは`-List`を使う。通常アプリ起動中は安全条件でblockedとなる。Computer Useは今回使用せず、今後の必須試験にも含めていない。

今回はsetup生成・外部公開・導入更新を行わない。親のv0.1.9実装報告、他ツールと未追跡ファイルの並行変更は保持する。
