# 土台ハーネス強化 実装計画（フェーズ A+C）

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** superpowers 開発ワークフローを「仕組み」で安定させる補助輪（ハーネス）を、グローバルとプロジェクトの2層に最小・確実な形で追加する。

**Architecture:** 変更は2種類。①リポジトリ内（`.claude/hooks/`・AGENTS.md＝git管理下、コミット可）②グローバル `~/.claude`（このリポジトリ外＝コミットしない、適用のみ）。フック・権限・指示文・監査スキルを組み合わせ、判断系の行動は超軽量な毎ターン注入で補強する。

**Tech Stack:** PowerShell（フック）, .NET 10 / `dotnet test`（完了ゲート）, Python 3（監査スキル）, Claude Code Hooks（UserPromptSubmit / PreToolUse）, settings.json。

## Global Constraints

- ユーザーは非プログラマー。やり取り・コメントは日本語。専門用語には一言説明。
- ランタイム NuGet パッケージ追加禁止（`System.Text.Json` のみ可）。テスト用 xUnit は可。
- ユーザーデータは `%APPDATA%\ReloadedHelper` 以外に保存禁止。
- 外部有償サービス依存禁止。**課金は Claude のみ**（監査スキルは外部APIキー不要＝この制約を満たす）。
- CLAUDE.md / AGENTS.md は短く保つ（50〜60行目安）。
- `git commit --no-verify` 禁止（フックをスキップさせない。既存の禁止を維持）。
- グローバル変更（`~/.claude/...`）は**このリポジトリにコミットしない**。各タスクの「コミット」手順はリポジトリ内ファイルのみ対象。
- 設定/フックの一部はセッション再起動または次プロンプトで有効化される。検証はその点を考慮する。
- 破壊的でない変更のみ。既存の安全フック（危険コマンド・`.env`・`--no-verify` ブロック）は維持する。

---

### Task 1: pre-commit にテストゲートを追加（P1）

完了ゲート。コミット前に `dotnet test` を実行し、失敗ならブロックする。

**Files:**
- Modify: `.claude/hooks/pre-commit-guard.ps1`（リポジトリ内・コミット可）

**現状（参考）**: 末尾はビルド成功後 `Write-Host "ビルド成功。コミットを許可します。"; exit 0`。

- [ ] **Step 1: 一時的な失敗テストを追加して、ゲートが無いことを確認（RED の前提作り）**

`tests/ReloadedHelper.Core.Tests/_TempGate.cs` を作成:

```csharp
using Xunit;
namespace ReloadedHelper.Core.Tests;
public class _TempGate { [Fact] public void DeliberateFail() => Assert.True(false, "gate check"); }
```

Run: `dotnet test 2>&1 | tail -3`
Expected: 1 failed（失敗するテストが存在する状態になった）

- [ ] **Step 2: 現状の pre-commit で commit が通ってしまうことを確認（修正前の挙動）**

Run:
```bash
git add tests/ReloadedHelper.Core.Tests/_TempGate.cs
git commit -m "tmp: gate check (will revert)"
```
Expected: ビルドは通るため**コミットが成功してしまう**（テスト失敗を検出していない＝これが直す対象）。
直後に取り消し: `git reset --soft HEAD~1`

- [ ] **Step 3: pre-commit-guard.ps1 にテストチェックを追加**

`pre-commit-guard.ps1` の「ビルドチェック」ブロック（`if ($LASTEXITCODE -ne 0) { ... exit 2 }` のビルド版）の**直後**、最後の `Write-Host "ビルド成功。コミットを許可します。"` の**手前**に以下を挿入:

```powershell
# テストチェック
Write-Host "コミット前テスト実行中..."
$testOutput = dotnet test "reloaded-helper.slnx" 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "テスト失敗が検出されました。コミットをブロックします。`n$($testOutput | Out-String)"
    exit 2
}
Write-Host "テスト成功。"
```

- [ ] **Step 4: 失敗テストがある状態で commit がブロックされることを確認（GREEN: ゲートが効く）**

Run:
```bash
git add tests/ReloadedHelper.Core.Tests/_TempGate.cs
git commit -m "tmp: should be blocked"
```
Expected: `exit 2` で「テスト失敗が検出されました。コミットをブロックします。」と表示され、**コミットされない**。

- [ ] **Step 5: 一時テストを削除し、クリーンに戻す**

Run:
```bash
git reset
rm tests/ReloadedHelper.Core.Tests/_TempGate.cs
dotnet test 2>&1 | tail -2
```
Expected: 180 passed, 0 failed（元のクリーン状態）

- [ ] **Step 6: pre-commit-guard.ps1 の変更をコミット**

```bash
git add .claude/hooks/pre-commit-guard.ps1
git commit -m "feat: pre-commitフックにテストゲートを追加（完了条件）"
```
（このコミット自体でフックが走り、build+test 通過を確認できる）

---

### Task 2: AGENTS.md に運用原則を追記（P2）

**Files:**
- Modify: `AGENTS.md`（リポジトリ内・コミット可）

- [ ] **Step 1: 末尾に運用原則セクションを追記**

`AGENTS.md` の末尾（「## アーキテクチャの決定記録」セクションの後）に追加:

```markdown

## 運用原則

- ミスが出たら、それを防ぐテスト/リンタールールを足す（再発防止。一度足せば以降のセッションすべてに効く）。
```

- [ ] **Step 2: 行数が目安内か確認**

Run: `wc -l AGENTS.md`
Expected: 35行以下（60行目安の範囲内）

- [ ] **Step 3: コミット**

```bash
git add AGENTS.md
git commit -m "docs: AGENTS.mdに再発防止の運用原則を追記"
```

---

### Task 3: UserPromptSubmit フックで毎ターン注入（G1・グローバル）

**Files:**
- Create: `C:\Users\rainb\.claude\hooks\global-prompt-reminder.ps1`（グローバル・コミットしない）
- Modify: `C:\Users\rainb\.claude\settings.json`（グローバル・コミットしない）

- [ ] **Step 1: リマインダースクリプトを作成**

`C:\Users\rainb\.claude\hooks\global-prompt-reminder.ps1`:

```powershell
# UserPromptSubmit Hook: 毎ターン、超短文のリマインドをコンテキストに注入する
[System.Console]::In.ReadToEnd() | Out-Null  # stdin を消費（ハング防止）

$reminder = "開発作業中は superpowers ワークフローを継続（サブエージェント使用は確認不要・既定動作）。質問・提案は AskUserQuestion で。"
$out = @{ hookSpecificOutput = @{ hookEventName = "UserPromptSubmit"; additionalContext = $reminder } } | ConvertTo-Json -Compress
Write-Output $out
exit 0
```

- [ ] **Step 2: スクリプト単体で正しい JSON を出すか確認**

Run（Git Bash）:
```bash
echo '{}' | powershell -NoProfile -File "C:\\Users\\rainb\\.claude\\hooks\\global-prompt-reminder.ps1"
```
Expected: `{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"開発作業中は ..."}}` のような1行JSON。

- [ ] **Step 3: settings.json に UserPromptSubmit フックを登録**

`C:\Users\rainb\.claude\settings.json` の `"hooks"` オブジェクト内に、既存の `PreToolUse` と並べて追加:

```json
"UserPromptSubmit": [
  {
    "hooks": [
      {
        "type": "command",
        "command": "powershell -NoProfile -File \"C:\\Users\\rainb\\.claude\\hooks\\global-prompt-reminder.ps1\""
      }
    ]
  }
]
```

- [ ] **Step 4: JSON 妥当性を確認**

Run: `python -c "import json,io; json.load(open(r'C:\\Users\\rainb\\.claude\\settings.json', encoding='utf-8')); print('OK')"`
Expected: `OK`（settings.json が壊れていない）

- [ ] **Step 5: 反映確認（次プロンプト/次セッションで有効）**

注入は**次のユーザープロンプトから**有効になる。実装者は「このフックは次プロンプトで発火する」旨を報告すること。コミットは無し（グローバル設定）。

---

### Task 4: グローバル CLAUDE.md にワークフロー指針を追記（G2・グローバル）

**Files:**
- Modify: `C:\Users\rainb\.claude\CLAUDE.md`（グローバル・コミットしない）

- [ ] **Step 1: ワークフロー指針セクションを追記**

`C:\Users\rainb\.claude\CLAUDE.md` の末尾に追加:

```markdown

## 開発ワークフロー（本線の強制）
- 開発は superpowers ワークフローが本線。常にこれで進める。サブエージェント使用は既定動作（確認は不要）。
- プラグイン併用: UIの見た目を作る/直す→frontend-design ／ セキュリティが絡む(APIキー・外部連携・権限・入力安全性)→security-guidance ／ 深い調査・設計→feature-dev のサブエージェント(探索/設計/レビュー)を superpowers 各フェーズ内の道具として。
```

- [ ] **Step 2: 行数確認**

Run: `python -c "print(sum(1 for _ in open(r'C:\\Users\\rainb\\.claude\\CLAUDE.md', encoding='utf-8')))"`
Expected: 約25行（60行目安内）。コミットは無し（グローバル設定）。

---

### Task 5: サブエージェント起動をブロックしない権限設定（G3・グローバル）

ログで判明した根因（auto モードで Agent ツールが分類器にブロックされ「サブエージェント確認」が出る）への対処。

**Files:**
- Modify: `C:\Users\rainb\.claude\settings.json` の `permissions.allow`（グローバル・コミットしない）

- [ ] **Step 1: `update-config` スキルで許可エントリを追加（記法は委譲）**

権限ルールの正確な表記（サブエージェント起動ツールが `Agent` か `Task` か、`ToolName(...)` 形式かどうか）は環境依存で、手書きで推測しない。**`update-config` スキルを起動**し、「グローバル設定 `~/.claude/settings.json` の `permissions.allow` に、サブエージェント起動ツール（Agent/Task）を追加して、auto モードでブロックされないようにする」と指示する。このスキルが正しい記法・検証・既存 `deny` の保持を担保する。

調査の根拠（スキルに渡す文脈）:
- ログ `0886f889` セッションで `permissionMode:"auto"` 時に「Agent ツール」が自動モードの分類器にブロックされ、Claude が代替案を提示していた。対象は**サブエージェント起動ツール**。

- [ ] **Step 2: 既存 `deny` が保持されているか確認**

Run: `python -c "import json; d=json.load(open(r'C:\\Users\\rainb\\.claude\\settings.json', encoding='utf-8')); print('deny:', d.get('permissions',{}).get('deny')); print('allow:', d.get('permissions',{}).get('allow'))"`
Expected: 既存の `deny`（`.env` 等の Read 禁止群）がそのまま残り、`allow` にサブエージェント起動ツールが入っている。

- [ ] **Step 3: 反映確認（次セッション・auto モードで）**

検証は**新しいセッション**で行う必要がある（settings 再読込のため）。次セッションで auto モード中に開発タスクを与え、サブエージェントが**確認を挟まず**起動すれば成功。実装者はこの検証が次セッション持ち越しである旨を報告。
※ もし設定変更が権限でブロックされたら、その正確な操作をユーザーに提示して実行してもらう。コミットは無し（グローバル設定）。

---

### Task 6: skill-auditor を導入（本家）＋ Windows 対応 ＋ 動作確認（G4・グローバル）

**Files:**
- Create: `C:\Users\rainb\.claude\skills\skill-auditor\`（本家から配置・グローバル・コミットしない）
- Modify: `~/.claude/skills/skill-auditor/scripts/collect_transcripts.py`（Windows パス符号化の修正）
- Modify: `~/.claude/skills/skill-auditor/SKILL.md`（`open` → `start`）

- [ ] **Step 1: 本家リポジトリを取得して skill-auditor を配置**

Run（Git Bash）:
```bash
rm -rf /tmp/nyo-skills
git clone --depth 1 https://github.com/nyosegawa/skills.git /tmp/nyo-skills
mkdir -p "$HOME/.claude/skills"
cp -r /tmp/nyo-skills/skills/skill-auditor "$HOME/.claude/skills/"
ls "$HOME/.claude/skills/skill-auditor"
```
Expected: `SKILL.md agents schemas scripts assets references` が並ぶ。
※ clone がネットワーク/権限でブロックされたら、そのコマンドをユーザーに提示して実行してもらう。

- [ ] **Step 2: Windows パス符号化の修正（修正①）**

`scripts/collect_transcripts.py` の該当行を修正:
- 変更前: `encoded = abs_path.replace("/", "-").replace(".", "-")`
- 変更後: `encoded = abs_path.replace("\\", "-").replace("/", "-").replace(":", "-").replace(".", "-")`

（Windows の `C:\Users\...` 形式を Claude Code のプロジェクトフォルダ名 `C--Users-...` に正しく一致させるため）

- [ ] **Step 3: 修正①の検証（Windows ログを発見できるか＝最大リスクの確認）**

Run:
```bash
cd "$HOME/.claude/skills/skill-auditor"
python scripts/collect_transcripts.py --help 2>&1 | head -5 || python scripts/collect_transcripts.py 2>&1 | head -20
```
Expected: スクリプトが実行でき、`C--Users-rainb-src-reloaded-helper` 等のプロジェクト/セッションを検出した出力が出る（引数仕様はスクリプトに従う。`--help` か実行ログでセッション件数 > 0 を確認）。
※ もしプロジェクト検出が 0 件なら符号化がまだ合っていない。実際のフォルダ名 `C--Users-rainb-src-reloaded-helper` と符号化結果を突き合わせて修正を調整する。

- [ ] **Step 4: `open` → `start` の修正（修正②）**

`SKILL.md` 内の macOS 用 HTML オープンコマンドを Windows 用に置換:
```bash
cd "$HOME/.claude/skills/skill-auditor"
grep -n "open " SKILL.md
```
見つかった `open <report>.html` 系の行を、Windows の `start "" <report>.html`（または `cmd /c start <report>.html`）に置換する。
Expected: `grep -n "open " SKILL.md` 後の該当箇所が `start` ベースになっている。

- [ ] **Step 5: スモーク動作確認（任意でフル実行）**

最小確認として Step 3 のトランスクリプト収集が通れば「Windows でログを読める」ことは担保される。フル監査（`/skill-auditor` 起動 → 並列サブエージェント → HTMLレポート）は時間がかかる（10〜15分）ため、ユーザーの希望に応じて実施する。実装者は「収集スクリプトは Windows で動作確認済み。フル監査はオプション」と報告。コミットは無し（グローバル）。

---

### Task 7: docs-auditor を導入（本家）＋ Windows 対応 ＋ 動作確認（G5・グローバル）

**Files:**
- Create: `C:\Users\rainb\.claude\skills\docs-auditor\`（本家から配置・グローバル・コミットしない）
- Modify: `~/.claude/skills/docs-auditor/` 内の Windows 非互換箇所（`open`→`start`、パス符号化があれば同様に）

- [ ] **Step 1: docs-auditor を配置**

Run（Git Bash。Task 6 の clone が残っていれば再利用、無ければ再 clone）:
```bash
[ -d /tmp/nyo-skills ] || git clone --depth 1 https://github.com/nyosegawa/skills.git /tmp/nyo-skills
cp -r /tmp/nyo-skills/skills/docs-auditor "$HOME/.claude/skills/"
ls "$HOME/.claude/skills/docs-auditor"
```
Expected: `SKILL.md scripts` 等が並ぶ。

- [ ] **Step 2: Windows 非互換箇所を修正**

```bash
cd "$HOME/.claude/skills/docs-auditor"
grep -rn "open " SKILL.md
grep -rn 'replace("/", "-")' scripts/ || true
```
- `SKILL.md` の `open <report>.html` を `start "" <report>.html` に置換。
- scripts に `abs_path.replace("/", "-")` 相当のパス符号化があれば、Task 6 Step 2 と同じく `\` と `:` も変換対象に追加。
Expected: 該当箇所が Windows 対応済み。

- [ ] **Step 3: スモーク動作確認**

docs-auditor は対象プロジェクト単位（`--cwd`）で動く。reloaded-helper を対象に収集系スクリプトを実行し、`docs/` `CLAUDE.md` `AGENTS.md` を検出できることを確認する（スクリプトの引数仕様に従う）。
Run（例）:
```bash
cd "$HOME/.claude/skills/docs-auditor"
ls scripts/
# 収集スクリプトを reloaded-helper を対象に実行（--cwd / --project-root 等、SKILL.md の指定に従う）
```
Expected: 対象プロジェクトの文書群を検出した出力。フル監査はオプション。コミットは無し（グローバル）。

---

## 検証サマリ（全タスク後）

- Task1/2（リポジトリ）: worktree ブランチにコミット済み。`dotnet test` 180 passed を維持。
- Task3〜7（グローバル）: コミット無し。次セッション/次プロンプトで有効化される項目（G1注入・G3権限）は、その旨を報告し、次セッションで最終確認。
- 監査スキル（Task6/7）: 収集スクリプトが Windows ログ/文書を検出できることを確認済み（これが移植の最大リスクだった）。

## 完了後の流れ

- リポジトリ側コミット（Task1/2）は worktree ブランチ `worktree-harness-overhaul` 上。完了後 `superpowers:finishing-a-development-branch` でマージ/PR を判断。
- フェーズ B（外部AI連携）は本計画外・後続。設計書の §6 を参照。
