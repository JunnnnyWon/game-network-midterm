---
name: setup-engine
description: Configure engine, version, and technical preferences for the Codex-native studio repo
argument-hint: "[engine] | [engine version] | refresh | upgrade [old-version] [new-version] | no args for guided selection"
---

# Setup Engine

Configure the project's engine and write the repo's technical baseline.

## Goals
- populate `.claude/docs/technical-preferences.md`
- pin engine/version expectations
- identify post-cutoff knowledge risks
- establish specialist routing for later skills

## Rules
- Ask one question at a time when user preference is required.
- Use official engine docs or version-pinned local reference material for unstable API details.
- Preserve the source repo's engine-reference discipline.

## Required updates
- engine + language
- rendering + physics
- target platforms / input methods
- naming conventions
- performance budgets
- testing framework
- engine specialists + file-extension routing

## Codex-native notes
This skill keeps the original file targets where practical, but the interactive flow must use Codex-compatible questions instead of Claude-only widgets.
