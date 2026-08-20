---
trigger: model_decision
description: MudBlazor and Blazor UI implementation rules
---

---
trigger: always_on
globs: ["/*.cs", "/*.razor"]
Blazor Architecture Constraints
Activation: このルールは、C# クラスおよび Blazor コンポーネントを扱う際に常に適用されます。
---

# Blazor / MudBlazor Implementation Rules

## Primary objectives

- Prefer MudBlazor built-in component behavior over manually recreated markup.
- インラインの Style 属性を完全に排除し、MudBlazorが提供するネイティブのコンポーネントプロパティに依存。
- Preserve existing business behavior, validation rules, accessibility, and user-visible text.
- Do not perform broad refactoring when a local component replacement is sufficient.
- IDE警告を消す目的だけでInspectionを抑制しない。
- まずコード削除、MudBlazor API、プロジェクトCSSで解決する。
- 抑制する場合は、対象Inspection、理由、影響範囲を記録する。

## Text input and character limits

- For a text field that displays a character count, prefer MudTextField's Counter parameter.
- For a text field that must reject input beyond a limit, use MaxLength in addition to Counter.
- Treat Counter as a display feature and MaxLength as the input restriction.
- Use Immediate="true" when the bound model value, validation, or other UI state must update on every keystroke.
- Prefer an explicit generic type for clarity:
  <MudTextField T="string" ... />

## Character-count migration

When the code contains a manually rendered character counter such as:

    @($"{value?.Length ?? 0} / {limit}")

or:

    <MudText Typo="Typo.caption" Color="Color.Secondary">
        @(...)
    </MudText>

perform the following analysis before editing:

1. Identify the input component to which the counter belongs.
2. Determine the authoritative maximum length.
3. Replace the manual counter with Counter="N".
4. Add MaxLength="N" if input must be prevented from exceeding N characters.
5. Add Immediate="true" if the bound value or validation must update per keystroke.
6. Remove obsolete manual markup and unused CSS.
7. Preserve HelperText, Validation, Label, Variant, Lines, and For parameters unless there is a documented reason to change them.
8. Check whether the same maximum is enforced by the model or server-side validation.

## Example

Prefer:

    <MudTextField T="string"
                  @bind-Value="_newItem.Content"
                  Label="コンテンツ"
                  Variant="Variant.Outlined"
                  Lines="5"
                  MaxLength="1000"
                  Counter="1000"
                  Immediate="true" />

Do not retain a separate manually rendered character counter unless:

- the counter format differs materially from MudBlazor's format;
- the count is based on a domain-specific metric rather than string length;
- the counter must be displayed outside the input component;
- the component library cannot provide the required behavior.

## CSS and theme variables

- Do not copy MudBlazor theme variables into local inline styles merely to reproduce built-in component styling.
- Do not suppress Rider's unresolved custom property inspection before confirming whether the CSS variable is actually defined.
- Prefer MudBlazor Color, Typo, Variant, Class, and Style parameters where they express the requirement.
- If a custom CSS variable is genuinely required, define it in a project stylesheet and document its scope and fallback.
- Use a fallback in CSS when appropriate, for example:
  color: var(--my-app-secondary-text, #666);

## Validation and verification

After a change:

1. Run dotnet build.
2. Run the relevant tests.
3. Confirm the counter changes during typing.
4. Confirm input cannot exceed MaxLength.
5. Confirm paste behavior.
6. Confirm validation and submit behavior.
7. Inspect the rendered DOM in the browser.
8. Check the UI in both normal and validation-error states.
9. Report any remaining Rider warning separately from compiler or runtime errors.

## Change discipline

- Make the smallest safe change.
- Do not change package versions without evidence.
- Do not replace a custom implementation solely to remove an IDE warning if it changes behavior.
- If the requirement is ambiguous, explain the ambiguity and present a minimal patch and an alternative.


設計評価プロセス (Chain of Thought Enforcement)
コードの修正や追加を行う前に、必ず自身の内部推論（CoT）プロセスにおいて、対象となるコンポーネントやクラスの現在のCBOとLCOM4を概算してください。

Blazor固有の制約: @code ブロック内にデータフェッチ（HttpClientの直接利用など）や複雑な状態管理ロジックを記述しないでください。これは「神コンポーネント（God Component）」を生み出し、CBOとRFCを悪化させます。

機能を追加する際は、ビジネスロジックを独立したC#のサービスクラス（またはStateContainer）に抽出し、インターフェースを介してコンポーネントにDI（依存性の注入）を行ってください。

新しいサービスクラスを作成する際は、そのクラスの LCOM4 が 1（すべてのメソッドとフィールドが関連している状態）を維持できる単一責任となっているかを確認してください。