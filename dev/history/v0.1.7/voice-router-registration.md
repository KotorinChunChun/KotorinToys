# 音声2ツールの対象登録 再確認・補完

依頼日: 2026-10-19。原文: 「https://github.com/KotorinChunChun/VoiceMixerSpkRouter https://github.com/KotorinChunChun/VoiceMixerMicRouter この2つを対象アプリに追加してください。」

## 事前確認と境界

- 開発カタログ・配信用config・固定製品識別・GitHubリンク・公開台帳には2件とも登録済み。重複登録しない。
- GitHub APIで2件ともPublic・main・公開済み開発版を確認。公開版はSpkRouter 1.0.10、MicRouter 1.0.18。ローカルのセキュリティ修正版1.0.11／1.0.19とは区別する。
- GitHub mainのconfigを実取得すると9件で、音声2件は含まれない。これはローカル追加とは別の公開工程が未実施のため。
- SpkRouterには、本体の開発ビルド処理と本体配布への定義・説明書同梱に漏れがある。READMEの共通表にも音声2件が未掲載。
- 今回は登録漏れの補完と自動検証・コミット。前回確認したmainへの統合・push、本体の配布・PCへの導入・実アプリ停止は行わない。既存のHttpSelector差分、設定・履歴・音声の稼働は保持する。

## 方針・工程

- [x] P1: 登録の一意性、固定URL／AppId、オンラインconfig、公開版への導線を回帰確認する。SpkRouterのビルドと説明書同梱の漏れを補い、関連テストを追加する。
- [x] P2: 本体ビルド・通常回帰・配布スクリプト検証・文書リンク・差分検査を行い、結果と公開前の残件を記録してコミットする。

配布インストーラーの生成元は引き続き各子のBuild-Installer.ps1とし、Rustツールを本体のC#製品発行へ混ぜない。

## 検証結果

本体ビルド成功（エラー0、NuGet監査API取得不可NU1900警告4）。通常回帰845件、本番の説明書同梱とビルド指定8件、配布スクリプト5件の構文・固定先・拒否確認が成功。実GitHub公開照合でMic 1.0.18とSpk 1.0.10を確認した。ログは本版logs/voice-registration-final-6dcaaec0bfb248d6922b8ba14100da75/。インストール・起動・音声測定は再実行していない。以前の管理IPC実測は[SpkRouter](../v0.1.6/voicemixer-spk-router-imp.md)・[MicRouter](../v0.1.6/voicemixer-mic-router-imp.md)を参照し、今回の新しい実測と混同しない。
