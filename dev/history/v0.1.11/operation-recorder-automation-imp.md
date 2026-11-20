# OperationRecorder 1.4.1 自動検証の本体側追随

2026-10-24。ユーザーの「Computer Useを使わずコマンドライン等から全機能を動作テストできるようにする」依頼へ対応する。製品側の要求・設計・全ケース・結果は[子1.4.1の実装報告](../../../tools/OperationRecorder/dev/history/v1.4.1/v1.4.1-imp.md)、利用方法は[自動テスト手順](../../../tools/OperationRecorder/docs/testing.md)を正本とする。

- [x] 本体の登録試験と実プロセス試験の期待する開発版を1.4.1へ追随する。公開オンラインconfigは既存の1.3.2を維持する。
- [x] 本体のDebugビルドは警告0・エラー0。通常回帰1,178件、OperationRecorder専用登録19件が成功した。専用ビルド出力は`artifacts/operation-recorder-automation/`。
- [x] 子の独立Gitを維持し、計画`d750fc7`、共有API/CLI/画面分離`54598c3`、記録fixture/runner/台帳`a856249`、Desktop前面化/実機完了`5c6620a`、手順書HTML修正`5f9b468`/`e794b60`を採用する。最終gitlinkは`e794b602e6c0e613d23697ac76cf0381131f5238`。

子では合成編集セッションをGUI/CLIで共有し、専用workspaceのJSON自動操作、隔離プロセス識別子、記録controller/画面のOS境界注入、別PID fixture、一括runner、機能とケースの対応表を整備する。通常アプリが起動中のDesktop試験は入力・撮影前にblockedを返し、そのアプリを停止しない。

初回の`-Suite All`ではCore374件成功・skip0、アプリ/テスト/専用fixtureのRelease buildは警告0・エラー0。ケース表は23機能・254メソッド・414引数行（Core374＋Desktop40）で発見結果と一致し、全機能の対応とケースID一意性を確認した。この時点ではDesktopは利用者用常駐PID38748を検出し実行前blocked、全体終了3だった。

追加指示に従ってPID38748のインストール版1.3.2へexpectedPid付き正常停止を要求し、accepted応答とプロセス終了を確認した。実施するとfixtureの初期前面化不足が判明したため、試験側のみで一時的な入力スレッド接続と最大5秒の初期前面待機を追加。実行中の前面逸脱の中断は維持した。修正後の`-Suite Desktop`は40件すべて成功・失敗0・skip0、summaryはpassed/終了0（所要2分4秒）。証跡は子の`dev/history/v1.4.1/logs/desktop-acceptance-02/`。台帳の再生成ハッシュは一致し、製品コード/Core経路は変更していない。通常アプリと専用fixtureは終了した状態で、Computer Useは使用していない。

```powershell
cd C:\develop\KotorinToys\tools\OperationRecorder
pwsh -NoProfile -File .\dev\scripts\Test-OperationRecorder.ps1 -Suite Core
```

実入力と撮影まで行う場合は`-Suite All`、一覧だけは`-List`を使う。通常アプリ起動中は安全条件でblockedとなる。Computer Useは今回使用せず、今後の必須試験にも含めていない。

今回はsetup生成・外部公開・導入更新を行わない。親のv0.1.9実装報告、他ツールと未追跡ファイルの並行変更は保持する。

2026-10-31の追加修正では、操作前専用user-beforeを全手順に適用し設定読込でも保持、明示した片側が無ければ反対側を差し込まないようにした。HTMLの切替は同じ手順に前後両画像が揃う場合だけ表示し、右上の「閉じる（ESC）」とEscによる解除を追加した。Core381件（失敗0・skip0）と、専用HTMLを読むヘッドレスEdgeの9項目が成功。旧実EXEと修正EXEで報告の再現・解消も確認した。ケース表はCore381＋Desktop40の421引数行へ更新し、別CLIブラウザー試験は件数を分けて記録した。今回Desktop40件の再実施や本体製品コードの変更はなく、本体のビルド/試験は形式的に再実行していない。既存HTMLは修正版で再出力すると反映される。
