# 収録ツール

各ツールを独立したGitリポジトリとして配置する場所です。

LiveTextMarker、SmartPaste、SSCapture、ShortcutKeyCounter、CapchunScreen、ChulipVideo、ChunapTool、HttpSelectorの8件を配置しています。それぞれ独立したGitリポジトリを維持し、管理窓口で本体と連携しています。HttpSelectorは署名付きMSIXを必須とし、それ以外の開発管理・正式導入の対応状況は各ツールの資料で確認してください。

## HttpSelector

HttpSelectorを8件目の独立Gitとして収録しています。通常利用・管理は[署名付きMSIXの導入](HttpSelector/docs/INSTALL_MSIX.md)が必須です。MSIX版の起動・表示・状態確認・正常停止に対応し、開発用EXEは隔離試験に限って使用します。子のGit remoteは未設定のため、親にはパスと `update=none`、mode 160000の採用コミットだけを記録します。
