# Rules

2. アーキテクチャの基本原則
本プロジェクトでは、Blazorコンポーネント（.razor）のUI責務と、背後のドメインロジック・データアクセス（.cs）を厳格に分離する。AIエージェントによるすべてのコード生成・修正は、以下の品質メトリクスを遵守しなければならない。

3. 絶対的メトリクスしきい値（Guardrails）
コードの設計においては、以下のトレードオフを常に監視・最適化すること：

凝集度の最大化 (LCOM4): 単一責任の原則を遵守せよ。LCOM4 >= 2（クラスが複数の独立したコンポーネントに分断されている状態）が検出された場合、直ちにクラスの分割を計画すること。

結合度の抑制 (CBO): LCOM4を下げるためにクラスを分割した場合でも、CBO > 10 は警告、CBO > 15 は原則として却下とする。

複雑性の管理 (RFC): Blazorコンポーネントの @code ブロックにビジネスロジックを直接実装してRFCを増大させることを固く禁じる。

4. 分割時のトレードオフ解決戦略
Blazor環境におけるCBOおよびRFCの急増を防ぐため、以下のパターンを積極的に適用すること：

依存性の注入（Dependency Injection）: コンポーネントは @inject を用いてサービスクラスを呼び出し、直接インスタンス化しないこと。

Facadeパターン: 複数のサービスに依存する複雑なコンポーネントには、処理をまとめるFacade層を設けること。

## Blazor コンポーネント設計規約 (単体テスト容易性)

1. ダイアログ起動: コンポーネントから MudBlazor の `IDialogService` を直接注入してはならない。必ず `SRNSMudApp.Services.Dialogs.IDialogLauncher` を注入し、`DialogLauncher.ShowAsync<TDialog>(title, parameters, options)` 経由で起動すること。テストでは `IDialogLauncher` をモックして起動時の型・タイトル・オプションを検証できる。
2. データアクセス: `.razor` / `.razor.cs` 内で `IDbContextFactory<ApplicationDbContext>` を直接使用してはならない。データ取得・更新は `Services/` 配下のインターフェース経由に分離すること。
3. ロジック配置: 純粋な計算・状態遷移は ViewModel クラス（例: `ItemCardViewModel`）または code-behind (.razor.cs partial class) に置き、bUnit を介さない xUnit 単体テストを可能にする。
4. 表示専用子コンポーネント: 大きなページコンポーネントは「状態保持コンテナ」と「表示専用子コンポーネント」に分割する。子コンポーネントは `[Parameter]` のデータ + `RenderFragment?` スロット（例: `BaseTagChip` の `PrefixContent` 等）のみを受け取り、サービス注入なしで bUnit レンダリングテストが可能な構成にする。型による表示切り替えが必要な場合は `DynamicComponent` と型マッピングを検討すること。
5. テスト共通セットアップ: bUnit テストでは `SRNSMudApp.Tests/TestSupport/BunitTestSetup` (`AddAuth()`, `AddInMemoryDbFactory()`, `AddSrnsDialogs()`, `CreateAuthState()`, `AuthHost`) を利用し、セットアップコードの重複を避けること。

詳細な実装ルールは `.agents/rules/` 配下の指定に従うこと。

## C# ReSharper Compatibility
- Always write C# code that is compliant with JetBrains ReSharper coding standards and inspections.
- Adhere to common ReSharper suggestions (e.g., using `var` where appropriate, removing unused directives, using expression-bodied members where suitable, proper naming conventions, avoiding multiple enumerations of `IEnumerable`, etc.).
- Ensure there are no warnings or suggestions that ReSharper would typically flag.

## Workspace Hygiene
- **Temporary Scripts**: When creating throwaway scripts (such as Python scripts for refactoring or batch edits), do not leave them in the project workspace. You MUST either delete them immediately after execution or create them in the system's `scratch/` directory.
