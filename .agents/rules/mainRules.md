---
trigger: always_on
---

# Project Context & AI Prompt Rules

## 1. プロジェクト概要 (Project Overview)
- **Framework**: .NET (11.0), ASP.NET Core Blazor Web App
- **UI Library**: MudBlazor (v9.7)
- **Interactivity**: Blazor Server (InteractiveServer モード)
- **Authentication**: Individual Accounts (ASP.NET Core Identity)
- **AI Integration**: Gemini API
- **C# 15

follow [best practice](<skills/dotnet-best-practices.md>) 
[design-pattern-review](skills/dotnet-design-pattern-review.md)

## 7. コード生成のフォーマット要求 (Output Formatting)
- コードを提供する際は、ファイル名とパスを明記すること (例: `/// Services/GeminiChatService.cs`)。
- 修正箇所だけでなく、コンテキストが分かるように必要な using 句や DI の登録 (`Program.cs`) も併せて提示すること。
- コメントは日本語で、なぜその設計にしたかの意図を簡潔に記載すること。

## テスト方針
- 新しい実装をしたら　E2ETests と Tests を実行しパスするか確かめる
- 新しい実装をしたらテストを実装する。