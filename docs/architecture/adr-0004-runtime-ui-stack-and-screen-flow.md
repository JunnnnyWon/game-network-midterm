# ADR-0004: Runtime UI Stack and Screen Flow

## Status

Accepted

## Date

2026-04-16

## Last Verified

2026-04-16

## Decision Makers

User, Codex (architecture synthesis)

## Summary

Battery Rush Arena will use **Unity UI Toolkit** as the runtime UI stack, organized around a **single root UIDocument with state panels plus a gameplay HUD overlay**. Screen flow is fixed as Menu → Join Room → Lobby → Countdown → Match HUD → Results → Leaderboard → Lobby, and the UI will render only authoritative room, score, persistence, and ranking data from the server.

## Engine Compatibility

| Field | Value |
|-------|-------|
| **Engine** | Unity 6.3 LTS (6000.3.10f1) |
| **Domain** | UI |
| **Knowledge Risk** | HIGH — UI Toolkit runtime UI is post-cutoff and must be verified in the pinned engine build |
| **References Consulted** | `docs/engine-reference/unity/VERSION.md`, `docs/engine-reference/unity/current-best-practices.md`, `docs/engine-reference/unity/deprecated-apis.md`, `docs/engine-reference/unity/modules/ui.md`, `docs/engine-reference/unity/modules/input.md` |
| **Post-Cutoff APIs Used** | UI Toolkit runtime UI (`UIDocument`, UXML, USS, `VisualElement`, `Label`, `Button`) |
| **Verification Required** | Verify panel switching, focus behavior, keyboard + mouse interaction, and HUD readability in actual Unity 6.3 player builds. |

## ADR Dependencies

| Field | Value |
|-------|-------|
| **Depends On** | ADR-0001, ADR-0002, ADR-0003 |
| **Enables** | UI system GDDs, menu/HUD implementation, results/leaderboard implementation |
| **Blocks** | Runtime UI implementation, class-demo presentation flow |
| **Ordering Note** | This ADR depends on authoritative room states and persistence status semantics already being fixed. |

## Context

### Problem Statement

The project needs a single runtime UI approach and a single player-facing flow so the game remains readable and easy to demo. Without this ADR, developers could mix UI Toolkit and UGUI, invent inconsistent screen transitions, or let the UI derive score/match outcomes independently of the authoritative server.

### Current State

The concept and reviews already identified missing UI state flow, HUD contracts, keyboard/mouse bindings, and results-vs-leaderboard separation as blockers. ADR-0001 and ADR-0002 define what the server owns, but not how the player sees it.

### Constraints

- Unity 6.3 favors UI Toolkit for new runtime UI.
- The project is PC-only with keyboard/mouse input.
- The UI must stay readable on one screen during live demo.
- The UI must not own competitive state.
- MVP should avoid overly complex multi-document UI orchestration.

### Requirements

- Must show score, timer, status effects, and match state clearly.
- Must support lobby, ready, countdown, results, leaderboard, and error states.
- Must be controllable with keyboard/mouse.
- Must surface persistence success/failure after each match.
- Must separate authoritative shared-state commands from purely local menu interactions.

## Decision

Battery Rush Arena will use **UI Toolkit runtime UI** with a **single root UIDocument** and **named panel containers** for non-match screens, plus a **persistent HUD overlay** during Active matches.

### Runtime UI structure

```text
UIDocument (Root)
├── MainMenuPanel
├── JoinRoomPanel
├── LobbyPanel
├── CountdownPanel
├── MatchHudOverlay
├── ResultsPanel
├── LeaderboardPanel
└── Modal/ErrorBannerLayer
```

- Exactly one major panel is visible at a time outside of gameplay.
- During `Active`, `MatchHudOverlay` is visible and modal/error banners may temporarily layer above it.
- Results and Leaderboard are separate panels, not one conflated screen.

### Screen flow

1. **MainMenuPanel**
   - Enter player name
   - Choose create/join room
2. **JoinRoomPanel**
   - Enter room code or confirm room creation
3. **LobbyPanel**
   - Show connected players, ready state, room code
   - `Ready` button and Enter-key shortcut
4. **CountdownPanel**
   - 3, 2, 1, Start
5. **MatchHudOverlay**
   - Local score: top-left
   - Opponent/highest rival score: top-right
   - Match timer: top-center
   - Slow-shot cooldown: bottom-right
   - Current debuff / trap status: bottom-center
   - Transient match banners (match point, persistence failure): upper-middle
6. **ResultsPanel**
   - Winner / Draw text
   - Final scores
   - Match end reason (`TargetScoreReached`, `TimeExpired`, `DisconnectForfeit`, `Draw`)
   - Persistence status line (`Saving...`, `Saved`, `Save Failed`)
7. **LeaderboardPanel**
   - Top 10 rows
   - Highlight current player row if present
   - Columns: Rank, Player, Wins, Best Score, Matches
   - Refresh button and Back-to-Lobby button
8. **Return to Lobby**
   - automatic after player confirms or rematch consensus fails

### Input contract

#### In-match controls
- **Move**: WASD
- **Aim**: mouse cursor direction relative to player
- **Fire slow shot**: Left Mouse Button
- **Pause / Back**: Escape

#### Menu / room controls
- **Confirm primary button**: Enter
- **Back / cancel**: Escape
- **Primary interaction**: mouse click
- Menu navigation should work with mouse first; keyboard confirm/back remain available for speed.

### UI state rules

- UI renders only server-provided match state, score, timer, and persistence status.
- The HUD never computes win/loss locally.
- Debuff state uses a visible status icon plus a remaining-duration bar.
- Trap trigger feedback uses a short banner + icon flash.
- Slow-shot hit confirmation shows a small attacker-side hit marker and a victim-side debuff indicator.
- Persistence failures appear in both ResultsPanel and LeaderboardPanel as a banner.

### Architecture

```text
Server Snapshot/Event -> UI ViewModel Adapter -> UI Toolkit Panels
                                      |
                                      v
                          Input/Click Intents -> ADR-0001 transport
```

### Key Interfaces

```csharp
public enum UiScreenState {
    MainMenu,
    JoinRoom,
    Lobby,
    Countdown,
    MatchHud,
    Results,
    Leaderboard
}

public interface IUiStateRouter {
    UiScreenState CurrentScreen { get; }
    void Show(UiScreenState state);
    void ShowError(string message);
    void ApplyRoomSnapshot(RoomSnapshotDto snapshot);
    void ApplyPersistenceStatus(PersistenceOutcome outcome);
}

public interface IUiIntentPublisher {
    void PublishReady();
    void PublishRematch();
    void PublishRefreshLeaderboard();
    void PublishBackToLobby();
}
```

### Implementation Guidelines

- Use UXML + USS for layout/styling and keep code-behind thin.
- Keep all authoritative data in ViewModel-style DTOs received from the transport layer.
- Keep one root UIDocument to reduce setup complexity for a class project.
- Use TextMeshPro only if a specific UI Toolkit limitation becomes blocking; otherwise do not mix UI stacks.
- Keep HUD colors high-contrast and minimalist to match the SF training theme.

## Alternatives Considered

### Alternative 1: UGUI / Canvas runtime UI
- **Description**: build all menus and HUD with legacy Canvas-based UI.
- **Pros**: familiar to many Unity tutorials; quick for basic prototypes.
- **Cons**: less future-friendly in Unity 6, more manual layout/state management, weaker alignment with engine references.
- **Estimated Effort**: Similar or slightly lower initial effort.
- **Rejection Reason**: UI Toolkit is the recommended modern UI path in Unity 6 and suits this relatively simple screen set.

### Alternative 2: Multiple UIDocuments / one document per screen
- **Description**: each screen has its own separate UI document and scene object.
- **Pros**: some separation by file.
- **Cons**: more object orchestration, more state-switch complexity, unnecessary overhead for MVP.
- **Estimated Effort**: Higher.
- **Rejection Reason**: a single root document with state panels is simpler and easier to demo.

### Alternative 3: HUD and results combined into one always-on canvas
- **Description**: reuse one giant screen and show/hide fragments without clear separation.
- **Pros**: fewer files.
- **Cons**: difficult to reason about, easy to clutter, conflates results and leaderboard concerns.
- **Estimated Effort**: Low initial effort, high maintenance confusion.
- **Rejection Reason**: the design-review explicitly flagged this as a problem.

## Consequences

### Positive
- One consistent runtime UI stack.
- Readable and explainable screen flow.
- Clear split between results and leaderboard responsibilities.
- Keyboard/mouse bindings are fixed early.

### Negative
- UI Toolkit learning overhead if the implementer is more used to Canvas.
- Requires panel routing and data binding discipline.
- Some Unity tutorials may not match the chosen UI stack exactly.

### Neutral
- The single UIDocument approach is optimized for MVP simplicity, not long-term UI scale.

## Risks

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|-----------|
| UI Toolkit runtime quirk in Unity 6.3 build | Medium | Medium | Verify panel switches and input focus in player builds |
| HUD becomes cluttered in future 4-player mode | Medium | Medium | Keep 2-player MVP HUD first; revisit 4-player layout later |
| Developers mix UGUI in ad hoc | Medium | Medium | Keep UI Toolkit as explicit project-wide runtime UI decision |

## Performance Implications
- **CPU**: low for this small UI surface.
- **Memory**: low; one root document and a few panels.
- **Load Time**: minor panel asset load only.
- **Network**: none beyond authoritative UI data already sent by server snapshots/events.

## Migration Plan

1. Create UI Toolkit root document and panel containers.
2. Implement MainMenu → Lobby → Countdown → HUD flow.
3. Bind Results and Leaderboard to persistence events from ADR-0003.
4. Add banner/error layer and debuff/cooldown widgets.

**Rollback plan**: If UI Toolkit causes a blocker that cannot be resolved quickly, write a superseding ADR before moving to UGUI. Do not mix stacks casually.

## Validation Criteria

- [ ] Every shared-state screen transition is driven by authoritative room/persistence state.
- [ ] Match HUD displays local score, opponent score, timer, cooldown, and debuff status during active play.
- [ ] Results and leaderboard are visually separate screens.
- [ ] Keyboard/mouse bindings work in both gameplay and menu flow.
- [ ] Persistence failure is visible without blocking return to lobby.

## GDD Requirements Addressed

| GDD Document | System | Requirement | How This ADR Satisfies It |
|-------------|--------|-------------|--------------------------|
| `design/gdd/game-concept.md` | Ranking | **TR-concept-009** — Players can query and view leaderboard/ranking data | Defines the runtime leaderboard screen and refresh flow |
| `design/gdd/game-concept.md` | Readability | **TR-concept-010** — Match state must remain instantly readable on a single PC display | Defines fixed HUD contract and screen hierarchy |
| `design/gdd/game-concept.md` | Control Scheme | **TR-concept-012** — Game is played with keyboard and mouse on PC | Locks keyboard/mouse gameplay and menu bindings |
| `design/gdd/game-concept.md` | Results Visibility | **TR-concept-014** — Network and database outcomes must be visible to players | Results and leaderboard panels render server-sourced room and persistence outcomes |
| `design/gdd/systems-index.md` | HUD, Results, and Ranking UI | MVP UI system | Defines runtime UI stack, panel layout, and player-facing flow |

## Related

- `docs/architecture/adr-0001-network-authority-and-transport-strategy.md`
- `docs/architecture/adr-0002-match-state-machine-and-event-ordering.md`
- `docs/architecture/adr-0003-persistence-boundary-and-leaderboard-formula.md`
- `docs/architecture/architecture.md`
- `design/gdd/game-concept.md`
- `design/gdd/systems-index.md`
