# Zest Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the product from "Lime for Windows" (code name `LimeWin`) to **Zest** everywhere users or developers see it, and move an existing installation's data and sign-in entry to the new names on first launch.

**Architecture:** A mechanical rename of folders, projects, namespaces and assembly names first (no behavior change, proven by an identical test run), then user-visible strings, then a small upgrade step (`LegacyMigration` in Core plus two App-side checks) that runs at startup.

**Tech Stack:** .NET 10, WPF + WPF-UI, xUnit v3. No new packages.

**Spec:** User decisions in the 2026-10-24 session: the software is named Zest; "folders and other names all from lime to zest"; the original Lime attribution must stay (MIT). This plan is the first of four (rename → design → metadata → README).

## Global Constraints

- New names: solution `Zest.slnx`; projects and folders `src/Zest.App`, `src/Zest.Core`, `tests/Zest.Core.Tests`; namespaces `Zest.App.*`, `Zest.Core.*`, `Zest.Core.Tests`; the app's output is `Zest.exe` (`<AssemblyName>Zest</AssemblyName>`, `<RootNamespace>Zest.App</RootNamespace>` in `Zest.App.csproj`).
- Type names that carry the product name: `LimeTool` → `ZestTool`, `LimeException` → `ZestException` (file `ZestException.cs`). Any other `Lime*` type found by `git grep -n "\bLime[A-Z]\w*"` follows the same rule.
- Runtime names: data folder `%LOCALAPPDATA%\Zest`, Office temp root `%TEMP%\Zest`, Run value `HKCU\...\Run\Zest`, mutex `Local\Zest.SingleInstance`, user agent `Zest/1.0`, thread names `Zest …`, window titles `Zest …`.
- Korean particles follow the new final sound (제스트 ends in a vowel): `Lime은→Zest는`, `Lime을→Zest를`, `Lime이→Zest가`, `Lime과→Zest와`, `Lime으로→Zest로`.
- Keep unchanged: every mention of the original macOS app (NOTICE's "Original project: Lime" section and its MIT text, comments that compare with "the original"), the lowercase color word "lime" (it stays the actions color in the Z3 design), and the dated historical docs `docs/superpowers/specs/2026-10-22-*`, `docs/superpowers/plans/2026-10-22-*`, `docs/test-results.md`.
- Baseline to match after Task 1: `dotnet test --project tests/Zest.Core.Tests` → 791 total, 779 passed, 0 failed, 12 skipped (docs/test-results.md, same machine).
- The repository folder `lime-win` is the running session's working directory and is NOT renamed by this plan; the user renames it after the session.

## Review Focus

- Upgrading while the old Lime is still running in the tray: the new app must not start a second set of hooks; it shows one message and exits (Task 3).
- The old data folder is partly locked (the old app has the 973 MB model open): migration must not throw or lose files; it moves what it can, leaves the rest, and the app keeps starting (Task 3 test `MoveDataFolder_LockedFile_LeavesItAndKeepsGoing`).
- Both `%LOCALAPPDATA%\LimeWin` and `%LOCALAPPDATA%\Zest` exist (e.g. a test run of the new build created `Zest` first): entries missing from `Zest` move over, entries already in `Zest` are never overwritten (Task 3 test `MoveDataFolder_BothExist_MovesOnlyMissingEntries`).
- A wrong pack URI after the assembly rename makes the embedded Pretendard font silently fall back to another face: Task 1 verifies with the render harness font log.
- The old Run value points at the old exe path: after migration only `Zest` remains, pointing at the current `Zest.exe`, and only if the user had sign-in launch turned on (Task 3 manual check 62).

---

### Task 1: Rename folders, projects, namespaces and assemblies

**Files:**
- Move (git mv): `src/LimeWin.App` → `src/Zest.App`, `src/LimeWin.Core` → `src/Zest.Core`, `tests/LimeWin.Core.Tests` → `tests/Zest.Core.Tests`, `LimeWin.slnx` → `Zest.slnx`, each `LimeWin.*.csproj` → `Zest.*.csproj`, `src/Zest.Core/LimeException.cs` → `src/Zest.Core/ZestException.cs`
- Modify: every `.cs`/`.xaml` under the moved folders (namespaces, `clr-namespace:`, `x:Class`, `pack://application:,,,/LimeWin.App;component/` → `/Zest;component/`), `Zest.slnx` paths, `Zest.Core.csproj` (`InternalsVisibleTo Include="Zest.Core.Tests"`), `Zest.Core.Tests.csproj` (ProjectReference path), `Zest.App.csproj` (ProjectReference path, `AssemblyName`, `RootNamespace`), `src/Zest.App/app.manifest` (`assemblyIdentity name="Zest"`), `build.ps1` (publish path `src/Zest.App`, final hint `dist\Zest.exe`), `README.md` (the three command lines only; the full rewrite is plan 4)

**Interfaces:**
- Produces: namespaces `Zest.App`, `Zest.Core`, `Zest.Core.Tests`; types `ZestTool`, `ZestException`; exe `dist\Zest.exe`; render CLI unchanged: `Zest.exe --render <folder>`.

- [ ] **Step 1: Move folders and files with `git mv`** (keeps history), then replace identifiers: `LimeWin` → `Zest` in all moved code and project files, `LimeTool` → `ZestTool`, `LimeException` → `ZestException`. Do not touch prose strings yet (Task 2).
- [ ] **Step 2: Build**
  Run: `dotnet build Zest.slnx -c Debug`
  Expected: 0 errors, 0 new warnings compared with `git stash`-free master build.
- [ ] **Step 3: Full test run**
  Run: `dotnet test --project tests/Zest.Core.Tests`
  Expected: 791 total, 779 passed, 0 failed, 12 skipped (the same 12 as docs/test-results.md).
- [ ] **Step 4: Publish and check embedded resources**
  Run: `./build.ps1` then `dist\Zest.exe --render "$env:TEMP\zest-render"` and read the font lines the harness logs.
  Expected: `dist\Zest.exe` exists; the log reports the Pretendard face resolved from the embedded resource (not a fallback); PNGs are written.
- [ ] **Step 5: Commit**
  `git commit -m "refactor: rename LimeWin projects, namespaces and assemblies to Zest"`

### Task 2: User-visible names and runtime identifiers

**Files:**
- Modify: `src/Zest.App/App.xaml.cs` (mutex name, `UnexpectedError`, startup failure MessageBox text and caption), `src/Zest.App/Tray/TrayIcon.cs` (tooltip, notification title), `src/Zest.App/Tray/LicenseWindow.xaml` (both titles), `src/Zest.App/Shelf/ShelfWindow.xaml` (title), `src/Zest.App/Wheel/OverlayWindow.xaml`, `src/Zest.App/Wheel/WheelController.cs`, `src/Zest.App/Jobs/ProgressCardHost.cs` (window titles), `src/Zest.App/Input/LowLevelHooks.cs`, `src/Zest.App/Shelf/ShellIcon.cs`, `src/Zest.Core/Office/OfficeWorker.cs` (thread names), `src/Zest.Core/Tools/ModelStore.cs` (folder `Zest`, user agent), `src/Zest.Core/Office/OfficeWorkFolder.cs` (temp root `Zest`), `src/Zest.App/Tray/LoginItem.cs` (`ValueName = "Zest"`, doc comment), `LICENSE` (`Copyright (c) 2026 Zest contributors`), `NOTICE.md` (first sentence: `Zest is a Windows port of the macOS app Lime (https://github.com/LumenJSN/Lime), used under the following MIT license.`; the rest unchanged), `build.ps1` comment (`Zest는 DirectML을 요청하지 않는다`), `docs/decisions.md` (append one dated entry, below)
- Test: `tests/Zest.Core.Tests/ModelStoreTests.cs`, `tests/Zest.Core.Tests/OfficeWorkFolderTests.cs` (update any expected path segment `LimeWin` → `Zest`), `tests/Zest.Core.Tests/TestFiles.cs` (temp root `%TEMP%\limewin-tests` → `%TEMP%\zest-tests`)

**Interfaces:**
- Consumes: Task 1 names.
- Produces: constants used by Task 3 — `ModelStore` default folder `<LocalAppData>\Zest\models`; `LoginItem.ValueName = "Zest"`; mutex `Local\Zest.SingleInstance`.

- [ ] **Step 1: Update the tests that pin path segments** so they expect `Zest` (`ModelStoreTests`, `OfficeWorkFolderTests`; search them for `LimeWin`).
- [ ] **Step 2: Run them to see them fail**
  Run: `dotnet test --project tests/Zest.Core.Tests --filter-class "Zest.Core.Tests.ModelStoreTests" --filter-class "Zest.Core.Tests.OfficeWorkFolderTests"`
  Expected: FAIL on the path segment (skip this step if neither test asserts the segment; note it in the commit message).
- [ ] **Step 3: Change the strings and identifiers listed under Files**, applying the particle rule exactly: `"예기치 않은 오류가 발생했지만 Zest는 계속 실행됩니다."`, `$"Zest를 시작하지 못했습니다: {ex.Message}"` with caption `"Zest"`, titles `"Zest 선반"`, `"Zest 라이선스"`, `"Zest Drop"`, `"Zest Wheel"`, `"Zest Progress"`, tooltip and notification title `"Zest"`.
- [ ] **Step 4: Append to `docs/decisions.md`**
  `- 2026-10-24 rename: the product is now Zest (code: Zest.App / Zest.Core, exe Zest.exe, data %LOCALAPPDATA%\Zest, Run value "Zest", mutex Local\Zest.SingleInstance). Mentions of the original macOS Lime and the lowercase color word "lime" are kept on purpose. Upgrades are migrated at startup (LegacyMigration).`
- [ ] **Step 5: Leftover scan**
  Run: `git grep -n "Lime" -- src tests build.ps1 LICENSE NOTICE.md`
  Expected: every remaining hit is either about the original macOS app (NOTICE section, "original" comments) or the legacy names Task 3 adds on purpose; no product-name use remains. Run `git grep -n "LimeWin"` — expected: only Task 3's legacy constants (none yet at this point).
- [ ] **Step 6: Tests pass**
  Run: `dotnet test --project tests/Zest.Core.Tests`
  Expected: 791 / 779 / 0 / 12.
- [ ] **Step 7: Commit** `git commit -m "feat: show the Zest name in the app, tray, data folder and sign-in entry"`

### Task 3: Upgrade an existing Lime installation

**Files:**
- Create: `src/Zest.Core/Migration/LegacyMigration.cs`
- Create: `tests/Zest.Core.Tests/LegacyMigrationTests.cs`
- Modify: `src/Zest.App/App.xaml.cs` (legacy instance check before the own mutex; `LegacyMigration.MoveDataFolder` and `LoginItem.MigrateLegacy` at the top of `Start()`), `src/Zest.App/Tray/LoginItem.cs` (`MigrateLegacy`), `docs/manual-checklist.md` (rows 62–64 below; also change `dist\LimeWin.App.exe` in the "최종 확인" intro to `dist\Zest.exe`)

**Interfaces:**
- Consumes: `ModelStore` folder from Task 2.
- Produces:
  - `public static class LegacyMigration` in `Zest.Core.Migration` with `public const string LegacyFolderName = "LimeWin"`, `public const string FolderName = "Zest"`, and `public static int MoveDataFolder(string localAppData)` — returns the number of top-level entries moved; never throws.
  - `LoginItem.MigrateLegacy()` — `public static void MigrateLegacy()`.
  - `App.LegacyInstanceName = @"Local\LimeWin.SingleInstance"`.

Rules for `MoveDataFolder` (the tests pin them): source `<localAppData>\LimeWin`, target `<localAppData>\Zest`. No source → 0. Target absent → `Directory.Move` of the whole folder (same volume), returns the number of top-level entries. Target present → move each top-level file or folder of the source whose name does not exist in the target; never overwrite. An entry that fails to move (IO or access error) stays where it is and the loop continues. If the source ends up empty, delete it.

- [ ] **Step 1: Write the failing tests** in `LegacyMigrationTests` (each uses a fresh temp folder from `TestFiles`):
  - `MoveDataFolder_NoLegacyFolder_ReturnsZero` → returns 0, creates nothing.
  - `MoveDataFolder_OnlyLegacy_MovesWholeFolder` → legacy `models\BiRefNet-matting-epoch_100.onnx` (a few bytes) ends at `Zest\models\…` with the same bytes; `LimeWin` no longer exists; returns 1.
  - `MoveDataFolder_BothExist_MovesOnlyMissingEntries` → legacy has `models\a.onnx` and `notes.txt` ("old"); target has `notes.txt` ("new"); after: `Zest\models\a.onnx` exists, `Zest\notes.txt` still reads "new", `LimeWin\notes.txt` still reads "old"; returns 1.
  - `MoveDataFolder_LockedFile_LeavesItAndKeepsGoing` → target exists; legacy has `models\` (with a file held open by a `FileStream` with `FileShare.None` during the call) and `other.txt`; does not throw; `Zest\other.txt` exists; the locked file is still at its legacy path; returns 1.
- [ ] **Step 2: Run them to see them fail**
  Run: `dotnet test --project tests/Zest.Core.Tests --filter-class "Zest.Core.Tests.LegacyMigrationTests"`
  Expected: FAIL to compile ("LegacyMigration does not exist").
- [ ] **Step 3: Implement `LegacyMigration.MoveDataFolder(string localAppData)`** per the rules above.
- [ ] **Step 4: Run the tests** — Expected: 4 passed.
- [ ] **Step 5: Wire it into the app**
  - `LoginItem.MigrateLegacy()`: open `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`; if a value named `LimeWin` exists, delete it and call `Set(true)`; otherwise do nothing. Wrap in try/catch for `SecurityException`/`UnauthorizedAccessException`/`IOException` and ignore (sign-in launch is a convenience).
  - `App.OnStartup`, after the `--render` branch and before creating the own mutex: `if (Mutex.TryOpenExisting(LegacyInstanceName, out var legacy)) { legacy.Dispose(); MessageBox.Show("이전 버전인 Lime이 실행 중입니다. 트레이에서 Lime을 종료한 뒤 Zest를 다시 실행하세요.", "Zest", MessageBoxButton.OK, MessageBoxImage.Information); Shutdown(); return; }`
  - First lines of `Start()`: `LegacyMigration.MoveDataFolder(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)); LoginItem.MigrateLegacy();`
- [ ] **Step 6: Add manual checks** to `docs/manual-checklist.md` under "최종 확인 (Task 17)" → a new table "### Zest 이름 변경 (2026-10-24)":
  - `| 62 | Lime에서 '윈도우 시작 시 실행'을 켜 둔 PC에 Zest 실행 | 레지스트리 Run에 LimeWin이 없어지고 Zest가 dist\Zest.exe를 가리킴. 트레이 메뉴의 체크가 켜져 있음 | |`
  - `| 63 | 배경 제거 모델을 받아 둔 PC에서 Zest로 배경 제거 | 다시 내려받지 않고 바로 처리. %LOCALAPPDATA%\Zest\models에 모델이 있고 LimeWin 폴더는 없음 | |`
  - `| 64 | Lime을 트레이에 띄운 채 Zest 실행 | "이전 버전인 Lime이 실행 중입니다…" 안내 후 Zest가 종료되고, 휠이 두 번 뜨지 않음 | |`
- [ ] **Step 7: Full test run** — `dotnet test --project tests/Zest.Core.Tests` → 795 total, 783 passed, 0 failed, 12 skipped.
- [ ] **Step 8: Commit** `git commit -m "feat: migrate an existing Lime install's data folder and sign-in entry to Zest"`
