# Zest README Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A current-style GitHub README for Zest in Korean (`README.md`) and English (`README.en.md`), led by real app images rendered by the app itself.

**Architecture:** The render harness gains a README mode that writes hero, wheel and popup images (light and dark) into `docs/images/`; both READMEs use `<picture>` with `prefers-color-scheme` sources so GitHub shows the matching theme, and they mirror each other section for section.

**Tech Stack:** Markdown (GitHub-flavored), the existing WPF render harness, shields.io static badges.

**Spec:**
- User request (2026-10-24): "2026년 하반기 기준 트렌디한 깃헙 리드미 디자인 참고해서 우리 리드미도 꾸며줘. 한국어 영어 두 버전으로."
- Reference pattern found the same day: theme-aware hero images with `<picture>` / `prefers-color-scheme` (light image as the fallback), a 1280×640 hero carrying name, one-line pitch and a real app visual, a real screenshot above the fold, and a separate 1280×640 social preview image (e.g. github.com/jbuffin/agent-launcher/pull/9, github.com/duskfleet/duskfleet/issues/167, github.com/cathrynlavery/diagram-design/pull/263).
- Runs last, after the rename, design and metadata plans, so the images show the shipped Z3 look and the metadata tool.

## Global Constraints

- `README.md` is Korean (the app's UI language); `README.en.md` is English. Line 1 area of each: a language switch — `**한국어** · [English](README.en.md)` / `[한국어](README.md) · **English**`.
- Facts only from the shipped app and its docs: features, requirements, privacy, limits as in the current README plus the metadata tool and the Zest name. No download links (there is no GitHub release), no counts that are not computed (no "269 conversions"), no invented user numbers.
- Credit the original: a section naming Lime by LumenJSN (https://github.com/LumenJSN/Lime, MIT) as the app this is ported from, and pointing to `NOTICE.md` for its license text and the libraries.
- Images live in `docs/images/` and are produced by `Zest.exe --render-readme docs/images`; nothing hand-drawn or AI-generated.
- Badges: exactly three static shields — Windows 10 | 11, .NET 10, License MIT.
- Both files carry the same sections in the same order; headings differ only by language.

## Review Focus

- GitHub dark mode: the hero, wheel and popup images must switch to their dark versions (no white-boxed light image on a dark page) — every image is a `<picture>` with both sources (Task 2 check).
- Narrow (mobile) GitHub view: the three-wheel table must not force sideways scrolling of the page — images are set to `width="100%"` inside cells and the table has at most 3 columns (Task 2).
- Broken relative image paths after the repo folder rename: all paths are relative to the repo root (`docs/images/...`), checked by a script (Task 2 step 3).
- The English file drifting from the Korean one: heading counts must match (Task 2 step 3).

---

### Task 1: README images from the render harness

**Files:**
- Modify: `src/Zest.App/App.xaml.cs` (`--render-readme <folder>` beside `--render`), `src/Zest.App/Render/RenderHarness.cs` (new `RunReadme(string folder)`)
- Create (generated, committed): `docs/images/hero-light.png`, `hero-dark.png`, `wheel-convert-light.png`, `wheel-convert-dark.png`, `wheel-tools-light.png`, `wheel-tools-dark.png`, `wheel-actions-light.png`, `wheel-actions-dark.png`, `popups-light.png`, `popups-dark.png`, `social-preview.png`

**Interfaces:**
- Produces: `public static int RunReadme(string folder)` — returns 0 on success; image names exactly as listed.

Compositions (all rendered at 2×): hero 1280×640 — left half the word "Zest" (Segoe UI Variable Display Bold 96), under it the line "파일을 끌다가 키를 한 번 탭하면, 그 자리에서 변환 · 도구 · 액션" (Pretendard 22, text-2) and the three key chips (Shift 레몬, Alt 탠저린, Ctrl 라임); right half one convert wheel (JPG highlighted) over the existing synthetic Explorer backdrop; page background = window token of the theme. Wheel images 640×640 — one wheel each (convert: photo.png with JPG highlighted; tools: photo.png with 메타데이터 highlighted; actions: 선반 highlighted). Popups 1280×800 — a progress card stack (running lemon, done lemon, running tangerine), the compress window and the shelf, side by side on the stage color. Social preview 1280×640 = the light hero without the key chips.

- [ ] **Step 1: Implement `RunReadme`** reusing the existing `Wheel(...)`, card and tool-window render helpers.
- [ ] **Step 2: Render** — `./build.ps1; dist\Zest.exe --render-readme docs/images` — Expected: the 11 files exist at the listed sizes ×2; the hero text is Pretendard (check `render.log`).
- [ ] **Step 3: Commit** `git commit -m "docs: README images rendered by the app"`

### Task 2: README.md (Korean) and README.en.md (English)

**Files:**
- Rewrite: `README.md`
- Create: `README.en.md`

Section order (both files): hero `<picture>` (dark source `docs/images/hero-dark.png`, light `docs/images/hero-light.png`, `alt` = name + pitch) → language switch → one-line pitch → three badges → **세 개의 휠 / Three wheels** (a 3-column table: Shift · 변환 · 레몬, Alt · 도구 · 탠저린, Ctrl · 액션 · 라임, each cell a `<picture>` of that wheel and a one-sentence description) → **쓰는 법 / How to use** (the four steps of the current README, unchanged in meaning) → **기능 / Features** (convert, tools including 메타데이터 with one sentence on location removal and that originals are never changed, actions; formats list inside `<details>`) → **창과 카드 / Popups** (`<picture>` of popups) → **설치와 빌드 / Install and build** (requirements, `./build.ps1`, `dist\Zest.exe`, test command `dotnet test --project tests/Zest.Core.Tests`) → **트레이 메뉴 / Tray menu** → **개인정보 / Privacy** (the current bullets) → **알려진 제한 / Known limits** → **원작과 라이선스 / Credits and license** (Lime by LumenJSN, MIT; NOTICE.md; this project MIT).

- [ ] **Step 1: Write `README.md`** with the sections above.
- [ ] **Step 2: Write `README.en.md`** mirroring it.
- [ ] **Step 3: Check links and parity**
  Run (PowerShell): `foreach ($f in 'README.md','README.en.md') { Select-String -Path $f -Pattern '(docs/images/[\w\-]+\.png)' -AllMatches | ForEach-Object { $_.Matches.Value } | Sort-Object -Unique | Where-Object { -not (Test-Path $_) } }` — Expected: no output. `(Select-String README.md -Pattern '^#{1,3} ').Count -eq (Select-String README.en.md -Pattern '^#{1,3} ').Count` — Expected: `True`.
- [ ] **Step 4: Commit** `git commit -m "docs: Korean and English README for Zest"`
- [ ] **Step 5: Hand-off note for the user** (in the final message, not in a file): upload `docs/images/social-preview.png` under GitHub → Settings → Social preview; look at the README on GitHub once in light and dark mode.
