# Rules

## C# ReSharper Compatibility
- Always write C# code that is compliant with JetBrains ReSharper coding standards and inspections.
- Adhere to common ReSharper suggestions (e.g., using `var` where appropriate, removing unused directives, using expression-bodied members where suitable, proper naming conventions, avoiding multiple enumerations of `IEnumerable`, etc.).
- Ensure there are no warnings or suggestions that ReSharper would typically flag.

## Workspace Hygiene
- **Temporary Scripts**: When creating throwaway scripts (such as Python scripts for refactoring or batch edits), do not leave them in the project workspace. You MUST either delete them immediately after execution or create them in the system's `scratch/` directory.
