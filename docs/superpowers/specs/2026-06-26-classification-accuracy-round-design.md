# 分類精度ラウンド＋冗長検出＋旧エンジン撤去 — 設計

- 日付: 2026-06-26
- ブランチ: `autosort-evidence-phase1`（既存。F1 統一制約グラフ完了済み）
- 関連: docs/superpowers/specs/2026-06-25-autosort-unified-graph-engine-design.md
- 前提メモリ: project-autosort-reframe / feedback-general-not-handtuned / reference-loot-vortex-autosort

## 背景

並び順を決めるエンジン（`ConstraintGraphOptimizer`＝依存ハード＋資源重なり辺）は完成し、実機 P5R で「土台が前方」を確認済み。**順序は正しい**。

残っているのは **MODに付く「種類ラベル」と理由文の精度**。実害は小さい（ラベルは順序の弱いタイブレークにしか効かない）が、実機ダンプで見るとズレがある。本ラウンドでラベル精度を底上げし、合わせて「冗長MOD検出（片方だけ想定の重複）」を追加、本番未使用となった旧エンジンを撤去する。

## 揺らさない原則

- **手チューニング禁止**：特定のMOD名やID固有の分岐を書かない。汎用語・汎用条件の一般ルールのみ。ソフト自身が新ゲーム/新MODを解けること。
- **実機ダンプ検証**：各変更を `tests/ReloadedHelper.Core.Tests/_RealInstallDump.cs`（実機 `C:\FreeSoft\Reloaded-II` を読み `classification-dump.txt` を出力）で before→after 比較し、ラベルが実際に改善したか確認する。合成テストの緑だけで完了としない（過去の重大教訓）。
- **北極星**：完全自動。並び替えはエンジンが決め切る。手動並び替えUIは持たない。ユーザーに見せる競合は「片方だけ想定の重複」だけ＝無効化で解決。

## 対象ファイル

- `src/ReloadedHelper.Core/ModTypeClassifier.cs` … ①②③(a) の分類ルール
- `src/ReloadedHelper.Core/ModType.cs` … ③(b) Unknown の順序ランク（`ModTypeInfo.Rank`、現状 Unknown=10 で最後尾）
- 冗長検出: 新クラス（後述）＋ `GameDiagnostics` / `ModDiagnostics` への配線、診断パネル表示
- 撤去: `LoadOrderOptimizer.cs` / `WinnerResolver.cs` / `PlacementHint.cs`（PlacementHintParser）と各テスト

---

## ① Characters を一律「モデル」にしない

### 現状の問題
`ModTypeClassifier.Classify` は、名前キーワードに当たらず GameBanana カテゴリが `Characters`/`Character` のMODを全部 `Model` にする（`ModTypeClassifier.cs:96-99`）。結果、髪の色替え（Black Hair Futaba）やメニュー（dynmainmenu）までモデル扱いになる。

### 変更（一般ルール）
`KeywordRules` のキャラ部位の語彙を広げる。判定は「資源 → カテゴリ → 名前キーワード → Characters フォールバック」の既存順序を保ったまま、名前キーワードの網を広げて Characters フォールバックに落ちる前に正しい種類へ振り分ける。

- UI 語に追加: `menu`, `title`, `mainmenu`
- スキン・テクスチャ語に追加: `hair`, `eyes`, `face`, `recolor`
- モデル語に追加: `mask`, `helmet`

`Characters` フォールバック（最後の手段＝`Model`）は残す。どの語にも当たらなければモデル。

### 検証で決めること
`hair` をスキン側にするとモデル系の髪型MODを巻き込まないか、`mask`/`helmet` がスキン retexture を奪わないか。**追加語ごとにダンプの before→after を確認**し、誤爆が出る語は採用しない。最終語彙はダンプ検証で確定する（本仕様の語リストは初期候補）。

### 期待
Black Hair Futaba → スキン・テクスチャ、dynmainmenu → UI。Characters の「モデル」総数が減り、内訳が髪/立ち絵/メニュー/モデルに分散する。

---

## ② 土台（ライブラリ）ルールを緩める

### 変更
`ModTypeClassifier.Classify`（資源オーバーロード, `ModTypeClassifier.cs:65`）の
`dependentsCount >= 2 && resources.Count == 0` を **`dependentsCount >= 1 && resources.Count == 0`** に。

理由文の数値（`{dependentsCount}個のMODから依存される土台のため…`）はそのまま使える。

### 一般則の意味
「誰かに依存される（＝前提として参照される）＋自分はゲームファイル（資源）を持たない＝土台/前提物」。資源を持つ人気コンテンツ（パッチ多数のスキン等）は資源0でないので巻き込まれない。

### リスクと検証
資源0は「スキャン対象ルート外にファイルがある」MODでも0になりうる（例: Black Hair Futaba も r0）。ただし**依存される側に限る**ため影響は限定的。期待昇格は texturefixesproject(d1 r0)・evt.fadeouttablemerge(d1 r0)。ダンプで誤昇格（本来コンテンツなのに土台化したMOD）がないか確認する。

---

## ③ 判定不能の救済＋中立配置

### (a) 救済キーワード追加
`KeywordRules` に汎用語を追加し、判定不能(Unknown)を減らす。
- 音楽語に追加: `audio` → audiomixcontrol を音楽へ
- UI 語に追加: `confirm`（＋①の `menu`） → silentmenuconfirm・uitoggler を UI へ

`q2mementos` のような手がかりの無いものは無理に当てず Unknown のまま残す（誤分類より中立が良い）。

### (b) 中立配置
どうしても Unknown のものを「末尾」ではなく**中間（中立）位置**に置く。
- `ModTypeInfo.Rank`（ModType.cs）の Unknown を現状 10（最後尾）から中間値へ。Music(4) と Model(5) の間あたり＝コンテンツ層の中央に置く（具体値は実装時、他ランクとの兼ね合いで確定）。
- 理由文を「手がかりがないため末尾に配置」→「種類が特定できないため中間に配置」へ変更。

末尾は「最後に上書きする＝最強」を意味してしまい、正体不明のMODを最強位置に置くのは不適切。中間が無難。

---

## ④ 冗長MOD検出＋旧エンジン撤去

### (a) 冗長ペア検出
**目的**: 依存関係に無い2つのMODが同じ資源（file/song/costume キー）を大きく重ねて触る場合、「片方だけ想定の重複かも」とユーザーに**非ブロッキング**で提示する。並び替えはしない（北極星）。解決は無効化。

**一般則（しきい値はダンプで調整）**:
- 2つのMOD A, B が共有する資源キー数を、小さい方の資源集合サイズで割った「重なり率」を計算。
- 重なり率が高い（初期候補: 小さい方の資源の大半＝例 80% 以上が相手と重複）かつ A→B / B→A の依存が無い場合に「重複ペア候補」とする。
- 既存の `ConflictDetector` が出す資源単位の衝突を、MODペア単位に集約して率を算出する新ロジック（例: `RedundancyDetector`）。

**表示**: 既存の診断（`Diagnostic` / 診断パネル）に「重複の可能性: A と B は◯◯を大きく重複。片方の無効化を検討」と日本語で1件追加。ブロックも自動操作もしない。

**検証**: 実機ダンプで、変種ペア（かすみ変種 altmodels/longhairvariant、双葉の髪 Black Hair Futaba 系、longhairann のパッチ群）が妥当に出るか、正常な「土台＋それを使うMOD」を誤検出しないか確認。しきい値はここで確定。

### (b) 旧エンジン撤去
本番（App / `AutoSortCoordinator` / `GameDiagnostics`）から参照されていないことを確認済み（grep でソース参照なし）。以下を削除:
- `src/ReloadedHelper.Core/LoadOrderOptimizer.cs` ＋ `LoadOrderOptimizerTests.cs`
- `src/ReloadedHelper.Core/WinnerResolver.cs` ＋ `WinnerResolverTests.cs`
- `src/ReloadedHelper.Core/PlacementHint.cs`（PlacementHintParser）＋ `PlacementHintParserTests.cs`

撤去後 `dotnet build` / `dotnet test` 緑、孤立参照ゼロを確認。

---

## テスト方針

- 各分類ルール（①②③a）は `ModTypeClassifier*Tests` に汎用語の単体テストを追加（特定MOD名でなく「`hair` を含む名前＋Characters → スキン」のような汎用ケース）。
- ③(b) は `ModTypeInfo.Rank(Unknown)` が中間であることのテスト。
- ④(a) は `RedundancyDetector` の単体テスト（重なり率・依存除外・しきい値境界）。
- **全変更の最終確認は実機ダンプ**（`_RealInstallDump.cs` を実行 → `classification-dump.txt` を before→after 比較）。`_RealInstallDump.cs` は使い捨て・未コミットのまま手元保持。

## 完了条件

- 実機ダンプで: Characters の内訳が分散（Black Hair Futaba＝スキン, dynmainmenu＝UI）／土台に d1r0 の前提物が入る／判定不能が減り残りは中間配置／冗長ペアが妥当に提示。
- 旧エンジン3クラス＋テストが消え、build/test 緑。
- 順序（先頭の土台前方）は不変であること（ラベル変更が順序を壊さない）。

## スコープ外（YAGNI）

- 起動時ログ/クラッシュ検知（将来枠）。
- 手動並び替えUI（北極星に反する）。
- 冗長ペアの自動無効化（提示のみ。操作はユーザー）。
