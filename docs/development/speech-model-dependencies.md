# 音声モデルを使うアプリと更新通知

[開発者ガイド](../DEVELOPERS_GUIDE.md)へ戻る。KotorinToysの開発一覧と配信一覧に含まれるアプリを対象に、モデルや音声サービスを呼ぶ実装と、通知先のCodex開発チャットを記録する。

| アプリ | 用途・依存先 | Codex開発チャット |
| --- | --- | --- |
| TranscriptionServer | Whisper、speech.cpp（Haas等）、NeMo系の文字起こし・話者分離。外部のOpenAI音声認識も選択できる。[API仕様](../../tools/TranscriptionServer/docs/server-api.md) | TranscriptionServer |
| EchoType | 音声入力の即時表示・確定。Windowsの認識機能やローカルのWhisper・Haas・Nemotron等に対応し、設定でOpenAI互換の文字起こしAPIも使う。[モデル一覧](../../tools/EchoType/src/EchoType.Core/Transcription.cs)・[互換接続](../../tools/EchoType/src/EchoType.Windows/ExternalSpeechRecognizer.cs) | EchoType |
| OperationRecorder | マイク説明を手順に反映。Windows音声認識、ローカルWhisper・Haas、外部の文字起こしAPIに対応する。[認識方式](../../tools/OperationRecorder/src/OperationRecorder/Recording/NarrationTranscription.cs)・[互換接続](../../tools/OperationRecorder/src/OperationRecorder/Recording/TranscriptionApi.cs) | OperationRecorder |
| ScriptSync | 収録したテイクをWhisperで認識する。デスクトップとWebで実行環境が分かれ、音声合成は別途用意するHTTPサービスを接続ゲートウェイ経由で使う。[構成](../../tools/ScriptSync/docs/architecture.md)・[Web版](../../tools/ScriptSync/docs/web-browser.md) | ScriptSync |
| ReadingRepair | VoiSona TalkまたはHTTP音声合成サービスの出力をローカルのfaster-whisperで照合し、読みと辞書を検証する。[使い方と構成](../../tools/ReadingRepair/README.md) | ReadingRepair |
| TalkRecorder | speech.cppのC APIでSilero VADを使う。Haasの認識やRNNoise・DeepFilterNet3・RE-USEのノイズ除去を選べる。[機能](../../tools/TalkRecorder/docs/features.md) | TalkRecorder |
| VoiceMixerMicRouter | RVCモデルによるリアルタイムの音声変換。[RVC設定](../../tools/VoiceMixerMicRouter/README.md) | VoiceMixerMicRouter |
| ChulipVideo | 動画書き出し時のナレーションを、Windowsに登録された音声で合成する。[音声合成の実装](../../tools/ChulipVideo/src/ChulipVideo/Audio/NarrationService.cs) | ChulipVideo |

## TranscriptionServerの連携候補

収録アプリではEchoType、OperationRecorder、ScriptSync、ReadingRepair、ChulipVideoの5件が連携候補。TranscriptionServerの仕様変更は、この5件の開発チャットへ通知する。候補ごとの連携方法と実装の有無は、個別に確認する。

TalkRecorderとVoiceMixerMicRouterは独立して運用し、TranscriptionServerの連携候補と仕様変更の通知先から除く。

EchoTypeとOperationRecorderはTranscriptionServerと同じ形式の文字起こしエンドポイントを設定できる。実際の接続は、認証モード・リクエスト項目・結果形式を照合して判断する。調査対象のソースからは、両アプリがTranscriptionServer専用のクライアントであることや、このPCで接続先に設定されていることは確認していない。TalkRecorderはspeech.cppをC APIで直接利用する。VoiceMixerSpkRouterの製品仕様は音声ルーティングで、共通クレートに残るRVC・音声合成コードを製品の利用としては数えない。[SpkRouterの構成](../../tools/VoiceMixerSpkRouter/docs/architecture/spk-router.md)

収録外では、TranscriptionServerの組込み契約にTranscriptCutterが実名で記載されている。仕様の影響を確認するときは組込みクライアントとして扱う。通知先のCodex開発チャットは未特定。[組込み契約](../../tools/TranscriptionServer/docs/embedding-contract.md)

## 仕様変更を通知する手順

1. TranscriptionServerの更新では、更新前後のAPI、認証、モデルIDと既定値、実行環境、起動・準備完了条件、字幕や話者の結果形式を比較する。共通のモデルやランタイムだけを更新する場合も、この一覧で利用先と互換性を確認する。
2. 仕様が確定したら、TranscriptionServerの更新では上記5件の開発チャットへ通知し、各アプリへの影響を明記する。TranscriptionServerとは別に共通モデルや実行環境そのものを更新した場合は、その依存先を使うアプリへ通知する。通知元と同じチャットでは作業報告に含める。チャットはKotorinToysプロジェクト内の表記で照合する。
3. 通知には対象版とコミットまたはリリース、変更前後の仕様、旧版との互換性、そのアプリで変更・再検証する点、仕様の参照先を添える。送信結果と、宛先未特定・送信失敗があればその理由を版別の実装・公開記録へ残す。通知は仕様の共有として送り、受信側のコード修正や公開は別途指示がある場合に行う。

設定の変更や同じ仕様の再導入では、通知する仕様差分があるかを確認する。アプリの追加、利用経路の変更、開発チャットの移動時はこの一覧も更新する。
