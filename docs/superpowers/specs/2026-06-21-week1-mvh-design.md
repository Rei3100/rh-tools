# Week 1 MVH（最小実行可能ハーネス）設計ドキュメント

**作成日：** 2026-06-21  
**ステータス：** Approved

---

## 目的

記事「Harness Engineeringベストプラクティス」の Week 1 を実装し、Claude Code が安全・確実に作業できる基盤を構築する。
一度設定すれば全セッションに効き続ける複利効果のある投資。

---

## スコープ

### グローバル（全プロジェクト共通）

#### 1. `~/.claude/settings.json`（新規）
全プロジェクトに共通するセーフティゲート（PreToolUse Hook）：
- システム破壊コマンドのブロック（`rm -rf /`、`rmdir /s /q C:\` 等）
- `git push --force origin main/master` のブロック
- `.env` ファイルの直接編集禁止
- `--no-verify` 付き git commit の禁止

#### 2. `~/.claude/CLAUDE.md`（更新）
- 各プロジェクトに `docs/adr/` と `AGENTS.md` を作る方針を 1〜2 行追記
- 作業フローの記述を Superpowers スキル名で整理

### プロジェクト（reloaded-helper 専用）

#### 3. `AGENTS.md`（新規）
ツール共通のルールブック（50行以下）：
- ビルド / テスト / 発行コマンド
- 禁止事項：ランタイム NuGet 追加禁止、データ保存先制約
- ADR の参照先ポインタ

#### 4. `CLAUDE.md`（更新）
Claude Code 専用の追加設定：
- `@AGENTS.md` でインクルード
- Superpowers ワークフロー / `.superpowers/` の使い方
- ADR ポインタ

#### 5. `.claude/settings.json`（新規）
プロジェクト固有のフック：
- **PostToolUse**: `.cs` ファイル編集後に `dotnet format --include <path>` を自動実行
- **PreToolUse**: `git commit` 前に `dotnet build --no-restore` でビルド確認・失敗時ブロック

#### 6. `.claude/hooks/post-edit.ps1`（新規）
PostToolUse Hook から呼ばれる PowerShell スクリプト：
- stdin から JSON でファイルパスを受け取る
- `.cs` ファイルのみ `dotnet format` を実行
- フォーマットエラーは無視して終了

#### 7. `.claude/hooks/pre-commit-guard.ps1`（新規）
PreToolUse Hook から呼ばれる PowerShell スクリプト：
- `git commit` 以外のコマンドはスルー（exit 0）
- `--no-verify` が含まれていたらブロック（exit 2）
- `dotnet build --no-restore` を実行、失敗したらブロック（exit 2）

#### 8. `docs/adr/0001-tech-stack.md`（新規）
最初の ADR（Architecture Decision Record）：
- C# / .NET 10 / WPF 選択の記録
- ランタイム NuGet 禁止の理由
- データ保存先制約の理由
- ステータス：Accepted

---

## AGENTS.md と CLAUDE.md の役割分担

```
AGENTS.md  ← ツール共通（Claude/Codex/その他 AIツールが読む）
             ビルドコマンド・禁止事項・ADR ポインタ

CLAUDE.md  ← Claude Code 専用
             @AGENTS.md（インクルード）
             + Superpowers ワークフロー
             + .superpowers/ の使い方
```

---

## 制約

- 追加 NuGet パッケージなし（System.Text.Json のみ）
- 有償サービスなし（Lefthook 不使用）
- フックスクリプトはすべて PowerShell（Windows 11 / Desktop App 対応）
- AGENTS.md・CLAUDE.md はそれぞれ 50 行以下

---

## 検証基準

1. グローバル safety gate: `rm -rf /` 等がブロックされる
2. PostToolUse Hook: `.cs` ファイル編集後 `dotnet format` が自動実行される
3. PreToolUse Hook: ビルドエラーがある状態で `git commit` がブロックされる
4. ADR: `docs/adr/0001-tech-stack.md` が存在・適切な内容
5. 行数: AGENTS.md / CLAUDE.md それぞれ 50 行以下
