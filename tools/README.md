# 収録ツール

各ツールを独立したGitリポジトリとして配置する場所です。

## OfficeAssist

[OfficeAssist](OfficeAssist/README.md)はOfficeの操作を補助する常駐アプリです。最初の機能として、エクスプローラーからExcelシートへの画像ドロップによる挿入を提供します。独立Gitで管理し、公開先未設定のため親にはパス・`update=none`・採用コミットを記録します。ローカル開発カタログへの登録であり、公開配布物や導入済みアプリの更新ではありません。

LiveTextMarker、SmartPaste、SSCapture、ShortcutKeyCounter、CapchunScreen、ChulipVideo、ChunapTool、HttpSelectorの8件を配置しています。それぞれ独立したGitリポジトリを維持し、管理窓口で本体と連携しています。HttpSelectorは署名付きMSIXを必須とし、それ以外の開発管理・正式導入の対応状況は各ツールの資料で確認してください。

## HttpSelector

HttpSelectorを8件目の独立Gitとして収録しています。通常利用・管理は[署名付きMSIXの導入](HttpSelector/docs/INSTALL_MSIX.md)が必須です。MSIX版の起動・表示・状態確認・正常停止に対応し、開発用EXEは隔離試験に限って使用します。子のGit remoteは未設定のため、親にはパスと `update=none`、mode 160000の採用コミットだけを記録します。
