# ADR-0001: C# / .NET 10 / WPF をメインスタックとして採用

**ステータス：** Accepted  
**日付：** 2026-06-21

## 背景

Reloaded-II（ゲーム mod ローダー）の Windows 補助ツールを開発するにあたり、技術スタックを選定した。

## 決定

- 言語：C#
- フレームワーク：.NET 10
- UI：WPF（Windows Presentation Foundation）

## 理由

- **C# / .NET 10**：Reloaded-II 本体と同じエコシステム。型安全・高性能・Windows ネイティブ API へのアクセスが容易。
- **WPF**：Windows 専用ツールであるため、クロスプラットフォームは不要。WPF は .NET 10 でサポートされ、XAML で宣言的 UI が書ける。
- **System.Text.Json のみ**：.NET 標準組み込みの JSON ライブラリ。外部依存を最小化し、単一実行ファイルとして配布可能にする。

## ランタイム NuGet 制約

テスト（xUnit）以外の追加 NuGet パッケージ禁止。理由：
- 単一実行ファイル（SelfContained）での配布を維持するため
- 依存関係の爆発的増加を防ぐため
- System.Text.Json は .NET 標準として実質的にゼロコスト

## データ保存先制約

ユーザーデータは `%APPDATA%\ReloadedHelper` のみ。理由：
- Windows 標準の慣例（アプリデータはユーザープロファイル以下）
- 管理者権限不要で動作させるため

## 却下した代替案

- **WinUI 3**：.NET 10 サポートが安定していないため
- **Avalonia**：クロスプラットフォームは不要なため
- **NuGet パッケージ追加**：依存管理の複雑化を避けるため禁止
