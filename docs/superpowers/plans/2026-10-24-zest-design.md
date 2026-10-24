# Zest Design (Z3 Citrus) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Apply the approved "Z3 · 시트러스 3색" look: a wheel of floating petal buttons with no disc, colored by the wheel that opened it (Shift = lemon, Alt = tangerine, Ctrl = lime), and popups restyled after the approved "Zest 팝업" page (light = airy sheets, dark = warm ink).

**Architecture:** One color source in Core (`ZestColors`, plain ARGB values, contrast-tested), applied in App as WPF brushes that also override WPF-UI's theme keys, so existing WPF-UI controls pick up the palette without being rewritten. A borderless `ZestWindow` base gives every tool window the new frame (14 px corners, centered title, round close, footer bar). The wheel drops the screen capture and draws petals as separate shadowed shapes.

**Tech Stack:** .NET 10 WPF, WPF-UI 4.3.0, xUnit v3. No new packages.

**Spec:**
- Wheel: Canvas artifact https://claude.ai/artifact/LEk6RRyYPppHG2bXKFyfyo, boards `Zest-Citrus.dc.html` and `Zest-Citrus-Wheels.dc.html` (approved by the user: "디자인 자체는 Z3 시트러스 3색으로 진행").
- Popups: https://claude.ai/artifact/9WYqLs4XVipoar4BjXQFKX (approved: "나머지 디자인은 괜찮은 것 같다. 이대로 진행해줘"), including its "구현용 값" table and the rule "buttons all right-aligned in the window's bottom bar, primary button rightmost".
- Runs after `2026-10-24-zest-rename.md` (all names below are the Zest names).

## Global Constraints

- Theme follows Windows `AppsUseLightTheme` (0 = dark) for the wheel and every popup, and switches live on `SystemEvents.UserPreferenceChanged` like today.
- Light / dark tokens (exact): window `#FBFBF9` / `#1D1B16`; raised `#FFFFFF` / `#27251F`; sunken `#F2F2ED` / `#18170F`; line `#171D1B16` / `#14FFFFFF`; line-strong `#2E1D1B16` / `#2EFFFFFF`; seg `#0F1D1B16` / `#12FFFFFF`; text `#1D1B16` / `#F1EEE6`; text-2 `#5F5B52` / `#ABA79C`; text-3 `#8A867C` / `#7F7B71`; danger `#B42D1A` / `#FF8A7A`.
- Citrus (fill, text on fill, ink in light theme; ink in dark = fill): lemon `#FFD43B`, `#241C00`, `#A67C00`; tangerine `#FF8A3D`, `#2A1204`, `#A84300`; lime `#9BE15D`, `#162A05`, `#3B7A0E`. Wash (tinted background) = fill at 13–14 % alpha in dark, light `#FFF5CC` / `#FFEBDD` / `#E8F6D8`.
- Mode → citrus: `WheelMode.Convert` → lemon, `Tools` → tangerine, `Actions` → lime. Tool windows use tangerine, progress cards use the citrus of the job's kind, the shelf uses lime.
- Radius: window 14, card 12, button/field 8, chip fully round. Popup motion: enter 200 ms `cubic-bezier(0.23,1,0.32,1)` opacity + scale 0.96→1 (cards: 8 px rise, already built), exit 150 ms; with `Motion.ReduceMotion` opacity only. The wheel itself never animates open (keyboard-triggered).
- Buttons: every window's buttons sit in its bottom bar, right-aligned, primary rightmost; the left side of the bar holds only hint text, an error line or a checkbox.
- Fonts stay as shipped: Pretendard SemiBold (embedded) for Korean, Segoe UI Variable Display/Text for Latin and numbers with `Typography.NumeralAlignment="Tabular"`. (The popup page showed Manrope; embedding a new font is out of scope — my call, flagged to the user.)
- Contrast: text and text-2 on window ≥ 4.5:1, text on each citrus fill ≥ 4.5:1, each light-theme ink on `#FFFFFF` ≥ 3:1 — pinned by tests in Task 1.

## Review Focus

- The wheel over a busy or same-colored background (white page under white light petals, near-black page under dark petals): every petal must stay distinct by its edge line and shadow (Task 2 renders `-on-white` / `-on-black` sets and the manual check 65).
- Releasing in the 8 px gutter: it must pick the nearest petal, never cancel; the hub hole and outside still cancel (Task 2 tests).
- Changing Windows light/dark while a tool window, the shelf or cards are open: they repaint with the new tokens without reopening (Task 1 applies brushes as `DynamicResource`; manual check 66).
- A tool window opened on a 150 % monitor next to a 100 % one: the borderless frame and shadow margin still center on the drop monitor and Esc/Enter still work (Task 3 keeps `ToolDispatcher.Present`; manual check 67).
- Long Korean file names in titles and cards ("2026 제주 가족 여행 바다 사진 원본.jpg"): ellipsis in one line, never pushing the close or cancel button off the card (Tasks 3 and 5).

---

### Task 1: Palette in Core, brushes and WPF-UI overrides in App

**Files:**
- Create: `src/Zest.Core/Design/ZestColors.cs`, `src/Zest.Core/Design/Contrast.cs`
- Create: `tests/Zest.Core.Tests/ZestColorsTests.cs`
- Create: `src/Zest.App/Theme/ZestTheme.cs`
- Modify: `src/Zest.App/App.xaml.cs` (call `ZestTheme.Apply()` right after each `ApplicationThemeManager.ApplySystemTheme()`), `src/Zest.App/Wheel/WheelStyle.cs` (`SystemPalette` reads the theme through `ZestTheme.IsDark`)

**Interfaces:**
- Produces (Core, `Zest.Core.Design`):
  - `public readonly record struct Argb(uint Value)` with `byte A, R, G, B` and `static Argb Hex(string "#AARRGGBB" or "#RRGGBB")`.
  - `public sealed record Citrus(string Name, Argb Fill, Argb OnFill, Argb InkLight, Argb WashLight)`; `Argb Ink(bool dark)`, `Argb Wash(bool dark)`.
  - `public sealed record Surface(Argb Window, Argb Raised, Argb Sunken, Argb Line, Argb LineStrong, Argb Seg, Argb Text, Argb Text2, Argb Text3, Argb Danger, Argb DangerWash)`.
  - `public static class ZestColors { Surface Light; Surface Dark; Citrus Lemon; Citrus Tangerine; Citrus Lime; Surface For(bool dark); Citrus For(WheelMode mode); }`
  - `public static class Contrast { double Ratio(Argb fg, Argb bg); }` (WCAG relative luminance; a translucent foreground is composited over the background first).
- Produces (App): `public static class ZestTheme { bool IsDark { get; } void Apply(); Brush Brush(string key); }` and resource keys `Zest.Window`, `Zest.Raised`, `Zest.Sunken`, `Zest.Line`, `Zest.LineStrong`, `Zest.Seg`, `Zest.Text`, `Zest.Text2`, `Zest.Text3`, `Zest.Danger`, `Zest.DangerWash`, and per citrus `Zest.{Lemon|Tangerine|Lime}.{Fill|OnFill|Ink|Wash}`.

- [ ] **Step 1: Write the failing tests** (`ZestColorsTests`):
  - `Text_OnWindow_PassesAA_BothThemes` → `Contrast.Ratio(s.Text, s.Window) >= 4.5` and same for `Text2`, for `Light` and `Dark`.
  - `OnFill_OnEachCitrus_PassesAA` → for Lemon, Tangerine, Lime: `Ratio(OnFill, Fill) >= 4.5`.
  - `LightInk_OnWhite_PassesNonText` → `Ratio(InkLight, Hex("#FFFFFF")) >= 3.0` for all three.
  - `ModeMapping` → `For(WheelMode.Convert) == Lemon`, `Tools == Tangerine`, `Actions == Lime`.
  - `Ratio_KnownPair` → `Ratio(Hex("#000000"), Hex("#FFFFFF"))` equals 21 within 0.01.
- [ ] **Step 2: Run** `dotnet test --project tests/Zest.Core.Tests --filter-class "Zest.Core.Tests.ZestColorsTests"` — Expected: compile failure (types missing).
- [ ] **Step 3: Implement `ZestColors` and `Contrast`** with the exact Global Constraints values.
- [ ] **Step 4: Run the same command** — Expected: 5 passed.
- [ ] **Step 5: Implement `ZestTheme`.** `IsDark` reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (0 → dark, missing → light; move the reading out of `WheelStyle.SystemPalette`). `Apply()` writes frozen `SolidColorBrush`es for every `Zest.*` key into `Application.Current.Resources`, then overrides these WPF-UI keys in the same dictionary: `ApplicationBackgroundBrush`→Window, `TextFillColorPrimaryBrush`→Text, `TextFillColorSecondaryBrush`→Text2, `TextFillColorTertiaryBrush`→Text3, `DividerStrokeColorDefaultBrush`→Line, `ControlStrokeColorDefaultBrush`→LineStrong, `CardBackgroundFillColorDefaultBrush`→Sunken, `CardStrokeColorDefaultBrush`→Line, `SubtleFillColorSecondaryBrush`→Seg, `SystemFillColorCriticalBrush`→Danger, `SystemFillColorSuccessBrush`→Lime ink, `AccentFillColorDefaultBrush`→Tangerine fill, `TextOnAccentFillColorPrimaryBrush`→Tangerine on-fill, and `ControlCornerRadius`→`new CornerRadius(8)`. Keys must live in `Application.Current.Resources` itself (not a merged dictionary) so they win over WPF-UI's `ThemesDictionary` after every `ApplySystemTheme()`.
- [ ] **Step 6: Verify the override survives a theme switch.** Run `./build.ps1`, start `dist\Zest.exe`, open the compress window, switch Windows between light and dark: the primary button stays tangerine with dark text and the window background follows the token. Record OK in the commit message.
- [ ] **Step 7: Commit** `git commit -m "feat: Zest citrus palette with contrast tests and WPF-UI overrides"`

### Task 2: Wheel without the disc

**Files:**
- Modify: `src/Zest.Core/Wheel/WheelGeometry.cs` (`Gap = 8`; `PetalAt` gives the gutter to a petal; delete unused `Hole`)
- Modify: `tests/Zest.Core.Tests/WheelGeometryTests.cs`
- Modify: `src/Zest.App/Wheel/WheelStyle.cs`, `WheelView.xaml`, `WheelView.xaml.cs`, `WheelController.cs`, `src/Zest.App/Render/RenderHarness.cs`
- Delete: `src/Zest.App/Wheel/BackdropCapture.cs`

**Interfaces:**
- Consumes: `ZestColors.For(WheelMode)`, `ZestTheme.IsDark` (Task 1).
- Produces: `WheelView.Mode { get; set; }` (`WheelMode`, set before `SetItems`); `WheelStyle.ModeChip(WheelMode) => "Shift 변환" | "Alt 도구" | "Ctrl 액션"`; `WheelPalette` reduced to theme colors: `(bool Dark, Color Petal, Color Label, Color Hub, Color HubText, Color ShadowColor, double ShadowOpacity)`.

Rules: hit-test keeps `r < Inner` (54) and `r > Outer` (128) → null; between them the petal is `floor((bearing + sector/2) / sector) % count`, so an exact boundary belongs to the next petal clockwise. Drawing keeps the 8 px straight gutters. Petal corners: outer 10, inner 7. Lift 6 px. Hub radius 40. Petal edge: 1 px of the mode's citrus — fill color at alpha `0x38` in dark, ink color at alpha `0x59` in light. Selected petal: citrus fill, label in the citrus on-fill color, no gradient. Labels: dark `#ECE7D8`, light `#2A2622`; format labels Segoe UI Variable Display **Bold** 15, Korean Pretendard SemiBold 12. Petal fill: dark `#24221C`, light `#FFFFFF`. Hub: dark `#1D1B16`, light `#FFFFFF`, same 1 px citrus edge; it shows the hovered label (Bold 18 / Korean 15) and under it a pill with `ModeChip(mode)` in citrus ink on citrus wash (8.5 → render at 10 px). One `DropShadowEffect` on the petals canvas and one on the hub: dark blur 18, depth 6, opacity 0.45; light blur 18, depth 4, opacity 0.16; color `#1D1B16`, direction 270. Empty state: the existing pill in window token + text-2. No backdrop capture, no tint, no ring.

- [ ] **Step 1: Update the geometry tests first:**
  - Replace `PetalAt_Gutter_IsNull` with `PetalAt_Gutter_GoesToNextPetalClockwise`: bearing π/4, r = 90, count 4 → 1.
  - In `PetalAt_TwoPetals`: `(90, 0)` → 1 (was null); `(0,-90)` → 0 and `(0,90)` → 1 unchanged.
  - Add `PetalAt_JustBeforeBoundary_StaysOnPetal`: bearing π/4 − 0.01, r = 90, count 4 → 0.
  - Keep the hole, outside, radius-bound, wrap and single-petal tests as they are.
- [ ] **Step 2: Run** `dotnet test --project tests/Zest.Core.Tests --filter-class "Zest.Core.Tests.WheelGeometryTests"` — Expected: the two changed tests FAIL.
- [ ] **Step 3: Implement the hit-test rule** in `PetalAt` and set `Gap = 8`.
- [ ] **Step 4: Run the same command** — Expected: all pass.
- [ ] **Step 5: Restyle the wheel** per the Rules above: remove `Glass`, `Backdrop`, `Tint`, `Ring`, `InnerLine` from `WheelView.xaml` and their code; `WheelController.DrawWheel` no longer captures the screen; `WheelController.Show` and `SwitchMode` set `_view.Mode = _mode` and `_view.Palette = WheelStyle.SystemPalette()` before `SetItems`; delete `BackdropCapture.cs` and every reference.
- [ ] **Step 6: Render.** `RenderHarness` renders each set once per mode it belongs to (convert sets lemon, tool sets tangerine, action sets lime) on the existing synthetic backdrop and on plain white and black, file names `wheel-{light|dark}-{convert|tools|actions}-{n}petals-{plain|hl|on-white|on-black}.png`.
  Run: `./build.ps1; dist\Zest.exe --render "$env:TEMP\zest-render"`
  Expected: PNGs written; looking at them, petals are distinct on white and black, the selected petal reads clearly, no disc behind the petals.
- [ ] **Step 7: Full tests** `dotnet test --project tests/Zest.Core.Tests` — Expected: 0 failed.
- [ ] **Step 8: Commit** `git commit -m "feat: floating citrus petals without the disc; gutters drop on the nearest petal"`

### Task 3: ZestWindow frame and shared control styles

**Files:**
- Create: `src/Zest.App/Theme/ZestWindow.cs`, `src/Zest.App/Theme/ZestControls.xaml`
- Modify: `src/Zest.App/App.xaml` (merge `ZestControls.xaml` after WPF-UI's dictionaries)

**Interfaces:**
- Produces:
  - `public class ZestWindow : Window` — `WindowStyle=None`, `AllowsTransparency=True`, `Background=Transparent`, `ShowInTaskbar=True`, `ResizeMode=NoResize` unless `IsResizable`; dependency properties `string Heading`, `object? Footer`, `bool IsResizable` (when true: `WindowChrome` with `ResizeBorderThickness=8`, `CaptionHeight=0`, `GlassFrameThickness=0`). Dragging the header moves the window (`DragMove`). The close button raises `Close()`.
  - Template `Zest.WindowTemplate` in `ZestControls.xaml`: 24 px transparent margin for the shadow → `Border` (radius 14, `Zest.Window` background, 1 px `Zest.Line`, `DropShadowEffect` blur 32 / depth 12 / opacity 0.14 light, 0.45 dark) → rows: header (title centered, Bold 14, `Zest.Text`, ellipsis; round close button 28 px on the right with `Zest.Seg` fill and glyph E711 at 10 px), content, footer (`Footer` content inside a 1 px `Zest.Line` top border, padding 12,16).
  - Styles: `Zest.FooterBar` (a `DockPanel`; children docked right are buttons with 8 px spacing, the last child fills the left), `Zest.SegmentPanel` (`Border` radius 9, `Zest.Seg`, padding 3, holding a one-row `UniformGrid`), `Zest.Segment` (a `RadioButton` template: height 28, radius 7, text-2; checked → `Zest.Raised` fill, text, a small shadow), `Zest.Chip` (`Border` radius 999, padding 2,8), `Zest.ChoiceChip` (a `RadioButton` template: height 28, radius 7, `Zest.Seg`; checked → tangerine wash fill, 1 px tangerine ink border and text), `Zest.Field` (TextBox: height 32, radius 8, 1 px `Zest.LineStrong`, `Zest.Raised`; focused → 1 px tangerine ink border), `Zest.GhostButton` (transparent; hover `Zest.Seg`), `Zest.Hairline` (1 px `Zest.Line`).
- Pressable elements scale to 0.97 on press over 160 ms ease-out (a `ScaleTransform` driven from the style's triggers); popups open with the Global Constraints motion via `ZestWindow.OnSourceInitialized`.

- [ ] **Step 1: Build `ZestWindow` and `ZestControls.xaml`** with the values above.
- [ ] **Step 2: Add a render.** `ToolWindowRenders` gets `ZestWindowSample(dark)`: a `ZestWindow` with heading "압축", a two-line body and a footer with one hint and buttons 취소 / 압축, saved as `zest-window{,-dark}.png`.
  Run: `./build.ps1; dist\Zest.exe --render "$env:TEMP\zest-render"` — Expected: the PNGs match the popup page's window frame (centered title, close on the right, buttons right-aligned).
- [ ] **Step 3: Commit** `git commit -m "feat: ZestWindow frame and shared control styles"`

### Task 4: Tool windows on the new frame

**Files:**
- Modify (XAML and code-behind): `src/Zest.App/Tools/CompressOptionsWindow.*`, `CropWindow.*`, `CropCanvas.cs`, `OrderWindow.*`, `ModelDownloadWindow.*`, `TextResultWindow.*`, `src/Zest.App/Tray/LicenseWindow.*`, `src/Zest.App/Tools/ToolStyles.xaml`, `src/Zest.App/Render/ToolWindowRenders.cs` (frame the new windows as they are, no Mica stand-in)

**Interfaces:**
- Consumes: Task 3 styles and `ZestWindow`. Every window changes its base class from `FluentWindow` to `ZestWindow`; constructors and callbacks stay the same, so `ToolDispatcher` needs no change.

Per-window layout (from the popup page):
- **압축:** rows separated by hairlines — file name (text-2) + "원본 크기 5.6 MB" (Bold 16); a `Zest.SegmentPanel` with the existing `PresetRadio` / `TargetRadio` / `PercentRadio` restyled as `Zest.Segment` labelled 프리셋 / 목표 크기 / 비율, showing only the selected mode's panel (preset: 약하게 / 세게 as segments; target: number + KB/MB; percent: value line, slider, "예상 3.5 MB 이하" with the number in tangerine ink); "최대 크기" row with the existing combo. Footer: left = error line when there is one, otherwise "옆에 ‘{stem} (compressed).{ext}’로 저장" for a single file; right = 압축.
- **자르기:** heading "자르기 · {file name}"; `IsResizable = true`; preview inset 16 px with radius 10; selection outline and handles in tangerine (`CropAccent` returns tangerine fill), handles white with a tangerine 1.5 px ring, a 3×3 thirds grid at 35 % white inside the selection; one control row: ratio `Zest.ChoiceChip`s built from the existing `Ratios` table (replacing `AspectBox`, same indices), then X/Y/폭/높이 fields. Footer right: 자르기.
- **PDF 만들기 / 합치기:** hint "끌거나 화살표로 순서를 바꾸세요." on top; rows = position number, 44×33 thumbnail, name (SemiBold) over detail (text-2), grip glyph; the selected row gets tangerine wash (no accent pill). Footer right: ↑, ↓, then 만들기/합치기.
- **배경 제거:** an icon tile (tangerine wash, ink glyph) beside "배경 제거 모델이 필요합니다" and "처음 한 번만 내려받습니다(973 MB). 이미지는 이 PC 안에서만 처리하고 밖으로 보내지 않습니다."; progress row (phase + amount, 4 px bar in tangerine ink); "저장 위치 %LOCALAPPDATA%\Zest\models" row. Footer: left = error line; right = 취소, 내려받기.
- **글자 복사:** `IsResizable = true`; first line = source name + a lime chip "클립보드에 복사함" (with a check) when `copiedAtOnce`; the text box with `Zest.Raised` and a 1 px line inset, radius 10. Footer right: 닫기, 텍스트로 저장, 복사 (primary).
- **라이선스:** `ZestWindow`, heading "Zest 라이선스", `IsResizable = true`, the existing read-only text.

- [ ] **Step 1: Convert 압축 and 자르기**, then render.
  Run: `./build.ps1; dist\Zest.exe --render "$env:TEMP\zest-render"` — Expected: `compress-options*.png` and `crop*.png` in both themes match the popup page sections, buttons right-aligned.
- [ ] **Step 2: Convert the other four windows**, render again — Expected: `order*`, `model-download*`, `text-result*` match the page; LicenseWindow opens from the tray with the new frame.
- [ ] **Step 3: Full tests** `dotnet test --project tests/Zest.Core.Tests` — Expected: 0 failed.
- [ ] **Step 4: Commit** `git commit -m "feat: tool windows on the Zest frame"`

### Task 5: Progress cards, shelf and tray icon

**Files:**
- Create: `src/Zest.Core/Jobs/JobKind.cs`
- Modify: `src/Zest.App/Jobs/JobRunner.cs`, `ProgressCard.xaml(.cs)`, `ProgressCardHost.cs`, `src/Zest.App/App.xaml.cs` (`StartJobs`), `src/Zest.App/Tools/ToolDispatcher.cs`, `src/Zest.App/Actions/ActionRunner.cs`, `src/Zest.App/Shelf/ShelfWindow.xaml(.cs)`, `src/Zest.App/Tray/TrayIconArt.cs`, `src/Zest.App/Render/RenderHarness.cs`
- Test: `tests/Zest.Core.Tests/JobKindTests.cs`

**Interfaces:**
- Produces: `public enum JobKind { Convert, Tool, Action }` and `public static WheelMode ModeOf(JobKind kind)` in `Zest.Core.Jobs`; `JobRunner.Run(string title, string detail, JobKind kind, Func<OperationContext, Task<IReadOnlyList<string>>> work)` and `ProgressCardHost.Add(string title, string detail, JobKind kind, Action cancel)`.

Card copy (titles name the task, the second line names the file; no particle after format names): convert → title `"{TARGET} 변환"`, detail = the file name or `"{first} 외 {n}개"`; tools → `"압축"`, `"자르기"`, `"나누기"`, `"PDF 만들기"`, `"합치기"`, `"배경 제거"`, `"글자 복사"`, `"텍스트 저장"` with the file detail; actions keep their current words as titles with the file detail. States: running → detail line, right side = percent (Bold 12 text-2, tabular) when the progress is determinate, then a ghost 취소; waiting → clock badge, detail `"{detail} · 대기 중"`; done → a 22 px round badge in the kind's citrus fill with a check in its on-fill color, detail `"{result name} · 클릭하면 탐색기에서 보기"`; failed → danger-wash badge with "!", the title stays as it was and the error message (wrapping, up to 3 lines) replaces the detail line; cancelled → detail "취소됨". Bar: 3 px, track `Zest.Line`, fill = kind's ink, full width under the text. Card: radius 12, `Zest.Window`, one `DropShadowEffect` (blur 20, depth 4, opacity 0.10 light / 0.40 dark). Summary card: title "외 N개 작업", detail "위 카드가 닫히면 차례로 올라옵니다", ghost "모두 취소".
Shelf: card radius 14; lime tab 36×5 centered on top; header = "파일 N개" (Bold 13.5) over "끌어서 꺼내기"; two 26 px round mini buttons: view toggle (grid ↔ list, grid first, kept for the session) and close; grid view = `ListBox` with a 3-column `UniformGrid` items panel, tiles 44 px icon + two-line name; list view = today's rows; selection = lime wash + 1 px lime ink inset; `DropRing` and the old `ShelfAccentBrush` use lime (`Zest.Lime.Ink`).
Tray icon: three ring segments (`PetalPath.Petal(center, i, 3, …)`) filled lemon, tangerine, lime clockwise from 12 o'clock with the existing gutter math, on both taskbar themes; drop the monochrome rind.

- [ ] **Step 1: Write `JobKindTests.ModeOf_MapsEachKind`** → Convert→Convert, Tool→Tools, Action→Actions; run `--filter-class "Zest.Core.Tests.JobKindTests"` — Expected: FAIL (missing type).
- [ ] **Step 2: Add `JobKind`** — run again, Expected: PASS.
- [ ] **Step 3: Thread `title, detail, kind` through `JobRunner`, `ProgressCardHost`, `ProgressCard` and every caller** with the copy above; restyle the card and summary card.
- [ ] **Step 4: Restyle the shelf and add the grid/list toggle.**
- [ ] **Step 5: Redraw the tray icon.**
- [ ] **Step 6: Render** — `progress-card-{running|waiting|done|failed}{,-dark}.png` for each kind, `shelf{,-dark}.png` (grid) and `shelf-list{,-dark}.png`, `tray-icons.png`. Expected: they match the popup page.
- [ ] **Step 7: Add manual checks** to `docs/manual-checklist.md`, table "### Zest 디자인 (2026-10-24)":
  - `| 65 | 흰 문서·검은 화면 위에서 Shift/Alt/Ctrl 휠 | 원판 없이 꽃잎마다 테두리와 그림자로 구분되고, 고른 꽃잎이 휠 색(레몬·탠저린·라임)으로 칠해짐 | |`
  - `| 66 | 창·선반·카드를 띄운 채 Windows 라이트↔다크 전환 | 다시 열지 않아도 모두 새 색으로 바뀜 | |`
  - `| 67 | 배율이 다른 모니터에 도구 창 띄우기, 머리글 끌기, Esc, Enter | 놓은 모니터 가운데에 뜨고, 머리글로 옮겨지며, Esc는 닫기, Enter는 주 버튼 | |`
  - `| 68 | 꽃잎 사이 틈에서 손 떼기 / 가운데 구멍에서 손 떼기 | 틈: 가까운 꽃잎 실행. 구멍: 아무 일 없음 | |`
  - `| 69 | 긴 한글 파일 이름으로 변환·압축 | 카드와 창 제목이 한 줄 말줄임, 취소·닫기 버튼이 밀려나지 않음 | |`
- [ ] **Step 8: Full tests** — Expected: 0 failed.
- [ ] **Step 9: Commit** `git commit -m "feat: citrus progress cards, shelf grid view and tri-color tray icon"`
