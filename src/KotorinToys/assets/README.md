# 本体アイコン：採用04

2026年10月14日の利用者指定により、候補04「青い小鳥と赤・緑の積み木」を本体のアイコンとして採用した。収録3ツールの固有アイコンとは分ける。

- KotorinToys.png：透過PNGマスター、1280×1280。
- KotorinToys.ico：16／20／24／32／40／48／64／96／128／256px、32bit DIB＋ANDマスク。256pxの幅・高さはICOの規約で0と記録する。

## 素材の作成

imagegenスキルに従い、組込み画像編集で候補ボードの右上04を参照した。指示は「04だけを単独の透過アイコンに仕上げる。青い鳥の丸い輪郭・姿勢・目・黄色いくちばしと赤／緑の積み木の色・相対位置・元の画風を維持する。他の候補を混ぜない。背景・床の影・番号・文字・枠を除去し、全体を切らずに正方形へ配置する」。生成結果をPNGマスターとして保存し、ICOへの変換は下記スクリプトで行う。再生成時は画像生成をやり直さず、保存済みマスターを使う。

### 編集に使用した最終プロンプト（ツール入力の原文）

組込みツールを使用し、透過背景を指定した。CLI/APIの代替経路は使っていない。候補ボードを編集対象として参照し、次の原文を渡した。

> Use case: identity-preserve + background-extraction. Edit target: the supplied 4x4 numbered concept board. The user selected EXACTLY the design numbered 04, the upper-right cell: a round sky-blue little bird leaning its head down to play with a red cube and a green cube. Output ONLY this selected 04 bird-and-two-cubes as a single isolated Windows application-icon master on a genuinely transparent background. Crop/isolate and faithfully reproduce that exact design at high resolution. Preserve the original blue bird's rounded silhouette, pose, two dark eyes, yellow beak touching the red cube, wing and tail shapes, colors, shading, and relative proportions; preserve the red cube near the bird and green cube to its right. Preserve the exact original cartoon style, do not redesign the character, do not choose or mix any other cell. Bird is to the left/top of the two cubes as in 04. No new legs, extra toys, decorative outline, badge, circle, frame, text, numbers, or logos. Remove the white background and soft floor shadow completely, preserve clean alpha edges with no white matte or glow. Use a square canvas, center the entire compact bird-plus-cubes composition, comfortably fill approximately 86 percent of canvas width without cropping any wings, tail, beak, or cubes. This is the definitive 04 source master, not a new proposal, not a contact sheet. It must remain readable when converted to 16, 24, 32, 48 and 256 pixel application icons.

## 再生成と検証

プロジェクトルートでpwshを使用する。

    ./dev/scripts/build-app-icon.ps1
    ./dev/scripts/test-app-icon.ps1

検証はICOの10フレームを個別に読み、寸法・透過・青い鳥と赤／緑の積み木の色を調べる。複合ICOに対するSystem.Drawingの自動サイズ選択と、ICO内のフレームの有無を区別する。実EXEへの埋込み・画面への割当と、実導入済みアプリの見た目確認は別の検証とする。

要求・実装結果は[版別計画・報告](../../../dev/history/v0.1.0/v0.1.0-imp.md)を参照する。
