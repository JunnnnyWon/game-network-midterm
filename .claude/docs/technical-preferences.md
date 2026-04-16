# Technical Preferences

<!-- Populated by /setup-engine. Updated as the user makes decisions throughout development. -->
<!-- All agents reference this file for project-specific standards and conventions. -->

## Engine & Language

- **Engine**: Unity 6.3 LTS (6000.3.10f1)
- **Language**: C#
- **Rendering**: Universal Render Pipeline (URP) with 2D Renderer
- **Physics**: Unity 2D Physics (Physics2D)

## Input & Platform

<!-- Written by /setup-engine. Read by /ux-design, /ux-review, /test-setup, /team-ui, and /dev-story -->
<!-- to scope interaction specs, test helpers, and implementation to the correct input methods. -->

- **Target Platforms**: PC
- **Input Methods**: Keyboard/Mouse
- **Primary Input**: Keyboard/Mouse
- **Gamepad Support**: None
- **Touch Support**: None
- **Platform Notes**: Competitive top-down UI must prioritize readability of score, timer, trap state, and ranking data on a single PC display.

## Naming Conventions

- **Classes**: PascalCase (e.g., `PlayerController`)
- **Variables**: Public properties/fields PascalCase; private fields `_camelCase`; local variables `camelCase`
- **Signals/Events**: PascalCase for event types and `OnPascalCase` for event methods
- **Files**: PascalCase matching primary class name (e.g., `PlayerController.cs`)
- **Scenes/Prefabs**: PascalCase (e.g., `MainArena`, `BatteryPickup`, `PlayerAvatar`)
- **Constants**: PascalCase for public constants or `UPPER_SNAKE_CASE` for shared static readonly values

## Performance Budgets

- **Target Framerate**: [TO BE CONFIGURED]
- **Frame Budget**: [TO BE CONFIGURED]
- **Draw Calls**: [TO BE CONFIGURED]
- **Memory Ceiling**: [TO BE CONFIGURED]

## Testing

- **Framework**: Unity Test Framework (NUnit + PlayMode/EditMode tests)
- **Minimum Coverage**: Moderate — cover core gameplay logic, match-state rules, and major networking flows
- **Required Tests**: Balance formulas, gameplay systems, networking (if applicable)

## Forbidden Patterns

<!-- Add patterns that should never appear in this project's codebase -->
- [None configured yet — add as architectural decisions are made]

## Allowed Libraries / Addons

<!-- Add approved third-party dependencies here -->
- [None configured yet — add as dependencies are approved]

## Architecture Decisions Log

<!-- Quick reference linking to full ADRs in docs/architecture/ -->
- [No ADRs yet — use /architecture-decision to create one]

## Engine Specialists

<!-- Written by /setup-engine when engine is configured. -->
<!-- Read by /code-review, /architecture-decision, /architecture-review, and team skills -->
<!-- to know which specialist to spawn for engine-specific validation. -->

- **Primary**: unity-specialist
- **Language/Code Specialist**: unity-specialist
- **Shader Specialist**: unity-shader-specialist
- **UI Specialist**: unity-ui-specialist
- **Additional Specialists**: unity-dots-specialist, unity-addressables-specialist
- **Routing Notes**: Use `unity-specialist` for general gameplay, scene, and architecture work; `unity-ui-specialist` for HUD/leaderboard UI; `unity-shader-specialist` for URP/material issues; `unity-addressables-specialist` only if asset loading grows beyond basic assignment scope.

### File Extension Routing

<!-- Skills use this table to select the right specialist per file type. -->
<!-- If a row says [TO BE CONFIGURED], fall back to Primary for that file type. -->

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (primary language) | unity-specialist |
| Shader / material files | unity-shader-specialist |
| UI / screen files | unity-ui-specialist |
| Scene / prefab / level files | unity-specialist |
| Native extension / plugin files | unity-specialist |
| General architecture review | Primary |
