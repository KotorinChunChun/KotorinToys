# OfficeAssistのアイコン変更とローカル再リリース

2026-10-27。利用者から指定された画像をOfficeAssistのアイコンに採用し、既存のこのPC向け導入版を更新した。OfficeAssistは未公開であり、公開先は未設定。

独立Gitの変更と詳しい検証結果は[OfficeAssist v0.1.0実装報告](../../../tools/OfficeAssist/dev/history/v0.1.0/v0.1.0-imp.md)に記録する。

配布用ビルド・10サイズのICOとEXE/DLL資源の照合、管理通信の既存検証後、ローカルsetupで導入版を更新し、導入済みEXE・DLL・runtimeconfigのハッシュが配布物と一致することを確認した。その後の利用者指示に従い、追加の動作テストは行っていない。

ローカルsetupは`tools/OfficeAssist/artifacts/icon-release-20261027/OfficeAssist-0.1.0-local-setup.exe`。SHA-256は`B054ECE22C04AFB187EC3F6ABFEB863D5C0B4D612ABF1BD8CAF6CB53353C6FA2`。製品版は0.1.0のまま。GitHub公開や本体の更新は行っていない。
