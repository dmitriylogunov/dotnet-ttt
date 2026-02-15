namespace TicTacToe.Models;

public abstract record GameEvent;
public record WaitingForOpponentEvent(string GameCode) : GameEvent;
public record GameStartedEvent(string OpponentName, string Symbol, bool YourTurn, string GameCode) : GameEvent;
public record OpponentMovedEvent(string CellId, string? ClearedCellId) : GameEvent;
public record YourMoveClearedEvent(string? ClearedCellId) : GameEvent;
public record TurnTimedOutEvent(bool YourTurnNow) : GameEvent;
public record GameOverEvent(string Result, string Message, string[]? WinningCells) : GameEvent;
public record OpponentDisconnectedEvent(string Message) : GameEvent;
public record ServerFullEvent(string Message) : GameEvent;
public record TurnErrorEvent(string Error) : GameEvent;
