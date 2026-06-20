# Phase 2.5 + Phase 3 設計書

作成日: 2026-06-20

## Context（なぜ作るのか）

rh-tools の本当の価値は「自動並び替え」。これがなければ毎日使う理由がない。
UI の日本語化・翻訳は「あれば便利」だが、自動並び替えは「ないと意味がない」コア機能。

設計方針:
- **Phase 2.5**: 現在の UI バグを先に直す（毎日使う快適さのため）
- **Phase 3**: 自動並び替え（ボタンなし・常に自動・起動時に適用）
- **Phase 4 以降**: GameBanana から情報取得 → カテゴリ情報で並び替えをさらに賢く + 翻訳

---

## Phase 2.5 — UI バグ修正（6件）

対象: `src/ReloadedHelper.App/`

| # | 修正内容 | 技術的な対応 |
|---|---|---|
| 1 | 起動時に最大化して起動 | `Window.WindowState = WindowState.Maximized`（起動時設定） |
| 2 | タスクトレイから復帰時も最大化 | NotifyIcon クリック時に `WindowState.Maximized` をセット |
| 3 | 多重起動禁止・既起動時は最前面 | `Mutex` でシングルインスタンス制御、既存プロセスに `WM_ACTIVATE` |
| 4 | 設定画面の%表示・フォルダ名の文字化け | バインディング・フォーマット文字列を確認・修正 |
| 5 | 設定・ヘルプをオーバーレイ表示 | サイドナビ切替からモーダルダイアログ方式に変更（MOD 画面の上に重ねて表示） |
| 6 | UI拡大率をMODリスト画面を見ながら変更 | オーバーレイ設定を半透明で表示、またはメイン画面にスライダーを常駐 |

---

## Phase 3 — 自動並び替え

### ゴール

ユーザーが何もしなくても、rh-tools 起動時に常に最適なロードオーダーが維持される。
Reloaded-II の AppConfig.json を直接書き換えることで、次回のゲーム起動から反映される。

### アルゴリズム

**トポロジカルソート（依存関係ベース）**

1. 全 MOD の `ModDependencies` フィールド（Phase 1 で読み込み済み）を使用
2. 有向グラフを構築: 「A が B に依存」→ エッジ B→A（B を A より先に配置）
3. Kahn のアルゴリズムでトポロジカル順序を計算
4. 依存関係のない MOD 同士は現在の相対順序を維持（安全第一）
   - 循環依存が検出された場合は警告ログを出力し、そのグループの並び替えをスキップ

**Phase 4 で改善予定**: GameBanana カテゴリ情報（フレームワーク系を優先など）を使ってさらに賢く並び替え

### 書き込み対象

```
C:\FreeSoft\Reloaded-II\Apps\{appId}\AppConfig.json
  → "EnabledMods" 配列の順序を変更
```

### バックアップ仕様

- 書き換え前に自動でバックアップ作成
- 保存先: `%APPDATA%\ReloadedHelper\backups\{appId}\{yyyy-MM-dd_HH-mm-ss}.json`
- 直近 3 世代を保持（それ以前は自動削除）
- UI から「1つ前の状態に戻す」操作を提供

### 新規追加クラス（Core）

| クラス | 責務 |
|---|---|
| `LoadOrderSorter` | MOD 依存グラフのトポロジカルソート。副作用なし、純粋なロジック層 |
| `AppConfigWriter` | AppConfig.json の読み書き。書き換え前にバックアップを呼ぶ |
| `LoadOrderBackupService` | バックアップの保存・列挙・復元 |

### 既存コードとの接続

- `ModInfo.ModDependencies`（既存）をソートのインプットとして使用
- `AppConfigParser`（既存）の読み取りロジックを `AppConfigWriter` で参照
- `MainViewModel` の初期化フロー（既存）に自動並び替えを追加

### テスト方針

`LoadOrderSorter` は純粋な関数のため xUnit でグラフパターンをテスト:
- 正常: 依存あり → 正しい順序
- 正常: 依存なしは元の順序を維持
- 異常: 循環依存 → スキップして警告

---

## 自動アップデート機能

### ゴール

`main` に push → GitHub Actions がビルド・リリース → `C:\FreeSoft\ReloadedHelper\ReloadedHelper.App.exe` が自動で最新版に更新される。

### 起動時の更新チェックフロー

```
アプリ起動
  ↓
GitHub Releases API (api.github.com) で最新タグを確認
  ↓
現在のアセンブリバージョンと比較
  ↓ 新バージョンあり
バックグラウンドでダウンロード
  ↓ 完了
通知: 「v0.4.0 に更新しました。再起動してください」
または自動で再起動（設定で選択可）
```

### 自己更新の仕組み

1. 新 exe を `%TEMP%\rh-tools-update.exe` にダウンロード
2. PowerShell ワンライナーを起動: `Start-Sleep 1; Copy-Item <temp> <current>; Start-Process <current>`
3. 現在のプロセスを終了 → PowerShell が新 exe に置き換えて再起動

### バージョン管理

- GitHub Actions で tag（例: `v0.3.0`）を打つとビルド＆リリース
- `AssemblyVersion` を Actions の tag から設定（`/p:Version=${{ github.ref_name }}` ）
- リリースに exe を添付（現行の Artifact から GitHub Release に変更）

### インストール先

`C:\FreeSoft\ReloadedHelper\ReloadedHelper.App.exe` を正式インストール先とする。
CI の初回ダウンロードでここに配置すれば、以降は自動更新のみ。

---

## Phase 4 メモ（今回は実装しない）

- GameBanana API から説明文・カテゴリ取得
- 日本語自動翻訳（無料 API）
- URL のない MOD の URL 自動探索
- カテゴリを使ったより賢い並び替え（フレームワーク系を先頭固定など）
- URL 一覧の手動編集 UI

---

## 確認方法

| # | 確認内容 |
|---|---|
| 1 | 起動時にウィンドウが最大化されている |
| 2 | タスクトレイから復帰時も最大化される |
| 3 | 2重起動で既存ウィンドウが最前面に出る |
| 4 | 依存関係のある MOD が「依存先が上」になっている（Reloaded-II と一致確認） |
| 5 | バックアップが `%APPDATA%\ReloadedHelper\backups\` に作成されている |
| 6 | GitHub に tag push 後、`C:\FreeSoft\ReloadedHelper\ReloadedHelper.App.exe` が自動更新される |
| 7 | 設定・ヘルプが MOD リストの上にオーバーレイで表示される |
| 8 | UI 拡大率を変更しながら MOD リストがリアルタイムで拡縮する |
