# Tic-Tac-Toe — Multiplayer with .NET & SignalR

A real-time multiplayer Tic-Tac-Toe web app built with **ASP.NET Core** and **SignalR**.

## Tech Stack

- **ASP.NET Core** (.NET 10)
- **SignalR** (WebSocket real-time communication)
- **Vanilla HTML/JS** client (single-page, no framework)

## Project Structure

| Path                      | Role                                                           |
| ------------------------- | -------------------------------------------------------------- |
| `Models/Game.cs`          | Board state, turn validation, win/draw detection               |
| `Models/Player.cs`        | Connected player model (connection ID, name, symbol)           |
| `Services/GameService.cs` | Singleton service — matchmaking, game state, player management |
| `Hubs/GameHub.cs`         | SignalR hub — real-time event handling                         |
| `wwwroot/index.html`      | Client — menu, name input, game board                          |
| `Program.cs`              | App entry point, DI wiring                                     |

## Multiplayer Flow

```
CLIENT (Browser + SignalR JS)              SERVER (ASP.NET Core + SignalR)
═════════════════════════════              ════════════════════════════════

1. Enter name → invoke NewPlayer ────────→ GameService.AddPlayer()
                                           FIFO matchmaking: join waiting
                                           game or create a new one

2. Waiting...  ←──── WaitingForOpponent    First player waits

3. Game starts ←──── PairPlayers           Second player joins → both get
                     { opponent, symbol,   opponent info, assigned symbols
                       gameCode, yourTurn} (x goes first)

4. Click cell → invoke MakeTurn ──────────→ Game.MakeTurn() validates:
                                            - correct player's turn
                                            - valid & empty cell
                                            - checks win/draw

5. Board update ←──── OpponentTurn         Valid move relayed to opponent
                      { cellId }

6. Game over   ←──── GameOver              Server detects win or draw →
                     { result, message,    notifies both players with
                       winningCells }      result (win/lose/draw)

7. Disconnect  ←──── OpponentDisconnected  Player leaves → opponent
                                           notified, game cleaned up
```

## SignalR Events

### Client → Server (Hub Methods)

| Method      | Payload         | Description                        |
| ----------- | --------------- | ---------------------------------- |
| `NewPlayer` | `string name`   | Register player, enter matchmaking |
| `MakeTurn`  | `string cellId` | Place a mark (`"c1"`–`"c9"`)       |

### Server → Client (Callbacks)

| Event                  | Payload                                    | When                           |
| ---------------------- | ------------------------------------------ | ------------------------------ |
| `WaitingForOpponent`   | `{ gameCode }`                             | First player waiting for match |
| `PairPlayers`          | `{ opponent, symbol, gameCode, yourTurn }` | Two players matched            |
| `OpponentTurn`         | `{ cellId }`                               | Opponent made a valid move     |
| `GameOver`             | `{ result, message, winningCells }`        | Game finished (win/lose/draw)  |
| `OpponentDisconnected` | `{ message }`                              | Other player left              |
| `ServerFull`           | `{ message }`                              | No room for new games          |
| `TurnError`            | `{ error }`                                | Invalid move rejected          |

## Game Rules

- Board is a 3×3 grid (`c1`–`c9`)
- First player gets `x`, second gets `o`
- `x` always goes first
- Server validates every move and detects the winner
- All state is in-memory (resets on server restart)
- Max 10 concurrent games (configurable in `GameService`)

## Run

```bash
dotnet run
```

Open two browser tabs to `http://localhost:5000` to play.
