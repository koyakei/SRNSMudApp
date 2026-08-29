# Rules
follow [best practice](<skills/dotnet-best-practices.md>) [design-pattern-review](skills/dotnet-design-pattern-review.md)
## Workspace Hygiene
- **Temporary Scripts**: When creating throwaway scripts (such as Python scripts for refactoring or batch edits), do not leave them in the project workspace. You MUST either delete them immediately after execution or create them in the system's `scratch/` directory.
