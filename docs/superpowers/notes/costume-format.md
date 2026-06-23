# コスチュームフレームワーク 実機形式調査メモ

調査日: 2026-06-23  
調査対象: `C:/FreeSoft/Reloaded-II/Mods/` 内の全コスチューム系MOD

---

## 1. コスチュームフレームワーク MOD の存在確認

**フレームワーク本体:** `P5R.CostumeFramework`（存在確認済み）

**CostumeFramework を依存として持つ MOD（実機確認済み）:**

| MOD ID | 枠数（キャラ×スロット） |
|--------|----------------------|
| `p5rpc.models.p4aultimaxoutfitpack` | 9キャラ × 1スロット |
| `p5rpc.models.p4doutfits` | 9キャラ × 1スロット |
| `p5rpc.skin.p3outfits` | 9キャラ × 1スロット |
| `p5rpc.models.P5XJokerOutfits` | Joker × 4スロット |
| `p5rpc.skins.butleroutfitsretextured` | 9キャラ × 1スロット（config.yaml なし） |
| `p5rpc.lastsurpriseskins` | 複数キャラ × 1～2スロット（Jokerのみ2） |

**CostumeFramework を使わない旧形式（非対象）:**
- `p4gpc.accuratevelvetroomcostumes` → `P5REssentials/CPK/` 直下にファイル配置（CostumeFramework 非依存）
- `Kawakami.Ponytails.Outfits` → 同上

---

## 2. フォルダ構造

### 標準構造（CostumeFramework 形式）

```
<ModRoot>/
  Costumes/
    <キャラ名>/           ← 英語表記（Joker, Ann, Ryuji, Yusuke, Makoto, Akechi, Futaba, Haru, Sumire, Morgana）
      <スロット名>/       ← 任意の英語文字列（スペース・括弧可）
        config.yaml       ← 省略可能（ない場合もある）
        description.msg   ← コスチューム説明テキスト
        *.BCD 等          ← サウンド・アニメーション等の追加ファイル（任意）
      <スロット名>.GMD    ← モデルデータ（スロットフォルダと同名）
  ModConfig.json
```

### 実例

```
p5rpc.models.p4aultimaxoutfitpack/
  Costumes/
    Joker/
      ArenaUltimax/
        config.yaml
        description.msg
      ArenaUltimax.GMD
    Ann/
      ArenaUltimax/
        config.yaml
        description.msg
      ArenaUltimax.GMD
    ...（8キャラ分）

p5rpc.models.P5XJokerOutfits/
  Costumes/
    Joker/
      Incognito Outfit (P5X)/   ← スペースや括弧を含むスロット名
        config.yaml
        description.msg
      Incognito Outfit (P5X).GMD
      Phantom Suit (P5X)/
        config.yaml
        ...
      Shujin Winter (P5X)/
      Wonder's Outfit (CBT3)/
```

---

## 3. config.yaml の構造

```yaml
# スロット表示名（省略時はフォルダ名が使われる）
name: "Ultimax Outfit"

# キャラクターアセットスロット番号（0 = デフォルト）
character_assets: 0

# デフォルト衣装フラグ
is_default: false
```

- `name`: ゲーム内に表示されるコスチューム名
- `character_assets`: 整数。どのアセットスロットに対応するかを指定
- `is_default`: true の場合、デフォルト衣装として扱われる

**config.yaml が存在しないスロットもある**（`p5rpc.skins.butleroutfitsretextured` の全スロット、`p5rpc.lastsurpriseskins` の一部）。  
その場合、フォルダ名がスロットキーとして機能する。

---

## 4. キャラ・スロットペアの抽出方法（CostumeAnalyzer 向け）

```
Costumes/<キャラ名>/<スロット名>/  が存在する = 1コスチューム枠
```

抽出ロジック:
1. `<ModRoot>/Costumes/` フォルダを探す
2. 直下のサブディレクトリ名 = **キャラ名**
3. キャラ名フォルダ直下のサブディレクトリ名 = **スロット名**（スロットキー）
4. スロット名フォルダに `config.yaml` があれば `name` フィールドを読む（表示名として使用）
5. なければフォルダ名をそのままスロット表示名にする

**既知のキャラ名一覧（実機確認済み）:**
Joker, Ann, Ryuji, Yusuke, Makoto, Akechi, Futaba, Haru, Sumire, Morgana

---

## 5. 「一部しかランダムにならない」現象への観察メモ

- 同じゲーム内コスチューム枠（同一 `character_assets` 番号）に複数 MOD が競合している可能性。
- `p5rpc.lastsurpriseskins` では Joker だけが 2スロット（`Last Surprise (Kasumi)` / `Last Surprise (Sumire)`）を持っており、他キャラは 1スロット。枠が埋まらないキャラはランダム候補に入らないかもしれない。
- `config.yaml` を持たないスロット（butler outfit 全般）がランダムから外れる可能性もあるが、未確認。
- 解決は将来タスクで対応予定。

---

## 6. 設計書プレースホルダとの一致確認

設計書の想定: `Costumes/<character>/<slot>/` フォルダ階層  
**→ 実機と完全一致。** ただし:

- スロット名はシンプルな英数字だけでなく、スペース・括弧・アポストロフィを含む場合がある（例: `Wonder's Outfit (CBT3)`）
- `config.yaml` は省略可能（存在しないスロットがある）
- スロットフォルダと同名の `.GMD` ファイルが並列に置かれる（モデルデータ）

---

## 7. CostumeAnalyzer への実装指針

- 判定条件: `Costumes/` フォルダが存在し、かつ ModConfig.json の `ModDependencies` に `"P5R.CostumeFramework"` を含む
  - または `Costumes/` 以下に `<キャラ>/<スロット>/` の 2 階層フォルダが存在するだけでも判断可能
- スロット列挙: `Costumes/*/*/` を glob で走査
- `config.yaml` の `name` が存在すれば使用、なければフォルダ名をスロット表示名として使用
- キャラ名はフォルダ名をそのまま使用（大文字小文字を保持）
