# UI style guide

Internal reference for anyone changing the interface. Not needed to build or play the game —
see the [README](../README.md) for that.

## Colour palette

Two palettes, deliberately separate. Mixing them is what makes the UI look off.

**UI chrome** — buttons, panels, HUD, menus. Defined at the top of [`UIManager.cs`](../Assets/Scripts/Presentation/UIManager.cs):

| Name | Value | Means |
|---|---|---|
| `Accent` | `0.45, 0.85, 1.00` | cyan — the brand. Primary actions, enabled state, level-clear, most glows |
| `Gold` | `1.00, 0.82, 0.35` | the Daily Maze, bonuses, rewards |
| `Danger` | `1.00, 0.42, 0.42` | failure, OFF state, destructive actions |
| `StarLit` | `0.70, 0.95, 1.00` | earned star markers |
| `TitleText` | `0.90, 0.97, 1.00` | headline text — near-white, cyan-leaning |
| `OnState` / `OffState` | Accent / Danger family | toggle labels in settings |
| `DailyDoneCol` | muted `Gold` | a Daily already played today |

**Gameplay colours** — the world, in [`GameConfig.cs`](../Assets/Scripts/Core/GameConfig.cs). These carry *meaning* and are the only place other hues are allowed:

| Colour | Where | Why |
|---|---|---|
| green `0.4, 1.0, 0.7` | the exit | the one "go here" signal in the game |
| warm orange `1.0, 0.5, 0.35` | decoys | reads as "not the exit" against a cool maze |
| red `1.0, 0.22, 0.22` | decoy highlight ring | hazard |
| flame `1.0, 0.6, 0.15` | streak glow | heat = momentum |
| gold `1.0, 0.85, 0.25` | bonus echo orb | matches the UI's reward gold |
| 8 sector tints | maze walls | per-chapter identity, cycled with a lap numeral |

**Panels** — every background slab, also in `UIManager.cs`:

| Name | Value | B/G | Used by |
|---|---|---|---|
| `PanelSolid` | `0.014, 0.022, 0.052` @ 97.5% | 2.4 | menu and settings cards |
| `PanelVeil` | same hue @ 86% | 2.4 | celebration overlay |
| `PanelScrim` | `0.010, 0.016, 0.055` @ 97% | 3.4 | banner backing, daily result backdrop |
| `ButtonFill` | `0.030, 0.052, 0.125` @ 50% | 2.4 | secondary buttons |
| `CardFill` | `0.055, 0.082, 0.180` | 2.2 | settings card |

### The rules

**1. Never write `new Color(...)` inline in UI code.** Every UI element resolves to a name above, so a palette change lands everywhere at once. The one exception is a colour derived from a palette entry, e.g. `Color.Lerp(Accent, Color.white, 0.45f)`.

**2. Blue must dominate green in every panel — keep B at least 2× G.** This is the non-obvious one. At these brightness levels a *desaturated* near-black reads as olive/green to the eye, especially sitting next to gold. `0.02, 0.03, 0.05` looks like neutral grey in source and green on screen — and its B/G is 1.7, which is why an earlier 1.5× threshold wasn't strict enough. **Measure the rendered pixels; don't trust the numbers in the source.** The full-screen backdrops are the least forgiving, since there's nothing else on screen to offset the cast.

> The one deliberate exception is `GameConfig.BackgroundColor` (B/G 1.75) — the camera clear colour. It never appears as a flat field: stars, the vignette and the maze always break it up, and changing it would shift the look of the entire game.

**3. Never pipe a gameplay colour into UI chrome.** `SetCelebrationTitle` was handed the *sector* tint and applied it to the glow panel behind the title — and sector tints include teal and sea-green, so the celebration went green every few levels. A gameplay colour may tint **text**, never a panel or glow.

**4. Panels carry the bracket frame.** Callouts use the same corner-bracket sprite as the buttons, tinted to the callout's own hue (red on a fail, gold on the Daily). That is what makes a panel look like part of this game instead of a box dropped on top of it.

Green in particular is **reserved for the exit**. UI text tinted green reads as a gameplay signal and fights the cyan chrome — the celebration title, settings toggles and the Daily "done" state all used ad-hoc mint greens at one point, and it made the whole UI feel unrelated to itself.
