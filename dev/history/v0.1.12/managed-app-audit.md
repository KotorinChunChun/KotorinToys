# 全管理アプリの規約点検

対象版は本体0.1.12。2026-10-24、開始時の開発一覧14件を固定する。元作業ツリーの未コミット差分は保持し、各子の独立点検ブランチで修正する。未登録MdVisualizerや、点検開始後に追加された別製品は自動追加しない。公開・実導入・利用者設定の書換え・既存常駐の停止は対象外。

規約の正本：[管理アプリの追加・更新手順](../../../docs/development/managed-app-integration.md)、[管理契約](../../../contracts/README.md)。

## 一巡目

静的検査器の回帰5件成功後、first-validated.jsonに14製品・31指摘を保存した。初期のfirst.jsonには検査器自身の配列比較の誤判定が含まれるため正式件数に使用しない。生ログはlogs/managed-app-audit/に置く。表の動作指摘は別途ソース経路を読んだ結果であり、機械検査だけから実動作合格を推測しない。

| 製品 | 点検開始時の子HEAD／定義版 | 一巡目の主な確認・指摘 | 二巡目 |
| --- | --- | --- | --- |
| LiveTextMarker | bfc85c6 / 1.1.0 | 引数なし表示・startup時の重複表示抑止あり。専用連携ガイドと入口がない | 静的整合OK（対象検証は下表） |
| SmartPaste | 3afd956 / 0.1.5 | startup／hidden／旧trayの抑止あり。専用連携ガイドと入口がない | 静的整合OK（対象検証は下表） |
| SSCapture | adf85cb / 0.1.5 | 共通起動方針の実装あり。親採用・configと子の版が乖離、専用連携ガイドがない | 静的整合OK（対象検証は下表） |
| ShortcutKeyCounter | aac94cc / 0.1.4 | Cargo製品版は0.1.5。重複起動通知が無表示引数の判定より前 | 静的整合OK（対象検証は下表） |
| CapchunWindow | c4dc2a5 / 0.1.2 | startupでも二重起動時にShowExisting。ガイドに未公開の旧説明と他製品の引数が混在 | 静的整合OK（対象検証は下表） |
| ChulipVideo | fcabdf9 / 0.2.1 | startupでも二重起動時にShowExisting。起動元と共通規約の説明が不足 | 静的整合OK（対象検証は下表） |
| ChunapTool | d4ccb91 / 0.1.2 | startupでも二重起動時にShowExisting。設定を持たない製品へsettings-dirの説明が混在 | 静的整合OK（対象検証は下表） |
| HttpSelector | 028b599 / 0.0.3.7 | MSIX識別と4成分版を保持。親版と子版、README導線の不整合。startupの重複転送経路を要確認 | 静的整合OK（対象検証は下表） |
| EveryDB | d8c2953 / 0.1.2 | 親版と子版の差、ガイドがconfigをローカル用と説明。単体多重起動と独自Run登録経路を要確認 | 静的整合OK（対象検証は下表） |
| VoiceMixerMicRouter | a958bde / 1.0.19 | 共通引数解析・起動元あり。ガイドの版が1.0.18、通常起動と本体登録の説明が古い | 静的整合OK（対象検証は下表） |
| VoiceMixerSpkRouter | 4c73b8f / 1.0.11 | 共通引数解析あり。ガイドのsetup版が1.0.10、本体側の固定表追加説明が古い | 静的整合OK（対象検証は下表） |
| OperationRecorder | f37049c / 1.3.2 | 通常／startupの既存起動区別あり。旧互換CLIと共通管理CLI、起動元の説明不足 | 静的整合OK（対象検証は下表） |
| TimecardOCR | 8cec2ee / 1.0.0 | startupでも二重起動時にShowExisting。Pythonの独自窓タイトルが共通形式外 | 静的整合OK（対象検証は下表） |
| ScriptSync | 5024bb1 / 0.0.6.3 | 能力は空。別作業の管理実装は未コミットで対象外。共通規約への案内とタイトルの整合を要確認 | 静的整合OK（対象検証は下表） |

## 検証の区分

静的検査：親・子の識別／版／能力、文書の存在と導線を扱う。パーサー・表示方針の回帰、実製品のビルド、IPC・画面・実サインインは別の結果として記録する。停止拒否や実デバイスを未測定のまま成功にしない。設定・履歴は専用の架空データ以外を試験入力にしない。

## 二巡目

共通手順の第9章と強化した検査器で、同じ14件を再点検した。最終結果はlogs/managed-app-audit/second-final.json。全14件のソース版・子定義・配信config・文書導線は不一致0件。検査器回帰12件成功。静的検査の範囲を一巡目より拡張しているため、語句検査だけで動作適合と判定しない。

| 製品／採用コミット | 今回の対象検証 | 個別記録 |
| --- | --- | --- |
| LiveTextMarker / dcb3bfc | 通常・startup・起動元・既存表示のソース照合、文書リンク。文書変更のみで再ビルドなし | [記録](../../../tools/LiveTextMarker/dev/history/v1.1.0/kotorintoys-contract-audit.md) |
| SmartPaste / fa7825f | startup／hidden／旧trayの初回・既存表示抑止をソース照合、文書リンク。再ビルドなし | [記録](../../../tools/SmartPaste/dev/history/v0.1.5/kotorintoys-contract-audit.md) |
| SSCapture / 0e80686 | startup／hidden・共通管理コピー・専用maintenanceの境界を照合、文書リンク。子の変更は文書のみ | [記録](../../../tools/SSCapture/dev/history/v0.1.5/kotorintoys-contract-audit.md) |
| ShortcutKeyCounter / 7cf56e5 | 専用Mutex2件、設定6件、保存層2件、TypeScriptコンパイル。既定OFF・既存値保持・旧変更RPC拒否 | [記録](../../../tools/ShortcutKeyCounter/dev/history/v0.1.5/kotorintoys-contract-audit.md) |
| CapchunWindow / ec63fd3 | .NET製品参照ビルド、初回／二重起動の共通表示方針5ケース | [記録](../../../tools/CapchunScreen/dev/history/v0.1.2/kotorintoys-contract-audit.md) |
| ChulipVideo / 41131d8 | .NET製品参照ビルド、通常／startup／hidden等5ケース | [記録](../../../tools/ChulipVideo/dev/history/v0.2.1/kotorintoys-contract-audit.md) |
| ChunapTool / f66055f | .NET製品参照ビルド、通常／startup／hidden等5ケース | [記録](../../../tools/ChunapTool/dev/history/v0.1.2/kotorintoys-contract-audit.md) |
| HttpSelector / a507997 | Rust3件、実行対象の追加コンパイル。startup・通常・明示URLと既存転送を区別 | [記録](../../../tools/HttpSelector/dev/history/v0.0.3.7/kotorintoys-contract-audit.md) |
| EveryDB / 465d303 | Rust217件・UI151件・型検査・ビルド。DB初期化前の排他と旧自動起動読取専用化。外部環境16件ignored | [記録](../../../tools/EveryDB/dev/history/v0.1.2/kotorintoys-contract-audit.md) |
| VoiceMixerMicRouter / aa3b59d | 製品CLI6件。共通引数・起動元と固有操作を検証。ソース版1.0.20 | [記録](../../../tools/VoiceMixerMicRouter/dev/history/v1.0.20/v1.0.20-imp.md) |
| VoiceMixerSpkRouter / 6dcfcfb | Rust62件。設定済み通常表示・startup抑止・共通タイトル。実デバイス等6件ignored。ソース版1.0.12 | [記録](../../../tools/VoiceMixerSpkRouter/dev/history/v1.0.12/v1.0.12-imp.md) |
| OperationRecorder / a24b43b | .NETビルド、起動・タイトル関係19件。旧CLI優先順と確認ダイアログ名を整合 | [記録](../../../tools/OperationRecorder/dev/history/v1.3.2/kotorintoys-contract-audit.md) |
| TimecardOCR / e08f9c4 | .NET起動／タイトル6件、Python2件。全5か所のTkタイトル呼出しを検査 | [記録](../../../tools/TimecardOCR/dev/history/v1.0.0/kotorintoys-contract-audit.md) |
| ScriptSync / 703afa3 | Rustタイトル1件、UI225件・ビルド、静的30項目・検査器7件。候補収録と空の管理能力を維持 | [記録](../../../tools/ScriptSync/dev/history/v0.0.6/kotorintoys-contract-audit.md) |

最終の親通常回帰1223件成功。製品・テスト・保守補助のビルドは警告・エラー0件。親のGitリンク14件と子HEADが一致し、点検用の子worktreeは全件clean。親コミットはf46d74a（修正採用）、a88beb0（付属手順・検査強化）。最終記録は別コミットへまとめる。

## 追加した手順と再発防止

- ソースの版正本を目録化し、JSON同士だけの一致で完了しない。HttpSelectorとScriptSyncの4成分版を保持する。
- 無表示判定を既存画面の通知より先に行う。DB・Hook等の初期化前に多重起動を判定する。製品固有引数併用の優先順も明記する。
- 旧Run同期を新規OFF・既存値保持へ改める。単体で安全に共通予約へ接続できないUIは旧状態を読取専用とし、本体へ案内する。実登録を自動移行・削除しない。
- 単独cloneでもREADMEから連携ガイドへ到達させる。未公開の親文書URLを推測せず、公開リポジトリと正本パスを記載する。
- 版別資料は最初からhistoryへ置く。子AGENTSの旧移動指示を訂正し、既存資料自体は移動しない。
- EveryDBの架空サンプル生成と隔離環境変数、ScriptSyncの固定依存準備と並列時間切れの切り分けを記録する。失敗結果を破棄せず、原因を解消した再検証を別ログへ保存する。
- 同梱の古い台帳を模擬最新Releaseと混同しない。公開前検証は必要版より古いMSIXを引き続き拒否する。

## 保持した状態と未検証の境界

利用者の設定・履歴・原稿・音声・実OS予約・既存常駐は、この作業から変更していない。設定保持の自動テストはメモリDB・架空fixtureでの測定であり、実利用者データの前後ハッシュを採取した証明とは区別する。独立Gitと版別ログの除外を維持し、子ソース・parentのコミットだけを作成した。署名・配布物生成・公開・インストール・本体やOSの再起動は実施していない。

実製品の画面前面化・管理IPCの通し接続・停止拒否の実操作・実サインイン・OCR／録画／実音声の受入は今回実施していない。各子の既存検証記録を、今回再実施した結果として数えない。ScriptSyncは利用者回答により管理未対応の候補のままである。

元作業ツリーfeature/full-refactoringは別作業が継続し、点検中にHEADが4a131efへ進んだ。そこでのScriptSync管理対応と未コミット差分を、今回の凍結対象へ後から取り込んでいない。元作業ツリーをリセット・上書きせず、点検結果はfeature/v0.1.12へ残す。後で統合する場合は、その時点の差分・対応能力と採用元を改めて照合する。

## 音声ルーターの開発起動コマンド

Rust・MSVCの前提環境を整えたpwshから、任意のフォルダーで使用できる。この点検用worktreeのソースをDebugでビルドして設定画面を開く。実行時は通常の音声設定を使用し、既存常駐があれば既存の画面が開く。今回これらの起動は実行していない。

```powershell
cargo run --locked --manifest-path "C:\develop\KotorinToys\artifacts\worktrees\v0.1.12\tools\VoiceMixerMicRouter\src\src-tauri\Cargo.toml" --bin voicemixer-mic -- --settings
cargo run --locked --manifest-path "C:\develop\KotorinToys\artifacts\worktrees\v0.1.12\tools\VoiceMixerSpkRouter\Cargo.toml" --bin voicemixer-spk -- --settings
```
