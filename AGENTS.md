# AGENTS.md — reloaded-helper

Reloaded-II Windows 補助ツール。C# / .NET 10 / WPF。

## ビルド・テスト・発行

```
dotnet build reloaded-helper.slnx
dotnet test
dotnet publish src/ReloadedHelper.App -r win-x64 -c Release /p:PublishSingleFile=true /p:SelfContained=true -o publish/
```

## 禁止事項

- ランタイム NuGet パッケージの追加禁止（System.Text.Json のみ可）
- テスト用 xUnit は可
- ユーザーデータは %APPDATA%\ReloadedHelper 以外の場所に保存禁止

## 構成

- src/ReloadedHelper.Core/         — ロジック層（WPF なし）
- src/ReloadedHelper.App/          — WPF UI 層
- tests/ReloadedHelper.Core.Tests/ — xUnit テスト

## アーキテクチャの決定記録

重要な設計決定は docs/adr/ を参照すること：
- docs/adr/0001-tech-stack.md — 技術スタック・制約の選択理由

## 運用原則

- ミスが出たら、それを防ぐテスト/リンタールールを足す（再発防止。一度足せば以降のセッションすべてに効く）。
- コミット前ビルド・テストゲート（`.claude/hooks/pre-commit-guard.ps1`）は Claude Code 経由のコミット時のみ発火する（settings.json の PreToolUse フック経由）。通常のターミナルからの `git commit` には適用されない。
