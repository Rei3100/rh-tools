# Phase 4: GameBanana 全自動取得 + 日本語化 設計書

## 目的

rh-tools の全 MOD を GameBanana から最新データで更新し、完全自動で日本語化する。
Reloaded-II 本体も rh-tools も両方日本語で表示されるようにする。

## 制約

- **無料サービスのみ**（API キー不要・アカウント登録不要）
- **Claude Code 非依存**（スタンドアロン動作必須）
- **英語データ保持不要**（MOD 名・説明文は日本語に直接上書き）
- **手動操作ゼロを目指す**（自動化できる部分はすべて自動）

## 対象ゲーム

- ペルソナ5 ザ・ロイヤル (AppId: p5r.exe 相当)
- ペルソナ4 ザ・ゴールデン (AppId: p4g.exe 相当)
- ペルソナ5スクランブル ザ・ファントムストライカーズ (AppId: p5s.exe 相当)

---

## アーキテクチャ

### 新規 Core クラス

| クラス | ファイル | 役割 |
|--------|---------|------|
| `GameBananaClient` | `GameBananaClient.cs` | GameBanana API 呼び出し（MOD 情報取得・検索） |
| `TranslationService` | `TranslationService.cs` | Google Translate 非公式 API で英語→日本語 |
| `GlossaryProvider` | `GlossaryProvider.cs` | ゲーム固有用語辞書（翻訳後補正） |
| `ModConfigUpdater` | `ModConfigUpdater.cs` | ModConfig.json の ModName・ModDescription 上書き |

### 既存 Core クラスの変更

| クラス | 変更内容 |
|--------|---------|
| `UserData.cs` の `ModUserData` | `GameBananaId`・`Category`・`FetchedAt`・`FetchedVersion` フィールド追加 |
| `Models.cs` の `ModLoadEntry` | `Category` フィールド追加（userdata.json から注入、省略可能）<br>`public sealed record ModLoadEntry(int Order, string ModId, ModInfo? Info, bool Enabled, string? Category = null)` |
| `Catalogs.cs` の `LoadOrderBuilder.Build()` | `UserDataFile` を受け取り `ModLoadEntry.Category` を設定 |
| `MainViewModel.cs` | UserData をロードして `LoadOrderBuilder` に渡す。`IsUpdating`、`UpdateProgress` プロパティ追加 |

### 変更 App クラス

| ファイル | 変更内容 |
|---------|---------|
| `App.xaml.cs` | 起動時 `Task.Run(RefreshModMetadataAsync)` 追加 |
| `Views/ModListView.xaml` | 「今すぐ更新」ボタン、プログレスラベル、カテゴリバッジ追加 |
| `Views/ModListView.xaml.cs` | 更新ボタンハンドラ追加 |

---

## データフロー

```
起動時 (バックグラウンドスレッド)
│
├─ UserDataStore.Load() で userdata.json を読み込み
│
├─ 全 MOD をスキャン (mainVm.AllMods)
│   └─ 各 MOD について:
│       ├─ userdata.json に FetchedVersion == 現在のバージョン → スキップ
│       └─ 未処理 or バージョン変化 → 処理対象
│
├─ GameBananaClient.FindIdAsync(modInfo)
│   ├─ ① ProjectUrl に gamebanana.com/mods/{id} → 直接 ID 抽出
│   ├─ ② ModId / ModName で GameBanana 検索 → 名前類似度 80%+ で自動リンク
│   └─ ③ マッチなし → 翻訳のみ（GameBanana ID なし）
│
├─ (ID あり) GameBananaClient.FetchAsync(id)
│   └─ name, text, Category を取得
│
├─ TranslationService.TranslateAsync(text, "ja")
│   └─ translate.googleapis.com/translate_a/single?client=gtx
│
├─ GlossaryProvider.Apply(translatedText, appId)
│   └─ ゲーム固有用語で誤訳を置換
│
├─ ModConfigUpdater.Write(modFolderPath, japaneseName, japaneseDescription)
│   └─ ModConfig.json の ModName + ModDescription を上書き
│
├─ userdata.json 更新 (GameBananaId, Category, FetchedAt, FetchedVersion)
│
└─ UI スレッドで mainVm.LoadFrom(install) 再読み込み
```

---

## GameBanana マッチングアルゴリズム

### ステップ1: 直接 URL 抽出（確度 100%）

```
ProjectUrl = "https://gamebanana.com/mods/123456"
              ↓
GameBananaId = "123456"
```

`ProjectUrl` が `gamebanana.com/mods/` または `gamebanana.com/dl/` を含む場合は即 ID 確定。

### ステップ2: 名前検索（確度 高）

```
GameBanana API:
GET https://api.gamebanana.com/apiv11/Util/Search/Results
    ?search_query={ModName}&itemtype=Mod&gameid={gameId}&page=1&nperpage=5

各結果と ModName を正規化比較:
  - 両方を小文字化・記号除去
  - 類似度 80% 以上 → 自動リンク
  - 最高スコアの結果を採用
```

**ゲーム ID の自動検出（ハードコードなし）:**
同じゲーム（同一 AppId）に GameBanana URL が判明している MOD が 1 つでもあれば、その MOD の GameBanana game ID を API から取得してキャッシュする。
以後、同ゲームの全 MOD 検索はその game ID でフィルタ。

### ステップ3: マッチなし

GameBanana ID なしでも、既存の英語 MOD 名・説明文を翻訳して ModConfig.json に書く。
カテゴリは `null`（バッジ非表示）。

---

## 翻訳 API

**Google Translate 非公式エンドポイント（無料・キー不要）:**

```
GET https://translate.googleapis.com/translate_a/single
    ?client=gtx&sl=en&tl=ja&dt=t&q={URLエンコード済テキスト}
```

レスポンス: `[[["翻訳テキスト","原文"...],...],null,"en"]`

翻訳後、GlossaryProvider でゲーム固有用語を置換する。

**レート制限対策:**
- 連続リクエスト間に 100ms の遅延
- 失敗時は 1回リトライ、それでも失敗なら元の英語テキストを保持

---

## ゲーム固有用語辞書（GlossaryProvider）

AppId をキーに用語マップを保持。GameBanana から取得した name / text に翻訳適用後、以下で置換。

### P5R 用語（例）

| 英語 | 日本語 |
|------|--------|
| Phantom Thieves | 怪盗団 |
| Joker | ジョーカー |
| Metaverse | メタバース |
| Velvet Room | ベルベット・ルーム |
| Palace | パレス |
| Mementos | メメントス |
| Persona | ペルソナ |
| Confidant | コープ |
| Shadow | シャドウ |
| Cognitive | 認知 |
| Ryuji | 竜司 |
| Ryuji Sakamoto | 坂本竜司 |
| Ann | 杏 |
| Ann Takamaki | 高巻杏 |
| Yusuke | 祐介 |
| Yusuke Kitagawa | 喜多川祐介 |
| Makoto | 真希 |
| Makoto Niijima | 新島真 |
| Futaba | 双葉 |
| Futaba Sakura | 佐倉双葉 |
| Haru | 春 |
| Haru Okumura | 奥村春 |
| Morgana | モルガナ |
| Akechi | 明智 |
| Goro Akechi | 明智吾郎 |
| Lavenza | ラヴェンザ |
| Igor | イゴール |
| Sojiro | 双葉の父 |
| Kasumi | 霞 |

### P4G 用語（例）

| 英語 | 日本語 |
|------|--------|
| Investigation Team | 捜査隊 |
| Yu Narukami | 鳴上悠 |
| Yosuke | 陽介 |
| Chie | 千枝 |
| Yukiko | 幸子 |
| Kanji | 完二 |
| Rise | 里奈 |
| Teddie | クマ |
| Naoto | 直斗 |
| Midnight Channel | 深夜テレビ |
| Shadow World | シャドウ |

### P5S 用語（例）

| 英語 | 日本語 |
|------|--------|
| EMMA | エマ |
| Jail | 監獄 |
| Monarch | モナーク |
| Sophia | ソフィア |
| Zenkichi | 善吉 |

> 辞書は `GlossaryProvider.cs` 内にハードコード（JSON ファイル不要）。正規表現で単語境界マッチ。

---

## UserData 拡張

`ModUserData` に以下フィールドを追加:

```csharp
public sealed class ModUserData
{
    // 既存フィールド
    public string? TranslatedName        { get; set; }
    public string? TranslatedDescription { get; set; }
    public string? UrlOverride           { get; set; }
    public string? Notes                 { get; set; }

    // Phase 4 追加
    public string?   GameBananaId    { get; set; }
    public string?   Category        { get; set; }  // "Sound", "Skin", "Gameplay", etc.
    public DateTime? FetchedAt       { get; set; }
    public string?   FetchedVersion  { get; set; }  // MOD バージョン文字列
}
```

---

## ModConfig.json 更新

`ModConfigUpdater.cs` が行う処理:

1. `ModConfig.json` を `JsonDocument.Parse()` で読み込み
2. `ModName` を日本語名で、`ModDescription` を日本語説明文で置換
3. 他のフィールドはすべて保持してインデント付きで書き出し
4. バックアップなし（ユーザー要望）

---

## カテゴリバッジ

GameBanana のカテゴリを日本語表示にマッピング:

| GameBanana 値 | 表示 |
|--------------|------|
| Sound | サウンド |
| Skin | スキン |
| Texture | テクスチャ |
| UI | UI |
| Gameplay Mechanics | ゲームプレイ |
| Misc | その他 |
| Quality Of Life | QOL |

MOD カードにスモールバッジとして表示（TextBlock with Background）。

---

## UI 変更

### ModListView ヘッダ

```
[読み込み順 ・ 全 218 件]  [全て] [有効] [無効]  [今すぐ更新]
```

処理中は:
```
[更新中 45/218 件...]
```

### MOD カード（既存の4列構成に追加）

```
[順番] [アイコン] [MOD名 + 作者]            [バッジ] [トグル]
 1     🖼        CRI FileSystem V2 Hook      [サウンド]  ●
                  Sewer56
```

### 詳細パネル

- カテゴリを「作者」「バージョン」の下に追加表示
- 説明文は日本語（ModConfig.json から読み込んだもの）

---

## 起動フロー

```csharp
// App.xaml.cs の OnStartup
modListVm.LoadFrom(install);
ApplySortAllGames(modListVm, install);

// バックグラウンドで並列実行（既存パターンと同様）
_ = Task.Run(() => CheckAndApplyUpdateAsync());
_ = Task.Run(() => RefreshModMetadataAsync(modListVm, install, settingsVm));
```

`RefreshModMetadataAsync` は非同期・バックグラウンドで実行。
完了後に `Dispatcher.Invoke(() => modListVm.LoadFrom(install))` でリロード。

---

## エラーハンドリング

| 状況 | 対処 |
|------|------|
| GameBanana API タイムアウト | 3秒でタイムアウト、スキップして次の MOD へ |
| 翻訳 API 失敗 | 1回リトライ、失敗なら元テキストを保持 |
| ModConfig.json 書き込み失敗 | エラーログのみ、スキップ（アプリ起動は継続） |
| マッチ信頼度が低い | マッチなし扱いにして翻訳のみ |

---

## テスト方針

- `GameBananaClient`, `TranslationService`, `GlossaryProvider`, `ModConfigUpdater` は xUnit でユニットテスト
- HTTP 呼び出しは `HttpClient` を DI でモック化
- 翻訳精度は目視確認（自動テストの対象外）

---

## スコープ外（Phase 5 以降）

- MOD 個別コンフィグ編集（MOD ごとの設定ファイル）
- 翻訳手動修正 UI
- カテゴリでのフィルタリング
