# MSIXの照合・データ保持・検証

[開発者ガイド](../DEVELOPERS_GUIDE.md)へ戻る。利用者の導入・更新とエラー対処は[HttpSelectorの操作説明](../msix-management.md)へ分離する。

本体はWindowsの登録・Manifest・実プロセスのPackage Familyを照合し、パッケージ起動APIを使う。開発EXEへフォールバックしない。MSIXの製品定義・信頼条件を汎用Innoと混同しない。

## 設定・履歴の保持

- 更新は同じPackage Familyを維持します。更新前にLocalState/RoamingStateを退避し、展開後・再起動前に保持を照合します。
- 削除前に同領域を本体のユーザーデータ領域へ退避します。DB本体だけでなく署名・バックアップ・DPAPI整合性キーも含めます。退避できなければ削除しません。
- 保存先は `%LOCALAPPDATA%\KotorinToys\msix-data\HttpSelector.Development_xtxrnffqrdk4w\` です。現在ユーザーとSYSTEMだけのアクセス許可を設定します。
- 次の本体経由の再導入時、同じユーザー・同じFamilyへ起動前に復元します。異なるデータが既にある場合は上書きせず「要確認」で停止します。退避一式は自動削除しません。
- OSの設定から直接アンインストールした場合や本体の保持領域まで手動削除した場合は、本体で事前退避できません。削除・再導入は本体から行ってください。
- 自動退避の上限は10,000ファイル・4GBです。リンク、保持記録の不一致、退避中の変更は中止理由になります。超える場合は手動バックアップの管理が必要です。

## 照合と失敗原因

固定リポジトリ `KotorinChunChun/HttpSelector` の4成分tag、asset名、SHA256SUMS、GitHub digest、サイズを照合します。同梱台帳はcatalog/releases.jsonを正本とし、同版の差し替えを拒否します。

導入前にWindowsの署名検証、Publisher `CN=KotorinChunChun`、証明書指紋、Manifest、payload全ブロックを検証します。Familyは `HttpSelector.Development_xtxrnffqrdk4w` だけです。別のFamily・発行元・証明書への移行はconfigの変更だけでは許可しません。

結果には原因とHRESULTを表示します。例えば `0x800B0109` は証明書の信頼、`0x80073D02` はアプリ使用中、`0x80073CF3` は依存・競合登録、`0x80070005` はアクセス権・ポリシーを確認します。同版の内容差し替え等を自動アンインストールで解消することはありません。

GitHubのAppInstaller配信MIMEに依存せず、検証済みローカルMSIXをWindows PackageManagerへ渡します。ダウングレード、他ユーザー操作、強制終了、OS自動再起動は行いません。


## 検証の区分

OS操作を差し替えた自動試験、実公開物の取得・署名と内容の検証、実OSへの導入・更新・削除・再導入は別の結果として記録する。未信頼の証明書を使う別PCも別の受入条件となる。個別のコマンドと前提は[管理連携の検証](tool-integration.md)を参照する。過去の受入状況は[版別記録](../../dev/README.md)で確認する。