---
name: modernize-csharp
description: Refactor C# code to use modern language features and LINQ.
---
* Analyze the provided C# files and identify areas where legacy imperative code can be modernized.
* Specifically, look for `for` and `foreach` loops that can be safely converted to LINQ expressions to improve declarative readability.
* Apply modern C# features such as pattern matching, switch expressions, and target-typed `new()`.
* Fix any Roslynator or IDE analyzer warnings present in the code.
* Ensure the refactored code remains highly readable and logically identical to the original.
