# 自動並び替え：種類ベース分類への改訂 設計仕様（v2）

- 日付: 2026-06-23
- 対象: ReloadedHelper.Core（ロジック層）＋ App（表示のみ）
- 状態: 実データ検証済み・実装計画へ
- 前提: [2026-06-23-autosort-full-layered-design.md](2026-06-23-autosort-full-layered-design.md) の改訂。層別フル整列の枠組みは維持し、**分類の精度**を抜本的に上げる。

## 1. 背景：v1 実装の実機での失敗

層別フル整列（v1）を実機（ユーザーの P5R 環境・217 MOD）で動かした結果、並びがグチャグチャで実用にならなかった。フィードバック：「フレームワークが前に来た以外、残りはバラバラ」「配置理由の質が低い」。

### 根本原因（実データ 311 MOD / 並び 217 MOD で確定）

- **役割分類が GameBanana カテゴリにほぼ依存**しており、カテゴリは 42% が null。さらに最頻カテゴリ（Other/Misc・QOL・Fixes・Cheats）が全て `Unknown` にマップされる。
- 結果、**217 MOD 中 216 個＝69% が「不明（rank4）」に collapse**し、巨大な「不明」の山が元の順のまま並ぶ＝これがグチャグチャの正体。
- **「音楽」役割は実質ゼロ**。音楽 MOD はカテゴリ無しで AWB 音声差し替え等のため、見た目や不明に誤分類され散らばる。
- 一方、**217 MOD 中 71% は MOD 名・ID・説明文に種類キーワードを含む**（music 55, fix 66, model 41, costume 38, skin 37, battle 35 …）。この強い信号を v1 は完全に無視していた。

## 2. ゴール

- 各 MOD を**細かい「種類（ModType）」に分類**し、種類ごとに固まって並ぶようにする。
- カテゴリが無い MOD も、名前・ID・説明文・フォルダ構成から種類を判定する。
- 「不明」を大幅に削減する（実データ目標: ≤ 10%。プロトタイプ実測: 3%）。
- 配置理由を種類ベースの具体的なものにする。
- 有効/無効は分けない（ユーザー合意済み。混在のままで良い）。

### 非ゴール

- sounds/wips/GitHub のオンライン取得対応（種類分類で不要）。
- 適用前プレビュー承認 UI。
- Persona 以外のゲーム向け汎用化（当面 Persona 中心）。

## 3. 種類（ModType）とレイヤー順

ランクが小さいほどリスト前方（弱い・土台）。同一ランク内は元の相対順を維持。

| ランク | ModType | ラベル | 例 |
|---|---|---|---|
| 0 | Library | ライブラリ/前提 | フレームワーク、IsLibrary |
| 1 | Gameplay | ゲーム機能・修正 | チート/Fix/QOL/難易度 |
| 2 | Battle | 戦闘・ボス | ボス AI、戦闘改変 |
| 3 | Event | イベント・ストーリー | カットシーン/スクリプト/FMV |
| 4 | Music | 音楽・BGM | BGM 差し替え、サウンド |
| 5 | Model | モデル | キャラ/武器モデル |
| 6 | Costume | 衣装 | Costumes 差し替え |
| 7 | SkinTexture | スキン・テクスチャ | スキン/テクスチャ/リカラー |
| 8 | Portrait | 立ち絵 | バストアップ/ポートレート |
| 9 | Ui | UI | HUD/メニュー/カットイン |
| 10 | Unknown | 判定不能 | 手がかりなし（末尾） |

## 4. 分類アルゴリズム（ContentTypeClassifier）

責務：`ModInfo`（＋カテゴリ）から `ModType` と日本語の理由を返す。判定は上から順に最初に当たったもの。

1. `IsLibrary` が真 → Library（理由「ライブラリ指定」）。
2. フォルダ構成（強い信号）：
   - `BGME/` 配下あり → Music。
   - `Costumes/` 配下あり → Costume。
3. **特定の GameBanana カテゴリ**（信頼できるもの）→ 対応 ModType：
   - Skins/Skin/Textures/Texture/Texture Packs → SkinTexture
   - Portraits/Portrait → Portrait
   - Models/Model Packs/Personas/Persona → Model
   - User Interface/UI → Ui
   - Battles → Battle
   - Cutscenes / FMV/Cutscenes → Event
   - Sound/Music → Music
4. **キーワード判定**：`ModId`＋`ModName`＋`ModDescription`（小文字・部分一致）を、次の優先順で評価し最初に当たった種類：
   Music → Portrait → Costume → SkinTexture → Model → Ui → Battle → Event → Gameplay
   （キーワード表は実装計画で確定。日英両方を含める。例: music=[music,bgm,sound,ost,song,サウンド,音楽] 等）
5. **曖昧カテゴリのフォールバック**：
   - Characters/Character → Model
   - Other/Misc・QOL・Fixes・Cheats・Events・Bomb/Defuse → Gameplay
6. **ファイル上書きのみ**（`P5REssentials/CPK` か `FEmulator/AWB` 配下にファイルがある）→ SkinTexture（資産差し替えとみなす）。
7. それ以外 → Unknown。

> 既存 `ModRoleClassifier`（カテゴリ→粗い役割）と `ContentRoleClassifier`（v1）は本分類器に統合・置換する。`ModRole`/`ModLayer` は `ModType` ベースへ移行する。

## 5. 整列とデータフロー

- `LoadOrderOptimizer` は層ランクを `ModType` のランク（表 §3）から取り、全 MOD を `(typeランク昇順, 元の相対順)` で安定整列 → 依存ソート → 既存の衝突解決を重ねる（v1 の枠組みを踏襲）。
- 全 MOD に種類ベースの配置理由（種類ラベル＋判定根拠）を `Placements` として返す。UI 詳細パネルに表示（既存）。
- 衝突解決の勝者決定（`DecideByRole`）は、SkinTexture/Portrait/Ui/Model 等「見た目系（後方ランク）」を勝たせ、Gameplay/Battle 等「土台系（前方ランク）」を負けにする方針へ更新（ランク大＝勝ち、で一意なら自動、同点・不明は要確認）。

## 6. 受け入れ検証（v1 の失敗を繰り返さないため必須）

- **ユニットテスト**：代表的な入力（各種類のキーワード/カテゴリ/フォルダ/IsLibrary、曖昧カテゴリ、手がかりなし）で `ModType` と理由を検証。
- **実データ・シミュレーション**：ユーザー実機の 217 MOD に対し、実装後の分類で「Unknown ≤ 10%」「各種類が固まって並ぶ」ことを再確認してから完了とする。
- 既存テストの非回帰。

## 7. リスクと対策

| リスク | 対策 |
|---|---|
| キーワード誤判定（例: modloader が戦闘に紛れる） | 優先順位・キーワード表を実データのサンプル誤判定を見て調整。理由を必ず表示し目視可能に。 |
| 種類境界の曖昧さ（スキン vs 衣装） | グループとしてまとまっていれば実用上問題なし。Costumes フォルダ実装は Costume に寄せる。 |
| 大移動への不安 | 既存バックアップ／復元を維持。全件理由表示で透明化。 |
