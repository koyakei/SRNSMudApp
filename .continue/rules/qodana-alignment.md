# Qodana Alignment Rules
# Source of truth: /qodana.yaml
#
# このルールは Rider Qodana (qodana.yaml) の設定と Antigravity IDE の問題検出を一致させるためのものです。
# qodana.yaml が更新された場合は、このファイルも同期して更新してください。

## 積極的に検出・修正すべき問題カテゴリ (qodana.yaml include)

以下のカテゴリに属する問題は、コード変更時に必ず検出・修正すること。

- **Security** (`category:Security`): セキュリティ上の脆弱性
- **Probable bugs** (`category:Probable bugs`): バグになりやすいコードパターン
- **Performance** (`category:Performance`): パフォーマンス上の問題
- **Maintainability** (`category:Maintainability`): 保守性を下げるコード

## 検出・修正してはいけない問題 (qodana.yaml exclude)

以下の Inspection ID は **qodana.yaml で明示的に除外** されています。
コード生成・修正時にこれらの問題を指摘したり、修正を行ったりしないこと。
既存コードがこれらのパターンを含んでいても、変更しないこと。

| Inspection ID | 理由・備考 |
|---|---|
| `JSUnusedGlobalSymbols` | Blazor JSInterop から呼ばれる関数は静的解析では未使用に見える |
| `Html.PathError` | Blazor コンポーネントのルーティング由来の誤検知 |
| `ConditionalAccessQualifierIsNonNullableAccordingToAPIContract` | Blazor/EF コンテキストでの false positive |
| `ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract` | Blazor/EF コンテキストでの false positive |
| `NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract` | Blazor/EF コンテキストでの false positive |
| `InconsistentNaming` | プロジェクト独自の命名規則を使用 |
| `AutoPropertyCanBeMadeGetOnly.Global` | シリアライズ/EF のために setter が必要 |
| `PropertyCanBeMadeInitOnly.Global` | シリアライズ/EF のために setter が必要 |
| `UnusedAutoPropertyAccessor.Global` | フレームワーク起動のメンバーは未使用に見える |
| `CollectionNeverQueried.Global` | フレームワーク起動のメンバーは未使用に見える |
| `UnusedMember.Local` | フレームワーク起動のメンバーは未使用に見える |
| `UnusedMember.Global` | フレームワーク起動のメンバーは未使用に見える |
| `UnusedParameter.Local` | フレームワーク起動のメンバーは未使用に見える |
| `CSharpWarnings::CS8602` | Nullable 参照型の誤検知（Blazor コンテキスト） |
| `CSharpWarnings::CS8604` | Nullable 参照型の誤検知（Blazor コンテキスト） |
| `PreferConcreteValueOverDefault` | 意図的な設計 |
| `MergeIntoPattern` | 可読性を考慮して許容 |
| `ArrangeObjectCreationWhenTypeEvident` | 意図的なスタイル |
| `CanSimplifySetAddingWithSingleCall` | 意図的なスタイル |
| `MergeConditionalExpression` | 可読性を考慮して許容 |
| `AutoPropertyCanBeMadeGetOnly.Local` | シリアライズ/EF のために setter が必要 |
| `UseAwaitUsing` | 意図的な設計 |
| `NotAccessedPositionalProperty.Local` | フレームワーク起動のメンバー |
| `Html.IdNotResolved` | Blazor の動的 ID 生成による誤検知 |
| `ClassNeverInstantiated.Local` | フレームワーク起動のクラス |
| `UnusedAutoPropertyAccessor.Local` | フレームワーク起動のメンバー |
| `RedundantTypeDeclarationBody` | 意図的なスタイル |
| `RedundantSuppressNullableWarningExpression` | 意図的な null チェック |
| `ParameterHidesMember` | 意図的なスタイル |
| `JoinDeclarationAndInitializer` | 可読性を考慮して許容 |
| `CollectionNeverUpdated.Local` | フレームワーク起動のメンバー |
| `AsyncMethodWithoutAwait` | 意図的な設計（インターフェース実装など） |

## 適用優先順位

1. `qodana.yaml` の `exclude` リストに載っている問題は**絶対に修正しない**
2. `qodana.yaml` の `include` カテゴリの問題は**必ず修正する**
3. `.editorconfig` に定義されたスタイルルールは上記に従った上で適用する
4. `qodana.yaml` に記載のない問題は、`qodana.recommended` プロファイルのデフォルト動作に従う

## 同期メモ

- 対応する qodana.yaml のパス: `/Users/keisukekoyanagi/IdeaProjects/SRNSMudApp/qodana.yaml`
- 最終同期日: 2026-08-12
