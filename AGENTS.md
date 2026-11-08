# Notes for coding agents

## Building
- Windows desktop app (WPF, net8.0-windows): build the solution with Visual Studio 2022 or `dotnet build TexasHoldem.sln`.
- Android app (.NET MAUI, net9.0-android): `dotnet build UI/TexasHoldem.UI.Maui -f net9.0-android`. Needs the .NET 9 SDK with the `android` workload and an Android SDK with platform 35.
- Tests: `dotnet test Tests/TexasHoldem.Logic.Tests`.
- If a build fails because a workload or SDK is missing, stop and report it. Do not change target frameworks or remove projects to get past it.

## Conventions
- Line endings follow `.gitattributes` (CRLF for .NET source and project files). Keep `.editorconfig` aligned and run `git add --renormalize .` if the policy changes.
- StyleCop rules live in `stylecop.json`, `Rules.ruleset` and `Settings.StyleCop`.
- Prefer small, deterministic edits over broad refactors. Keep the WPF app building when changing shared projects.
- The game engine under `TexasHoldem.Logic` and the AI players derive from the MIT-licensed TexasHoldemGameEngine; see `THIRD_PARTY_NOTICES.txt`.
