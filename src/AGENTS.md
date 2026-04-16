# Source Directory — Codex Rules

Use this scope for game code and technical implementation guidance.

## Rules
- Always check the configured engine reference docs before assuming APIs.
- Public APIs need doc comments.
- Gameplay values should be data-driven, not hardcoded.
- Prefer dependency injection over singleton-heavy design for testability.
- Tests belong in `tests/`, not `src/`.
- When a Codex-native implementation differs from the original Claude guidance, prefer the change that preserves workflow value and technical correctness.
