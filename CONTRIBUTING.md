# Contributing

Thanks for taking an interest in Helyx.

## Before you open a pull request

Open an issue first and describe what you want to change. Helyx is a personal
project with a settled design, and a change to how a screen works is much
easier to discuss before it is written than after.

## Code style

Pull requests are expected to match the style of the surrounding file:

- Keep the logic inline in the method where it runs. Helper functions are for
  blocks that are genuinely reused across a screen, not for structuring one
  algorithm.
- No explanatory comments on ordinary code.
- Prefer switch expressions, ternaries, LINQ chains and collection
  expressions. Allman braces.
- Every user-visible string belongs in `Strings.resx` and all eight
  translations.
- Files use CRLF.

## Building

```bash
dotnet build Helyx/Helyx.csproj
```
