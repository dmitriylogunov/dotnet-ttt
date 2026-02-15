# Architecture Walkthrough

## Layers

```
Program.cs          → wiring (DI, middleware, hub route)
Models/             → data + rules (Game, Player, TurnResult)
Services/           → orchestration (matchmaking, state, thread safety)
Hubs/               → real-time transport (SignalR ↔ browser)
wwwroot/index.html  → client (vanilla HTML/JS)
```

---

## 1. Entry Point — `Program.cs`

Three things are wired:

- **SignalR** — real-time WebSocket communication
- **`GameService`** — registered as a **singleton** (one shared instance, all state in memory)
- **Hub route** — `/gamehub` is the WebSocket endpoint clients connect to

`UseDefaultFiles()` + `UseStaticFiles()` serve `wwwroot/index.html` as the client.

---

## 2. Models — the data

### `Player.cs`

| Property | Type | Purpose |
|----------|------|---------|
| `ConnectionId` | `string` | SignalR's unique WebSocket ID (like Socket.IO's `socket.id`) |
| `Name` | `string` | Display name entered by the user |
| `Symbol` | `string?` | `"x"` or `"o"`, assigned on game join |
| `Game` | `Game?` | Back-reference to the game they're in |

### `Game.cs`

| Property | Type | Purpose |
|----------|------|---------|
| `Code` | `string` | Random 4-letter code (e.g. `"XKWM"`) |
| `Field` | `Dictionary<string, string?>` | Board: `c1`–`c9`, each `null` / `"x"` / `"o"` |
| `Players` | `List<Player>` | 0–2 players |
| `CurrentTurn` | `string` | `"x"` or `"o"`, starts as `"x"` |
| `Status` | `GameStatus` | `Waiting` → `Playing` → `Finished` |
| `Winner` | `string?` | Winning symbol or `"draw"` |
| `WinningCells` | `string[]?` | The 3 cells that won |

**Key methods:**

- **`AddPlayer()`** — assigns symbol (`x` to first, `o` to second), flips status to `Playing` when 2 players joined
- **`MakeTurn()`** — validates: game active? your turn? cell exists? cell empty? Then places mark, checks winner, switches turn
- **`CheckWinner()`** — loops 8 winning lines; if 3 match → win; if all 9 filled → draw

### `TurnResult` (record)

Factory methods: `Ok()`, `Fail(error)`, `Win(symbol, cells)`, `Draw()` — clean way to return multiple outcomes from one method.

---

## 3. Service — `GameService.cs`

Orchestration layer between the hub and models. Owns all state.

| Field | Purpose |
|-------|---------|
| `_players` | `Dictionary<ConnectionId, Player>` |
| `_games` | `List<Game>` |
| `_lock` | All operations are `lock`-ed (SignalR is multi-threaded) |

### Methods

**`AddPlayer(connectionId, name)`** — FIFO matchmaking:
1. Create a `Player`
2. Find any game with `Status == Waiting`
3. If none, create a new `Game` (unless `MaxConcurrentGames` reached)
4. Add player to game
5. Return whether game started (2 players now)

**`MakeTurn(connectionId, cellId)`** — look up player, delegate to `Game.MakeTurn()`, return result + opponent

**`RemovePlayer(connectionId)`** — remove from game, clean up empty games, return opponent for notification

---

## 4. Hub — `GameHub.cs`

Translates WebSocket messages ↔ service calls. Uses primary constructor DI.

### Client → Server

| Method | Trigger | Action |
|--------|---------|--------|
| `NewPlayer(name)` | Client clicks "Play" | Matchmake → send `WaitingForOpponent` or `PairPlayers` |
| `MakeTurn(cellId)` | Client clicks a cell | Validate → relay `OpponentTurn` or `GameOver` |

### Server → Client

| Event | Payload | When |
|-------|---------|------|
| `WaitingForOpponent` | `{ gameCode }` | First player matched, waiting |
| `PairPlayers` | `{ opponent, symbol, gameCode, yourTurn }` | Two players matched |
| `OpponentTurn` | `{ cellId }` | Opponent made a valid move |
| `GameOver` | `{ result, message, winningCells }` | Win / lose / draw |
| `OpponentDisconnected` | `{ message }` | Other player left |
| `ServerFull` | `{ message }` | Max games reached |
| `TurnError` | `{ error }` | Invalid move rejected |

### `OnDisconnectedAsync()`

Automatic SignalR override — fires on WebSocket drop. Cleans up player, notifies opponent.

---

## 5. Complete Game Flow

```mermaid
sequenceDiagram
    participant A as Alice (Browser)
    participant H as GameHub
    participant S as GameService
    participant G as Game
    participant B as Bob (Browser)

    A->>H: NewPlayer("Alice")
    H->>S: AddPlayer(connA, "Alice")
    S->>G: new Game() + AddPlayer(alice)
    Note over G: Status: Waiting
    H-->>A: WaitingForOpponent { gameCode }

    B->>H: NewPlayer("Bob")
    H->>S: AddPlayer(connB, "Bob")
    S->>G: AddPlayer(bob)
    Note over G: Status: Playing (full)
    H-->>A: PairPlayers { symbol:"x", yourTurn:true }
    H-->>B: PairPlayers { symbol:"o", yourTurn:false }

    A->>H: MakeTurn("c5")
    H->>S: MakeTurn(connA, "c5")
    S->>G: MakeTurn(alice, "c5")
    Note over G: Field[c5] = "x"<br/>CurrentTurn → "o"
    H-->>B: OpponentTurn { cellId:"c5" }

    B->>H: MakeTurn("c1")
    H->>S: MakeTurn(connB, "c1")
    S->>G: MakeTurn(bob, "c1")
    Note over G: Field[c1] = "o"<br/>CurrentTurn → "x"
    H-->>A: OpponentTurn { cellId:"c1" }

    Note over A,B: ... turns continue ...

    A->>H: MakeTurn("c9")
    H->>S: MakeTurn(connA, "c9")
    S->>G: MakeTurn(alice, "c9")
    Note over G: CheckWinner() → WIN!<br/>Status: Finished
    H-->>A: GameOver { result:"win", winningCells }
    H-->>B: GameOver { result:"lose", winningCells }
```

### Disconnect Flow

```mermaid
sequenceDiagram
    participant A as Alice
    participant H as GameHub
    participant S as GameService
    participant B as Bob

    Note over A: Browser closes / network drops
    H->>S: RemovePlayer(connA)
    S-->>H: returns Bob (opponent)
    H-->>B: OpponentDisconnected { message }
```

---

## Layer Responsibilities

```mermaid
graph TD
    CLIENT[wwwroot/index.html<br/>UI + SignalR JS client]
    HUB[Hubs/GameHub.cs<br/>Transport layer]
    SVC[Services/GameService.cs<br/>State + matchmaking]
    GAME[Models/Game.cs<br/>Board + rules]
    PLAYER[Models/Player.cs<br/>Player data]

    CLIENT <-->|WebSocket| HUB
    HUB --> SVC
    SVC --> GAME
    SVC --> PLAYER
    GAME --> PLAYER
```
