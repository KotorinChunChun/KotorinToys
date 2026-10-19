# 全公開プログラムのセキュリティ点検

2026-10-19。対象: KotorinToysと収録11ツール。状態: **ローカル修正・検証。公開版への反映は未実施**。

[要求](v0.1.7-req.md)／[工程・報告](v0.1.7-imp.md)／[再検査手順](../../../docs/security.md)。本体のソース版は0.1.6のまま、点検記録を次版0.1.7へ置いた。版番号・署名・新リリースは公開準備時に確定する。

## 範囲と結果

公開12リポジトリの到達可能Git履歴とローカル履歴、公開HEADと全22公開tag、69資産（約1.42GB）を確認した。69件すべてGitHubのSHA-256 digest・サイズと照合。製品を起動せず23個のZIP／EXE／MSI／MSIXを展開し、説明文・配布台帳を含む22版のテキストを走査した。自作EXE/DLLは重複ハッシュを除く62種類のASCII／UTF-16可読文字列も、メモリー内で抽出して検査器の標準入力へ渡した。生文字列は保存していない。

Gitleaks 8.30.1は公式ZIPとEXEの固定ハッシュを確認し、常に完全マスクで実行した。履歴・追跡ソース・公開資産の秘密値検出は0。公開HEADの秘密保管ファイル名も0。公開バイナリに実DB・秘密鍵は検出しなかった。既知パターンの検出結果であり、「機密情報が一切ない」「リスクゼロ」の証明ではない。

| 製品 | 主な境界確認／対応 | 残す区別 |
|---|---|---|
| KotorinToys | 固定所有者HTTPS・容量／SHA-256・許可リダイレクト・越境防止・MSIX署名／製品照合を確認。配布前の秘密／PDB／旧.NET拒否を追加 | 既存の未署名EXEを署名済みとは扱わない |
| LiveTextMarker | OCRの認識文字列をログへ送らない経路、利用者限定管理パイプを確認。NuGet指摘0 | 旧公開maintenance PDBと同梱.NETは再配布時の対象 |
| SmartPaste | OCR別プロセス・引数・保存／履歴の境界、利用者限定IPC。NuGet指摘0 | 旧公開PDB・同梱.NETの更新が残る |
| SSCapture | 画面・音声の明示操作と生成メディアの処理、利用者限定IPC。NuGet指摘0 | 実撮影／録音は未実施。旧版PDB・同梱.NETは残る |
| ShortcutKeyCounter | password・判定不能・高IL前景のFail Closed、正規化入力保存・通常文字制限・管理IPCを確認。保護入力の既存単体5件成功、Cargo vulnerability0 | 保守終了8、Windows依存木にないglibのunsound1。実入力・secure desktop実測は別 |
| CapchunWindow | 再生デコーダーをfile/pipeへ限定。外部プレイリストを接続前拒否し291件成功 | 実画面／実入力／カメラは未実施。旧公開版へは未反映 |
| ChulipVideo-CSharp | file/pipe限定、外部参照拒否、PNG／GIF／MP4等の既存回帰成功。独立ZIPにも配布拒否ゲート | 旧公開版へ未反映。FFmpeg内部の全CVE確認ではない |
| ChunapTool | 対象ウィンドウ操作・引数・利用者限定管理パイプ、NuGet指摘0 | 実ウィンドウの移動等は未実施。同梱.NETの再配布更新が残る |
| HttpSelector | 公開tagでhttp/https限定・制御文字拒否・ブラウザー引数分離、署名付きMSIX管理。URL単体4件成功 | 既存ローカル差分は変更・コミット・採用しない |
| EveryDB | ZIP9／calamine／Vitest等を更新。実容量・項目数・行数制限、HTTP応答本文の非露出を追加 | 保守終了9、Windows依存木にないglibのunsound1。実サービスは未受入 |
| VoiceMixerMicRouter | rustls／quinn／wasapi更新、loopback・認証期限・32接続・1MiB・停止時解放、独立配布ゲート | 実音声・ブラウザー接続・インストーラー受入は別 |
| VoiceMixerSpkRouter | 同上。ZIP／Inno両方へ秘密検査を追加 | 実音声・ブラウザー接続・インストーラー受入は別 |

## 修正と依存監査

- EveryDB: ZIP3版に対するvulnerability9指摘を0へ。calamine 0.37、zip 9.0.5、開発用rust_xlsxwriter 0.100。mysql 28.0.4経由のlru 0.18.5、event-listener 5.4.2で修正可能なunsound2件も解消。npm開発依存6指摘をVitest4.1.11等で0、製品用も0。ZIPの自前処理はファイルシステムのextractを使わないが、依存更新と資源制限を別々に実施した。
- 音声2製品: rustls 0.23.46、quinn-proto 0.11.18で各9指摘を0、wasapi 0.25.0で各unsound1件も解消。最終lockのvulnerability／unsound／unmaintainedはいずれも0。
- Cargo監査は公式cargo-audit 0.22.2、RustSec DBコミット`999ec89d5d3f10f20be14750ec8c2d8b44e0ac9b`。5ローカルlockと7公開tagのlockを照合。古い公開EveryDB2版・音声2版には修正前の指摘が残る。これはlockの指摘であり、全経路が現在のWindows製品で攻撃可能という意味ではない。
- 最新公開ソースのNuGet12製品／helperプロジェクトの監査は指摘0。ただしランタイム／ネイティブDLLの別検査を代替しない。

一次情報: [wasapi境界外読取り](https://rustsec.org/advisories/RUSTSEC-2026-0332.html)、[ZIP extractのシンボリックリンク問題](https://rustsec.org/advisories/RUSTSEC-2026-0368.html)、[lruのpanic時安全性](https://rustsec.org/advisories/RUSTSEC-2026-0253.html)、[rustlsセッションDebugの機密露出](https://rustsec.org/advisories/RUSTSEC-2026-0346.html)。

## テストとコミット

| 対象 | 結果 | 修正コミット |
|---|---|---|
| EveryDB 0.1.2 | check:all成功。Vitest40ファイル151件、Rust214件成功／16件ignore。fmt／clippy -D warnings／文書／CSP／ビルド成功 | f3c8b70 |
| MicRouter 1.0.19 | 通信13件、通常85件成功／実機6件ignore、製品ビルド成功 | 113a77e |
| SpkRouter 1.0.11 | 通信13件、通常60件成功／実機6件ignore、製品ビルド成功 | 0a7c956 |
| ChulipVideo | 合成動画・外部参照拒否・編集・設定・管理・画像／動画出力の回帰成功、警告0ビルド | b783629 |
| CapchunWindow | 外部参照拒否・合成メディア／再生を含む291件成功、警告0ビルド | d7ba218 |
| 公開前ゲート | 正常、禁止8拡張子、.env、架空PAT、旧.NETの12ケース。親＋独立3コピーで計48ケース成功 | cfeddae以降の親検証 |
| 本体の統合回帰 | 修正5子の採用状態でも837件成功、警告0ビルド。公開configと既存リリースは不変 | P3親コミット |

追加で、Spk拡張の試験用固定トークンを新規／旧版空欄へ補完しないよう修正し、保存値保持を含む31件と型・拡張ビルドに成功した。実資格情報の漏洩ではなく、安全な未設定状態への整理である。10子の不足するGit除外を補い、秘密ファイル・private/temp・logs・artifactsの150ケースを実データなしで確認した。除外規則だけの追加で製品コンパイルを形式的に繰り返してはいない。

最終採用HEAD: LiveTextMarker `bfc85c6`、SmartPaste `bba2985`、SSCapture `36c5fc2`、ShortcutKeyCounter `aac94cc`、CapchunWindow `e5aadd6`、ChulipVideo `fcabdf9`、ChunapTool `d4ccb91`、EveryDB `d8c2953`、MicRouter `a958bde`、SpkRouter `4c73b8f`。上表の処理修正コミットをそれぞれ内包する。HttpSelectorは既存の作業状態を保持し、採用を変更しない。

生ログ・mirror・検査用ダウンロード・完全マスクJSONは`logs/audit-20261019/`と各版別logsに保持しGit除外。初回のGit所有者エラー、NULを含むバイナリ入力での早期終了、旧試験フォルダー再利用の失敗を成功件数へ含めていない。いずれも原因を切り分けて再検証した。実際のバイナリ検査証拠は`binary-printable-summary.json`であり、旧`binary-strings-summary.json`には未完了を明記した。

## 公開側に残る問題と必要な次工程

1. 旧本体0.1.0／0.1.1、LiveTextMarker1.1.0、SmartPaste0.1.4／0.1.5、SSCapture0.1.0の6インストーラーにmaintenance PDBが残る。秘密鍵ではないが不要なデバッグ情報公開であり、公開済みコピーを削除・差し替えしていない。
2. 公開配布に同梱.NET 10.0.11／10.0.12／8.0.30が残る。公式10.0の最新修正版は[10.0.13（2026-10-13）](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)。自己完結型はPC側ランタイムだけを更新しても直らないため、該当製品を最新パックで再ビルドする必要がある。
3. 調べたEXE／MSI／MSIX19件は未署名18件、このPCで署名Valid1件。ハッシュ一致は改変検知であって、GitHubアカウント／公開権限の侵害まで防ぐ発行元証明ではない。コード署名や秘密鍵のエクスポートは行っていない。
4. GitHub Secret scanningの取得可能な11リポジトリはopen0件。EveryDBは機能無効で取得不可。Dependabot alertsは全12件で取得不可（機能無効／トークンのadmin:repo_hook不足の応答）。これらを0件と報告しない。設定有効化・権限追加は未実施。
5. 修正版push・新規リリース・本体の公開取得対象更新について確認を依頼済みで、回答待ち。公開済みtag／asset削除・置換、強制push、認証情報失効、インストール・アプリ停止・PC再起動は行っていない。公開修正の承認があるまで、インターネット上の旧版リスクは残る。

## 未検証の境界

圧縮／難読化されて可読文字列に現れないバイナリ埋込みは保証対象外。HttpSelectorの既存作業は公開tagの試験と読取りに限定し、`.env`等の追加除外規則も今回変更していない。統合時に当該開発側で確認する。現在の公開履歴の秘密検出は0件。

完全な侵入試験、全ネイティブライブラリ／OS／WebView2／外部FFmpeg・OCR・RVCモデルの全CVE、Cargo yanked、全旧版NuGetグラフ、物理音声・実入力・実録画・外部サービス認証、MSRV1.88そのものでは検証していない。Rustの実測環境は1.99.1。同一利用者の悪意あるプロセスや、既に漏れた資格情報の失効は別の境界。プロジェクト外の秘密保管先とGit除外済みprivate/temp、通常DB・履歴・録音は読んでいない。

## 手動で修正ソース版を確認する場合

実行中の正式版をこの点検では停止していない。重複起動・設定の保存先を確認してから任意で使う。以下は今回ビルドしたDebug実行物で、更新インストールではない。

```powershell
cd C:\develop\KotorinToys\tools\VoiceMixerMicRouter
& .\src\src-tauri\target\debug\voicemixer-mic.exe --settings

cd C:\develop\KotorinToys\tools\VoiceMixerSpkRouter
& .\target\debug\voicemixer-spk.exe --settings
```
