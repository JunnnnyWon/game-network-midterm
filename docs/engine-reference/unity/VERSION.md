# Unity Engine — Version Reference

| Field | Value |
|-------|-------|
| **Engine Version** | Unity 6.3 LTS (6000.3.10f1) |
| **Release Track** | Official Unity 6.3 LTS editor build |
| **Project Pinned** | 2026-04-16 |
| **Last Docs Verified** | 2026-04-16 |
| **LLM Knowledge Cutoff** | May 2025 |
| **Risk Level** | HIGH — exact project version is post-cutoff and should be checked against official docs |

## Knowledge Gap Warning

Unity 6.3 LTS, and especially the exact editor build `6000.3.10f1`, is beyond the model's reliable built-in knowledge. For unstable or version-sensitive Unity APIs, check official
Unity 6.3 documentation and release notes before implementation decisions.

## Version Pin

This project is pinned to **Unity 6.3 LTS (6000.3.10f1)**. Treat this as the source of truth
for engine-specific recommendations, package compatibility checks, and migration assumptions.

## Verified Official Sources

- Unity 6.3 LTS release stream: https://unity.com/releases/unity-6
- Unity 6.3.10f1 release notes / what's new: https://unity.com/releases/editor/whats-new/6000.3.10f1
- Unity LTS support policy: https://unity.com/releases/lts

## Practical Guidance

- Prefer **URP + 2D Renderer** for this top-down 2D project unless a later decision explicitly changes pipelines.
- Prefer **Unity Test Framework** for EditMode/PlayMode coverage.
- Treat networking, input, and UI package guidance as version-sensitive areas worth re-checking before implementation.
