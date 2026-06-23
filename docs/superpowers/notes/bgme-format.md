# BGME 音楽MOD 実機ファイル形式 調査メモ

調査日: 2026-06-23  
調査対象: `C:\FreeSoft\Reloaded-II\Mods`

---

## 1. BGME 系 MOD の存在確認

多数の BGME 系 MOD が確認された。代表例：

```
BGME.Framework           (フレームワーク本体)
BGME.Framework.API
BGME.BattleThemes
BGME.Reactive
P5R.Boss.Music
p4g.bgm.overworld
p4g.bgm.shadowbosses
p4gpc.musicenhancementpack
p5r.music.darksun
MementosBossBattles_575967
(Personal)ArchangelBat_821893
MadarameBloomingVillia_431180
p5rpc.bgm.endlessdays
... 他多数
```

---

## 2. 曲ID定義ファイルの形式

### 形式 A: `.pme` ファイル（主流）

**ファイルパス:** `<MODフォルダ>/BGME/*.pme`

**文法:** BGME独自DSL（ドメイン固有言語）。encounter ブロックが単位。

#### パターン 1: 数値EncounterID → 数値SongID（最もシンプル）

```pme
encounter[779]:
  music = 10828
end
```

- `encounter[<数値>]` → エンカウンターID（整数）
- `music = <数値>` → 曲ID（整数）

例: `p5r.music.darksun/BGME/akechiphase1.pme`

#### パターン 2: 1ファイルに複数エンカウンター（複数曲を持つ MOD）

```pme
encounter[773]:
  music = 19988
end

encounter[842]:
  music = 19988
end

encounter[828]:
  music = 19989
end
```

例: `P5R.Boss.Music/BGME/MusicScript.pme`（15エンカウント以上）

#### パターン 3: 名前付きコレクション参照

```pme
encounter["Void Quest"]:
    music = random_song(10103, 10104)
end

encounter["Minibosses"]:
    music = 10109
end
```

- `encounter["<文字列>"]` → コレクション名（ダブルクォート文字列）
- `music = random_song(<songID1>, <songID2>, ...)` → ランダム選択
- `music = "default"` → デフォルト曲を使用（ID指定なし）

例: `p4gpc.musicenhancementpack/BGME/MusicEnhancementPack.pme`

#### パターン 4: const 変数によるランダム曲 一括適用

```pme
const myRandomBgm = random_song(10851, 10852)

encounter[712]:
  music = myRandomBgm
end

encounter[707]:
  music = myRandomBgm
end
```

例: `MementosBossBattles_575967/BGME/Mementos Bosses.pme`

#### パターン 5: global_bgm（エンカウント以外の場所BGM）

```pme
global_bgm[77]:
  music = 10101
end
```

例: `p4gpc.musicenhancementpack/BGME/MusicEnhancementPack.pme`

#### パターン 6: コメント行

```pme
// === Area collections ===
// Void Quest: ROTTT 14.4mL, TTMH BitLemur
```

`//` 行コメントあり。

---

### 形式 B: `music.yaml`（フレームワーク定義・曲名対応表）

**ファイルパス:** `BGME.Framework/<ゲーム名>/music.yaml`

これはフレームワーク本体の定義ファイルであり、MOD が持つものではない。
曲ID と実ファイルパスのマッピングを提供する。

```yaml
name: Persona 5 Royal BGM (PC)
tracks:
- name: Endless Days
  cue_id: 28
  output_path: FEmulator/AWB/BGM.AWB/0.adx
- name: Ideal and the Real
  cue_id: 44
  output_path: FEmulator/AWB/BGM.AWB/1.adx
```

BgmeAnalyzer での解析対象は `.pme` ファイルを優先する。

---

### 形式 C: FEmulator/AWB パス（.adx ファイル直置き）

```
p5r.music.colorsflyinghigh/FEmulator/AWB/BGM.AWB/00020_streaming.adx
p5rpc.bgm.endlessdays/FEmulator/AWB/BGM.AWB/000000_streaming.adx
```

BGME フレームワークを使わず、AWB スロット番号でそのまま差し替える方式。
この方式では `.pme` がなく、**ファイルパスのスロット番号が曲IDに相当する**。

例:
- `BGM.AWB/00020_streaming.adx` → スロット 20
- `BGM_42.AWB/828.adx` → アーカイブ BGM_42.AWB のスロット 828

---

## 3. 曲ID 抽出方法まとめ

### `.pme` ファイルから曲IDを抽出する正規表現

**数値 SongID を持つ `music =` 行:**

```
music\s*=\s*(\d+)
```

グループ 1 が曲ID（整数）。

**`random_song(...)` の全引数から複数 SongID を抽出:**

```
random_song\(([^)]+)\)
```

グループ 1 をカンマ分割して整数リストへ変換。

**`const` 変数名の定義を追跡する場合（発展）:**

```
const\s+(\w+)\s*=\s*random_song\(([^)]+)\)
```

変数名 → SongID リストのマップを構築してから encounter ブロックを解析。

### AWB スロット番号の抽出

ファイルパス `FEmulator/AWB/<アーカイブ名>/<番号>*.adx` から：

```
/(\d+)[^/]*\.adx$
```

グループ 1 がスロット番号（= 曲ID相当）。

---

## 4. BgmeAnalyzer 実装への指針

1. **検出条件:** `BGME/` ディレクトリ内に `.pme` ファイルが存在するか、`FEmulator/AWB/` に `.adx` ファイルが存在する場合に BGME 系 MOD と判定。

2. **SongID 抽出（.pme）:**
   - `music\s*=\s*(\d+)` で直接 SongID を取得
   - `random_song\(([^)]+)\)` でランダム選択 SongID リストを取得
   - `music = "default"` は SongID なし（デフォルト曲使用）として扱う

3. **SongID 抽出（AWB 直置き）:**
   - `FEmulator/AWB/<アーカイブ>.AWB/<番号>*.adx` のパターンでファイルスキャン
   - 番号部分を SongID として使用

4. **複数 SongID:** 1 MOD が複数の encounter ブロックを持つのは通常運用。SongID は `HashSet<int>` で重複排除して収集する。

5. **コンフリクト判定:** 同一 SongID を複数 MOD が持つ場合に競合として報告する。
