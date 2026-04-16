---
name: brainstorm
description: Guided game concept ideation for Codex Game Studios
argument-hint: "[genre or theme hint, or 'open'] [--review full|lean|solo]"
---

# Brainstorm

Codex-native guided ideation that preserves the original workflow while replacing Claude-only interaction primitives.

## Rules
- Ask one question at a time.
- Present analysis before asking for a decision.
- Use structured user input when available; otherwise use concise plain-text options.
- If design review mode is `full`, you may use Codex-native subagents for creative/art review after pillars are defined.

## Flow
1. Parse hint + review mode.
2. Resume existing concept files if present.
3. Run phases interactively:
   - creative discovery
   - concept generation
   - core loop design
   - pillars and anti-pillars
   - concept crystallization into `design/gdd/game-concept.md`
4. Preserve the original design methods:
   - verb-first design
   - mashup method
   - experience-first / MDA-backward framing
5. When visual direction or cross-domain review is needed, use Codex-native subagents instead of Claude-only child-agent semantics.

## Required outputs
- refined concept direction
- confirmed pillars + anti-pillars
- visual identity anchor when relevant
- `design/gdd/game-concept.md`

## Codex replacements
- Structured decision capture -> structured input or one concise question
- Claude-only subagent orchestration -> Codex native subagents / OMX orchestration
