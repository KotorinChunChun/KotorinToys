# インストーラー作成環境

本体と対応ツールはユーザー単位のInno Setup EXEを採用する。0.1.5からEveryDBも同じ形式で配布する。EveryDBはネイティブEXEとWebView2 Runtimeを使用し、共通maintenanceだけが自己完結型.NETである。

例外としてHttpSelectorは署名付きMSIXを維持する。0.1.6ソースの[MSIX管理](msix-management.md)は公開MSIXを検証してWindowsへ渡す機能であり、HttpSelectorをInnoへ梱包し直す機能ではない。本体・MaintenanceはWindows SDK APIを参照するため、発行時は新しいTFMの依存DLLと利用条件も含めて点検する。

## コンパイラーの準備

Windows x64・pwshで実行する。

```powershell
cd C:\develop\KotorinToys
pwsh -File dev/scripts/prepare-inno.ps1
```

公式GitHub配布のInno Setup 7.1.0を取得し、Authenticodeが有効で発行元が`Pyrsys B.V.`であること、および固定版SHA-256が一致することを確認してから実行する。既存のダウンロードも毎回検証する。配布バイナリーが変更された場合は自動的に許容せず、処理を停止する。

ダウンロード・検証のみを行う場合は`-DownloadOnly`を付ける。コンパイラーは`artifacts/dependencies/inno-7.1.0/`へユーザー単位で導入する。ログは`dev/history/v0.1.0/logs/inno-prepare-<実行識別子>/`へ置く。生成物はGit対象外である。

コンパイラーの導入は製品インストール、Windowsのログオフ・再起動、GitHub公開を行わない。コンパイラーの利用は[公式ライセンス条件](https://jrsoftware.org/isinfo.php)に従う。商用ライセンスの購入は自動化しない。

公式参照：[配布元](https://jrsoftware.org/isdl.php)、[署名確認](https://jrsoftware.org/isdl-verify.php)、[導入引数](https://jrsoftware.org/ishelp/topic_setupcmdline.htm)。

## パッケージ用の自己完結型発行

```powershell
cd C:\develop\KotorinToys
pwsh -File dev/scripts/publish-packages.ps1
```

既定では本体・LiveTextMarker・SmartPasteとSmartPasteのOCRワーカーを、毎回新しい`artifacts/packages/packages-<実行識別子>/`へ発行する。既存の開発出力や稼働中のEXEを上書きしない。`-Products SmartPaste`などで対象を限定できる。

SSCaptureは専用maintenanceと更新・復旧のtransaction契約を持つため、[子のRelease規則](../tools/SSCapture/dev/RELEASE_RULE.md)に沿って`tools/SSCapture/scripts/package.ps1`で生成する。共通の発行・インストーラー生成・公開スクリプトへ指定すると、ビルド・依存導入・認証より前に拒否する。旧`-ExcludeFFmpeg`指定でも共通経路は使用できない。専用生成はFFmpeg／FFprobe・OneOCR外部ファイル・利用者dataを含めない。子の作業中ソースを無条件に採用・生成しない。

共通発行はランタイム設定と主要DLL、OCRワーカー、学習データ・TesseractネイティブDLL、文書・ライセンスを検査する。全ファイルの長さとSHA-256を`<製品名>.files.json`へ記録する。学習済みデータ以外の利用者設定・画像・履歴・OneOCRの外部ファイルはコピーしない。

これはインストーラーの入力準備であり、機能の実動作や正式導入・削除の合格を示さない。FFmpegを外部へ再配布する際の対応ソース提供条件と、VC++／OneOCR等の外部依存は別途確認する。GitHubへの公開は行わない。
