# UX Spec — Network Spike Modernization

- Status: Draft
- Scope: `NetworkSpike` runtime overlay modernization for readability and presentation quality

## Context

- Player is in a short competitive multiplayer training arena.
- The UI must keep room state, score race, cooldown, debuff state, results, and persistence visibility readable at a glance.
- Accessibility tier is `Standard`.
- The network telemetry panel exists for presentation/debug value but must remain visually secondary.

## Structure

- `Prematch` is the primary entry panel on the left.
- `Active HUD` is split into top and bottom strips with compact cards.
- `Results` is a centered modal that dominates the screen after match end.
- `Network Telemetry` is a subordinate panel in the lower-right.

## Typography

- Title: 30px+
- Primary values: 24px+
- Body text: 18px
- Telemetry/support text: 15px+

## Accessibility

- Critical states are never color-only.
- Score, result, end reason, and persistence state remain text-first.
- Keyboard/mouse actions keep clear field → action → feedback flow.

## Patterns Used

- Split-Layer Match UI
- Authoritative Status Card
- Readable Prematch Form
- Persistence-Visible Results
