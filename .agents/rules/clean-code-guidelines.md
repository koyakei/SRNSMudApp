* You must strictly follow the coding styles and rules defined in `.editorconfig`.
* Before finalizing any code changes, ensure there are no unused `using` directives (IDE0005) and remove them if present.
* Automatically fix all minor compiler warnings and style issues in the files you modify (e.g., unused variables, unnecessary casts).
* Do not introduce new warnings.
* When running `dotnet format`, always pass `--diagnostics IDE0055`. Full runs break the repo's using-alias pattern — see `dotnet-format-policy.md` for details.
