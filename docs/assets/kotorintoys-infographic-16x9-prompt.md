# KotorinToys紹介インフォグラフィック

- 用途：KotorinToys本体の紹介。16:9向け横長レイアウト。
- 画像：[インフォグラフィックPNG](kotorintoys-infographic-16x9.png)。
- 作成日：2026-10-24。
- 生成方式：imagegenスキルに基づく組込み画像生成。CLI/APIの代替経路は使用していない。
- 参照した現行説明：README、利用ガイド、共通アイコン規約とPNGマスター。アプリのバージョン番号・収録総数は画像へ固定しない。
- 図中の小鳥と積み木はブランドを踏まえた説明用イラスト。各ツールの図形アイコンも説明用であり、既存のアイコン資産を置き換えるものではない。
- 保存した生成原寸：1672×941px（16:9相当）。厳密な整数比ではない。比率調整を画像編集ツールへ指示したが同じ寸法が返されたため、ツール外で拡縮・切り抜きせずそのまま保存した。
- 日本語、アプリ名、機能の対応と全体の可読性を目視確認。製品コード・設定・公開済み配布物は変更していない。

## 初回の生成プロンプト（原文）

```text
Use case: infographic-diagram.
Create one finished Japanese product infographic for KotorinToys, a Windows utility-app manager. STRICT landscape 16:9 canvas, ideally 2048 x 1152 pixels. Not a photograph of a slide; the image itself fills the whole canvas. High-quality polished editorial information design, immediately readable at presentation size.

Visual identity: bright sky-blue little bird with round body, white belly, large dark eyes, small yellow beak, playing with one red cube and one green cube, inspired by the app's established blue-bird-and-building-blocks motif. Friendly but professional. Off-white background, dark navy Japanese typography, restrained teal/sky-blue accents, red/green accents tied to the cubes. Smooth illustrated mascot and crisp flat diagram elements. Large Japanese Gothic sans-serif text, generous whitespace, precise alignment, consistent corner radii and icon strokes. No noisy decorations.

Composition:
Top: large title "KotorinToys", subtitle "Windowsの便利ツールを、ひとつの窓口で。". Small platform badge "Windows 11 / x64".
Middle left: a prominent hub illustration marked "KotorinToys" with the blue bird and red/green cubes, connected by clear fine lines to four neatly arranged utility cards. This is an explanatory hub diagram, NOT an invented screenshot. Each card has a simple appropriate icon, exact app name and short descriptor:
"SSCapture" / "画面撮影・録画"
"SmartPaste" / "貼り付け作業を支援"
"EveryDB" / "データの取り込み・横断検索"
"ChunapTool" / "ウィンドウの位置・サイズ調整"
Small heading above these examples: "必要なツールを選んで使う".

Middle right: four large feature blocks in a balanced 2 x 2 arrangement, each with a clear icon and exact heading/body:
1. "導入・更新をまとめて" / "対応アプリを一覧からインストール"
2. "起動・停止をひと目で" / "実行状態と更新の要否を確認"
3. "自動起動も設定" / "サインイン時の起動をまとめて管理"
4. "それぞれ単体でも" / "本体を閉じても、ツールは動作"

Bottom: a slim full-width band with heading "安心して、長く使うために" and three short checkmarked facts:
"設定・履歴を保持"
"製品・容量・SHA-256を照合"
"取得制限の解除予定を表示"
Small readable footer: "※対応機能はアプリにより異なります。"

All quoted Japanese and app names must be rendered verbatim, with correct kanji and punctuation. Text and explanations are the priority, no garbled placeholder text. Make each section clearly distinct without a crowded wall of cards. Large title and feature headings, readable supporting text. Keep all content within safe margins. No version numbers, no fake performance metrics, no promises of digital signatures or universal app support, no pricing/free claims, no QR codes, no watermarks, no extra logos. Beautiful finished publication-ready infographic, not a wireframe.
```

## 最終調整プロンプト（原文）

```text
Edit target: the supplied completed KotorinToys Japanese infographic.
Make exactly one change: normalize the complete canvas to STRICT EXACT 16:9 landscape, preferably precisely 1920 pixels wide by 1080 pixels high (or precisely 2048 by 1152). The input is 1672 by 941 pixels, which is only approximately 16:9; deliver an EXACT integer 16:9 output, not that approximate ratio. Use the existing entire infographic as the unchanged content. Do not redraw, reword, reposition or remove any text, icons, connectors, app names, mascot, panels or footer. The current Japanese copy is correct. Only a subpixel-scale edge adjustment / very small outer-background adjustment is necessary, since the ratio difference is under 0.1 percent. Keep all edges and the footer comfortably visible, no cropping text. Preserve its colors, clean off-white background, crisp type, and complete layout. Opaque PNG, no frame, no new content, no additional badges or watermarks.
```
