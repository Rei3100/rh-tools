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

## アーキテクチャの決定記録

重要な設計決定は docs/adr/ を参照すること：
- docs/adr/0001-tech-stack.md — 技術スタック・制約の選択理由
