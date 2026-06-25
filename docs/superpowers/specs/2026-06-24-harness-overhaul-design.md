# 土台ハーネス強化 設計書（フェーズ A+C）

- 日付: 2026-06-24
- ステータス: 承認済み（実装計画へ）
- 対象範囲: グローバル（`~/.claude`）＋ プロジェクト（reloaded-helper）の 2 層
- 関連: `docs/adr/0001-tech-stack.md`、`C:\Users\rainb\md\*`（Harness Engineering 記事3点 + skill-auditor 記事）

## 1. 背景と目的

開発ワークフロー（本線 = superpowers）を、**プロンプト（お願い）ではなく仕組み**で安定させる「補助輪（ハーネス）」を、参照記事の MVH（最小実行可能ハーネス）Week1 + Week2-4 に沿って一段進める。

ユーザーは非プログラマー。絶対条件は **品質を落とさない / リスク最小 / 課金は Claude のみ（外部有償サービス禁止）**。

元の依頼は 3 つに分解した:
- **A. 強制ルール**（質問は必ず AskUserQuestion／サブエージェント確認をやめる／superpowers 常時／3 プラグイン自動使用／外部AI制限中の誤診断防止）
- **B. 外部AI連携**でトークン節約
- **C. 環境構築**（Week1 + Week2-4）

**進め方（承認済み）**: 土台 **A+C を先に固める**。**B（外部AI）は後続**（記事の「まず 1 エージェントでハーネスを磨いてからスケール」に従う）。本設計書は A+C を対象とする。

## 2. ブレインストーミングで得た重要な判断（証拠ベース）

1. **「サブエージェント確認」の正体は権限設定だった**: 直近セッションログ（`0886f889`）に「`permissionMode:"auto"` で Agent（サブエージェント起動）ツールが自動モードの分類器にブロックされ、Claude が"サブエージェントなしで進めますか"と代替案を出した」記録あり。→ これは**指示では直らない。権限設定で根治する。**
2. **AskUserQuestion / Skill は「たまに抜ける」型**: ログ集計で毎セッション使われているが回数にムラ（最大 15〜0）。「全く使わない」ではない。→ リマインドは**超軽量で十分**（盛りすぎは記事の警告通り逆効果）。
3. **WPF デスクトップアプリのため Web 用 E2E（Playwright 等）は不適合**: UI 自動化は導入せず、ロジック層（Core）の自動テスト + 実機確認とする。
4. **外部AI（B）の再フレーミング**: "安い労働力で代替" は高リスク低リターン（検証コストが節約を上回る／主因は軽サブエージェントでなく長尺 Opus 本体）。"無料の追加レビュアー（AI on AI Review）" なら品質を上げつつ低リスク。B は後続だが方針はこれ。
5. **監査スキル（skill-auditor / docs-auditor）は Windows 実機で動く**: Python 導入済み、外部APIキー不要＝**追加課金なし**、ログは Windows 上に存在。要・小修正のみ（後述）。監査対象は「文書」ではなく**スキルのポートフォリオ全体**なので、規模は十分にある。

## 3. 変更項目

### グローバル（`~/.claude`、全プロジェクト共通）

**G1. UserPromptSubmit フック（新規）**
- `~/.claude/settings.json` に `UserPromptSubmit` フックを追加し、PowerShell スクリプト（例: `~/.claude/hooks/global-prompt-reminder.ps1`）で `hookSpecificOutput.additionalContext` を含む JSON を返し、毎ターン**超短文**を注入する。
- 注入文（案・1〜2 行に収める）:
  > 開発作業中は superpowers ワークフローを継続（サブエージェント使用は確認不要・既定動作）。質問・提案は AskUserQuestion で。
- 根拠: 判断系の行動はフックで 100% 機械強制できないが、毎ターン注入が「途中で忘れる」症状に直接効く唯一の仕組み。判断 2 の通り軽量に保つ。

**G2. グローバル CLAUDE.md 追記（数行）**
- ① superpowers が開発の本線（常時）。② プラグインの使い分け:
  - `frontend-design` = UI の見た目を作る/直すとき
  - `security-guidance` = セキュリティが絡むとき（APIキー・外部連携・権限・入力安全性。※特にフェーズ B で効く）
  - `feature-dev` = 深い調査・設計が要るとき、その専門サブエージェント（探索/設計/レビュー）を **superpowers 各フェーズ内の道具**として使う（superpowers と競合する別本線にはしない）
- 記事の「ルートは短く（〜50 行目安）」を守り、最小限に。

**G3. 権限設定の修正（`~/.claude/settings.json` permissions）**
- 判断 1 の対処。auto（自動承認）モードで **Agent（サブエージェント起動）ツールがブロックされない**よう許可を与える。
- 正確な許可エントリ（`permissions.allow` の表記等）は実装フェーズで確定する。`update-config` / `fewer-permission-prompts` スキルが該当領域。
- 安全性: Agent 起動自体は破壊的操作ではない。危険コマンド（`rm -rf /` 等）・`--no-verify`・`.env` 編集のブロックは既存フックで継続するため、安全網は維持される。

**G4. skill-auditor 導入（本家・グローバルスキル）**
- `nyosegawa/skills` の `skill-auditor` を `~/.claude/skills/` に配置。
- **Windows 対応の小修正 2 点（私が施す）**:
  - `scripts/collect_transcripts.py` のパス符号化 `abs_path.replace("/", "-")` が Windows のバックスラッシュ/コロンを変換せずプロジェクト名照合がズレる → `\` と `:` も変換対象に追加。
  - `SKILL.md` の Mac 用 `open`（HTML を開く）→ Windows の `start` に置換。
- `tiktoken` は任意（無ければ文字数推定）。外部 API キー不要・追加課金なし（分析は Claude のサブエージェント）。
- **動作確認まで含める**: 実際に 1 回 audit を回し、Windows のログを正しく読み HTML レポートが生成されるところまで確認する（ユーザーの「設定グダグダ」回避のため必須）。
- 価値: superpowers/3 プラグイン等の**発火精度・競合・Attention Budget**を後から定量監査できる。今回の改修が効いたかの検証基盤にもなる。

**G5. docs-auditor 導入（本家・グローバルスキル、プロジェクト単位で実行）**
- `nyosegawa/skills` の `docs-auditor` を `~/.claude/skills/` に配置。`--cwd` で対象プロジェクトを指定して実行する。
- skill-auditor と同種の Windows 小修正（`open`→`start` 等）を施し、動作確認まで。
- 価値: 対象プロジェクトの `docs/` `CLAUDE.md` `AGENTS.md` が実際に Claude の行動を改善しているか（ROI・未参照ドキュメント検出）を測る。

### プロジェクト（reloaded-helper）

**P1. pre-commit に `dotnet test` 追加**
- 既存 `.claude/hooks/pre-commit-guard.ps1` を拡張。現状は `dotnet build` チェックのみ → **ビルド成功後に `dotnet test` を実行し、失敗ならコミットをブロック**する（完了ゲート）。
- 記事は Stop Hook 推奨だが、毎ターン全テストが走るのは対話的デスクトップ開発に重い。コミット時という自然な区切りで走らせる（意図的な逸脱）。

**P2. AGENTS.md に原則 1 行**
- 「ミスが出たら、それを防ぐテスト/リンタールールを足す」を運用原則として明記（Week2-4 の「ミスのたびに強化」）。

### 既に達成済み（再構築しない）
- 最初の ADR（`docs/adr/0001-tech-stack.md`）
- 計画→承認→実行（superpowers ワークフロー）
- 編集時自動整形（`.claude/hooks/post-edit.ps1` の `dotnet format`）
- コミット前ビルドチェック・`--no-verify` 禁止（既存フック）
- 危険コマンド/`.env` 保護（グローバル既存フック）
- AGENTS.md(28 行)・CLAUDE.md(13 行)＝行数良好

## 4. やらないこと（YAGNI）
- WPF の UI 自動化ツール（Terminator 等）・Web 用 E2E（Playwright/agent-browser）
- Stop フックでの毎ターン `dotnet test`
- 新規 ADR（最初の ADR は作成済み）
- 外部AI連携（フェーズ B として後続）

## 5. 検証方法
各項目を実際に発火させて確認する:
- G1: 任意の発言で注入文がコンテキストに出るか
- G3: 開発タスクでサブエージェントが**確認を挟まず**起動するか
- G4/G5: audit を実走し、Windows ログ/文書を読んで HTML レポートが出るか
- P1: わざとテストを失敗させ、コミットがブロックされるか
- P2: AGENTS.md の記述確認

## 6. フェーズ B（外部AI連携）— 後続
- 役割は「安い労働力での代替」ではなく「**無料の追加レビュアー（AI on AI Review）**」に再フレーム。品質を上げる方向で、品質低下リスクを避ける。
- 外部AIは **CLI ではなく API**（アカウント作成→APIキー作成→連携）。このPCに現状 CLI/MCP 連携は無く、新規セットアップになる。キー・アカウントID はユーザーが控え済みで即提示可。
- 過去に連携・APIキー設定で大きく手間取った経緯あり → **着手時は最小ステップの手順を私が設計して提示**する（ユーザーがキーを貼るだけにする）。
- 「制限中（rate limit）の外部AIに対する誤診断防止」（A-5）は B の中で、呼び出し前の決定的なプリフライト確認として扱う。

## 7. リスクと留意点
- UserPromptSubmit 注入による微小なトークン増・希釈 → 超短文で緩和。後で skill-auditor の結果を見て調整。
- 権限緩和（Agent 許可）の安全性 → 破壊的でない操作に限定。既存の安全フックは維持。
- 監査スキルの Windows 修正は**動作確認を必須**とし、未検証のまま「動く」と言わない。
- `skipWorkflowUsageWarning` は schema 上 `@internal` 扱い（Task 5 で採用）。Claude Code のアップデートで仕様変更・削除されると「サブエージェントにしますか？」プロンプトが無音で再発する可能性がある。再発時のフォールバック手順: ① `~/.claude/settings.json` の当該キー存在を確認、② `update-config` スキルで再設定、③ それでも出るなら `autoMode.allow` への記法調整を再検討。
