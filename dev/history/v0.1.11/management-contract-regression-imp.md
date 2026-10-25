# 管理契約の照合修正と全体回帰

2026-10-25。MdVisualizer追加後に停止した全体回帰を、CapchunWindowの管理契約不一致も修正して最後まで実行する依頼。

## 計画と進捗

- [x] 不一致の内容と既存の未コミット修正を調査。CapchunWindowは要求上限4096文字・応答書込み期限を追加済み。ChunapToolも要求長制限・不正要求後の復旧・二重起動時の理由表示を持つ。
- [x] フェーズ1：製品側の安全処理を保持し、標準・上限付き・復旧対応の正本を明示してコピーを照合する。子の境界・停止ガード試験と本体の契約照合を実行し、対象差分だけコミット。
- [x] フェーズ2：専用ビルド先で本体の全体回帰を最後まで実行。途中で見つかった失敗を調査・修正し、結果を記録してコミット。

## 変更範囲

既存の`contracts/ManagementEndpoint.Bounded.cs`・`ManagementEndpoint.Resilient.cs`、照合テスト・契約説明の未コミット草案を確認して採用する。旧実装へ戻したり、照合そのものを省略したりしない。子Git、利用者の設定・起動中アプリを保護する。READMEや旧版資料など無関係な差分は採用しない。

## 検証結果

生ログは`logs/management-regression-20261025/`へ保存。公開・導入更新・実OS予約の変更は対象外。

- 契約照合の修正は並行作業の`ff116fa`で採用済み。内容をレビューし、現在の本体で管理契約照合59件が成功。CapchunWindowの`c412cea`も境界試験4件と待機時の停止許可・3状態の停止拒否・無表示の検証が成功。
- CapchunWindowの最終ビルドは警告0・エラー0。初回はsandbox内のNuGet情報取得がNU1900で失敗し、同じ設定を許可環境で実行して成功。
- 全体回帰の初回は、並べ替え試験の先頭製品が公開URL未設定のAgentTasktrayViewerとなり、試験から実際の案内ダイアログを開いて待機。専用試験EXEのPID17764・実パスを記録して終了。`DashboardViewTests`へ通知受取を注入し、URLがあれば開き、なければ選択製品名と未設定理由を通知することを検証する。製品コードは変更しない。
- 修正後のDebug build成功（エラー0、NuGet情報取得不可のNU1900警告2件）。全体回帰を先頭から再実行し、**1,202件すべて成功、終了コード0**。最後の本体製品定義・Assembly版の一致確認まで到達した。`full-regression-02.log`が最終成功ログ。初回の中断ログと混同しない。
- 全体回帰には契約照合、MdVisualizerの登録、名前付きpipeのUI応答、停止ガード、配布・導入・更新・修復・削除の模擬経路、自動起動の所有条件と復旧、各フォームの判定、キャッシュ・再起動証跡の試験を含む。`--live`・`--scheduler`・`--installed-startup`など実環境操作用の追加オプションは使っていない。

## 再実行

親ルートから以下を実行する。専用出力先を使い、利用中の製品EXEを上書きしない。

```powershell
dotnet build tests/KotorinToys.Tests/KotorinToys.Tests.csproj -c Debug --artifacts-path artifacts/management-regression-20261025
./artifacts/management-regression-20261025/bin/KotorinToys.Tests/debug/KotorinToys.Tests.exe
```

これは本体の通常の全体回帰であり、物理録画・実サインイン・実配布物の受入とは別の証跡である。今回の試験は利用者の起動中アプリ・設定・OS予約を変更していない。
