# rh-tools — プロジェクト指示書

## このプロジェクトとは
Reloaded-II（クローズドソース MODマネージャー）の Windows 補助ツール。
C# / .NET 10 / WPF。個人使用のみ。

## なぜ作るのか（背景）
Reloaded-II の 3 つの痛点を外部ツールで解決する:
1. MOD ロードオーダーの手動管理が困難（依存関係が複雑でクラッシュしやすい）
2. MOD 説明文の翻訳が MOD アップデートで毎回消える
3. MOD ページ URL 等メタデータが未入力で不便

## ロードマップ
| フェーズ | 内容 | 状態 |
|---|---|---|
| 1 | MOD 一覧ビューア（読み取り専用） | ✅ 完了 |
| 2 | UI 刷新（ダークテーマ・サイドナビ） | ✅ 完了 |
| 2.5 | UI バグ修正 6 件 | 次に実装 |
| 3 | 自動並び替え（トポロジカルソート + 自動アップデート） | 設計済み |
| 4 | GameBanana メタデータ取得 + 翻訳 | 将来 |

仕様書: docs/superpowers/specs/

## ビルド・テスト・発行
dotnet build reloaded-helper.slnx
dotnet test
dotnet publish src/ReloadedHelper.App -r win-x64 -c Release /p:PublishSingleFile=true /p:SelfContained=true -o publish/

## 制約
- ランタイム NuGet 追加禁止（System.Text.Json のみ）。テスト用 xUnit は可。
- ユーザーデータ保存先: %APPDATA%\ReloadedHelper のみ。

## 構成
- src/ReloadedHelper.Core/         — ロジック層（WPF なし）
- src/ReloadedHelper.App/          — WPF UI 層
- tests/ReloadedHelper.Core.Tests/ — xUnit テスト

## 重要ドキュメント
- Phase 2 UI 仕様: docs/superpowers/specs/2026-06-20-ui-redesign-design.md
- Phase 1 実装記録: docs/superpowers/plans/2026-06-20-foundation-viewer.md

## 作業ルール
- 各フェーズ開始時はプランモードで設計確認してから実装する。
- サブエージェント使用時は .superpowers/sdd/progress.md で進捗管理。
- .superpowers/ は gitignore 済み（コミット不要）。
