@AGENTS.md

## Claude Code 固有の設定

### 作業ルール
- 各フェーズ開始時はプランモードで設計確認してから実装する。
- サブエージェント使用時は .superpowers/sdd/progress.md で進捗管理（サブエージェント使用時のみ参照）。
- .superpowers/ は gitignore 済み（コミット不要）。

### ドキュメント
- アーキテクチャの決定記録：docs/adr/ を参照

### フェーズ状態

| フェーズ | 内容 | 状態 |
|---|---|---|
| 1 | MOD 一覧ビューア | ✅ 完了 |
| 2 | UI 刷新 | ✅ 完了 |
| 2.5 | UI バグ修正 6 件 | 次に実装 |
| 3 | 自動並び替え | 設計済み |
| 4 | GameBanana 連携 | 将来 |

仕様書：docs/superpowers/specs/
