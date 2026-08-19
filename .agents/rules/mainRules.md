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

## 2. 全体的なコーディングの原則 (General Coding Principles)
- **C#15の新機能を使用**: パターンマッチング、レコード型 (Records)、Primary Constructors , Union type などを積極的に使用すること。
- **関心事の分離 (SoC)**: UIコンポーネント (`.razor`) に複雑なビジネスロジックを書かない。ロジックは Service クラスに分離し、DI (Dependency Injection) で注入すること。
- **非同期プログラミング**: I/Oバウンドな操作（DBアクセス、Gemini API呼び出し）はすべて `async/await` を使用し、同期的なブロック (`.Result` や `.Wait()`) は絶対に避けること。

## if文撲滅
システムの取り得る状態を直和型（Sum Type）として厳密に定義し、UIの振る舞いの決定を手続き的な「条件分岐（if , else,三項演算子)」ではなく、コンパイラによって保証された「網羅的パターンマッチング（Exhaustive Pattern Matching）」による安全な型ダウンキャストに委譲する
そのために unionを積極的に利用する

## 再代入撲滅
イミュータブルなデータ構造や純粋関数　宣言的および関数型パラダイム　を優先する

## 3. Blazor Server (InteractiveServer) のル－ル
- **SignalRの意識**: Blazor ServerはSignalR接続上で動作するため、メモリリークに注意する。イベントハンドラの購読解除 (`IDisposable` の実装) を徹底すること。
- **UIの更新**: 非同期処理（Gemini APIのストリーミング応答など）のコールバック内でUIを更新する場合は、必ず `InvokeAsync(StateHasChanged)` を呼び出すこと。
- **ライフサイクルの競合防止 (Race Condition Prevention)**: 
    - `OnInitializedAsync` 内で `await` を伴うデータ取得を行うと、完了前に `OnAfterRenderAsync(firstRender: true)` が発火する仕様に注意すること。
    - 取得したデータに依存するJSInterop（初期化処理など）を `OnAfterRenderAsync` で呼ぶ場合は、必ず `_dataLoaded` のようなフラグ変数を設け、データ取得完了後にのみ実行されるように制御すること（空データでのJS初期化を防ぐため）。
- **DIのライフサイクル**:
    - `Singleton`: アプリ全体で共有するステートレスなサービス（Gemini APIのクライアントファクトリなど）。
    - `Scoped`: ユーザー（SignalRサーキット）ごとに保持する状態（チャット履歴、ユーザー固有の設定など）。
    - `Transient`: 軽量で状態を持たない処理。
- **JavaScriptの最小化**: DOM操作は極力Blazor/MudBlazorの機能で完結させ、JSInterop (`IJSRuntime`) の使用は既存のJSライブラリとの連携など最小限に留めること。

## 4. MudBlazor (v9.7) のUI設計ルール
- **コンポーネントファースト**: 標準のHTMLタグ (`<div>`, `<span>`, `<input>`) や生のCSSクラスの代わりに、必ずMudBlazorのコンポーネントを使用すること。
    - レイアウト: `<MudGrid>`, `<MudItem>`, `<MudStack>`
    - フォーム: `<MudForm>`, `<MudTextField>`, `<MudSelect>`
    - タイポグラフィ: `<MudText>`
- **テーマとスタイリング**: 色やサイズはハードコードせず、MudBlazorのテーマカラー (`Color.Primary`, `Color.Secondary`) やタイポグラフィのプロパティを使用すること。
- **アイコン**: `<MudIcon Icon="@Icons.Material.Filled.XXX" />` のように、MudBlazor内蔵のMaterial Iconsを使用すること。

## 5. 認証・認可 (Authentication: Individual)
- **UIの出し分け**: ログイン状態に基づくUIの切り替えには `<AuthorizeView>` を使用すること。
- **ページ保護**: 認証が必要なページには `@attribute [Authorize]` を付与すること。
- **ユーザー情報の取得**: コードビハインドやサービス内でユーザー情報が必要な場合は、`AuthenticationStateProvider` を注入して `GetAuthenticationStateAsync()` から Claims を取得すること。

## 6. Gemini API 連携に関するルール
- **APIキーの管理**: APIキーは絶対にコードにハードコードしないこと。`IConfiguration` 経由で取得し、開発環境では User Secrets、本番環境では環境変数を使用する設計にすること。
- **ストリーミング応答の活用**: LLMのUX向上のため、可能な限り `IAsyncEnumerable` を使用したストリーミングレスポンスを実装し、BlazorのUIをリアルタイムに更新（タイピングエフェクト）すること。
- **サービスクラスの分離**: Gemini APIを呼び出すロジックは `IGeminiService` のようなインターフェースと実装クラスに隠蔽し、UIコンポーネントから直接 HttpClient を叩かないこと。

## 7. コード生成のフォーマット要求 (Output Formatting)
- コードを提供する際は、ファイル名とパスを明記すること (例: `/// Services/GeminiChatService.cs`)。
- 修正箇所だけでなく、コンテキストが分かるように必要な using 句や DI の登録 (`Program.cs`) も併せて提示すること。
- コメントは日本語で、なぜその設計にしたかの意図を簡潔に記載すること。

## テスト方針
- 新しい実装をしたら　E2ETests と Tests を実行しパスするか確かめる
- 新しい実装をしたらテストを実装する。