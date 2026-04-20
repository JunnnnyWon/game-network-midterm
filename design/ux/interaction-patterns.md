# Interaction Pattern Library

## Pattern: Split-Layer Match UI

- Purpose: Keep player-facing hierarchy clear by separating `prematch`, `active HUD`, `results`, and `network telemetry` into different visual layers.
- Use when: a screen must show both gameplay state and support/debug state without collapsing them into one panel.
- Rules:
  - `prematch` is the primary left-side card
  - `results` is a centered modal with strongest emphasis
  - `active HUD` uses compact edge-aligned cards
  - `network telemetry` is always visually subordinate to gameplay/result information

## Pattern: Authoritative Status Card

- Purpose: Show a short label plus one or two lines of high-value authoritative state.
- Use when: displaying timer, score, cooldown, or effect state.
- Rules:
  - short uppercase label
  - large numeric or state value
  - never put more than one primary concept in one card

## Pattern: Readable Prematch Form

- Purpose: Make name entry, room entry, and room actions obvious with keyboard/mouse.
- Use when: the player must connect, create, join, or ready up.
- Rules:
  - fields first
  - actions second
  - room/member summaries below the actions
  - `Enter` maps to primary action, `Escape` to back/cancel where allowed

## Pattern: Persistence-Visible Results

- Purpose: Make end result, save state, and leaderboard all visible without ambiguity.
- Use when: a match ends and server/database status matters.
- Rules:
  - outcome and score first
  - persistence state second
  - leaderboard rows last
  - save failure must never be color-only; pair with explicit text
