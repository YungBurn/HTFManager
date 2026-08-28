# UI specification

## Shell

Default window: 1360 x 820. Minimum: 1100 x 700.

| Region | Width | Responsibility |
|---|---:|---|
| Left navigation | 220 px | Navigation only |
| Main workspace | flexible | Current page workflow |
| Right control panel | 300 px | Game status, Play, profile and quick actions |
| Bottom status bar | 28 px | Global readiness state |

## Visual language

- Dark, low-noise "Deep Sea Minimal" design.
- No emoji in the interface.
- Icons are local vector `StreamGeometry` resources; no icon-font dependency is required.
- Primary accent is reserved for Play, active navigation and important healthy states.
- Cards use 10 px corner radius and 1 px borders.
- The right panel is persistent across pages.

## Navigation

Home, Mods, Discover, Profiles, Tools, Settings.

The selected page is stored as `LastPage` in local settings and restored at launch.

## Localization

Supported locales in v0.1:

- `zh-CN`
- `en-US`

All XAML labels use `DynamicResource`. Language selection is available on the Settings page and is persisted immediately.
