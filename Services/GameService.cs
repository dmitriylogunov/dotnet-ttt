using TicTacToe.Models;

namespace TicTacToe.Services;

/// <summary>
/// Manages connected players, active games, and FIFO matchmaking.
/// Registered as a singleton — all state lives in memory.
/// Fires events so Blazor components can react to opponent actions.
/// </summary>
public class GameService
{
    private const int MaxConcurrentGames = 10;

    private readonly Dictionary<string, Player> _players = new();
    private readonly List<Game> _games = [];
    private readonly Dictionary<string, Timer> _turnTimers = new();
    private readonly object _lock = new();

    /// <summary>
    /// Subscribe to receive game events targeted at a specific player.
    /// The first parameter is the player ID, the second is the event.
    /// </summary>
    public event Action<string, GameEvent>? OnPlayerEvent;

    /// <summary>
    /// Register a new player and try to match them into a game.
    /// Fires WaitingForOpponentEvent or GameStartedEvent.
    /// </summary>
    public void JoinGame(string playerId, string name)
    {
        lock (_lock)
        {
            var player = new Player(playerId, name);
            _players[playerId] = player;

            var game = _games.FirstOrDefault(g => g.Status == GameStatus.Waiting);

            if (game is null)
            {
                if (_games.Count(g => g.Status != GameStatus.Finished) >= MaxConcurrentGames)
                {
                    _players.Remove(playerId);
                    Notify(playerId, new ServerFullEvent("Server is full. Try again later."));
                    return;
                }

                game = new Game();
                _games.Add(game);
            }

            game.AddPlayer(player);

            if (game.Status != GameStatus.Playing)
            {
                Notify(playerId, new WaitingForOpponentEvent(game.Code));
                return;
            }

            // Both players matched — notify each
            foreach (var p in game.Players)
            {
                var opponent = game.GetOpponent(p)!;
                Notify(p.ConnectionId, new GameStartedEvent(
                    opponent.Name, p.Symbol!, p.Symbol == game.CurrentTurn, game.Code));
            }

            // Start the turn timer
            StartTurnTimer(game);
        }
    }

    /// <summary>
    /// Process a player's move. Fires OpponentMovedEvent, GameOverEvent, or TurnErrorEvent.
    /// </summary>
    public void PlayTurn(string playerId, string cellId)
    {
        lock (_lock)
        {
            var player = GetPlayer(playerId);
            if (player?.Game is null)
            {
                Notify(playerId, new TurnErrorEvent("Not in a game."));
                return;
            }

            var game = player.Game;
            var opponent = game.GetOpponent(player);
            var result = game.MakeTurn(player, cellId);

            if (!result.Success)
            {
                Notify(playerId, new TurnErrorEvent(result.ErrorMessage!));
                return;
            }

            // Notify the current player about cleared cell (if any)
            if (result.ClearedCellId is not null)
                Notify(playerId, new YourMoveClearedEvent(result.ClearedCellId));

            // Relay move to opponent
            if (opponent is not null)
                Notify(opponent.ConnectionId, new OpponentMovedEvent(cellId, result.ClearedCellId));

            // Game over?
            if (result.WinnerSymbol is not null)
            {
                CancelTurnTimer(game);
                Notify(playerId, new GameOverEvent("win", "You win!", result.WinningCells));
                if (opponent is not null)
                    Notify(opponent.ConnectionId, new GameOverEvent("lose", "You lose!", result.WinningCells));
            }
            else if (result.IsDraw)
            {
                CancelTurnTimer(game);
                Notify(playerId, new GameOverEvent("draw", "It's a draw!", null));
                if (opponent is not null)
                    Notify(opponent.ConnectionId, new GameOverEvent("draw", "It's a draw!", null));
            }
            else
            {
                // Restart turn timer for the next player
                StartTurnTimer(game);
            }
        }
    }

    /// <summary>
    /// Handle a player leaving. Fires OpponentDisconnectedEvent to the opponent.
    /// </summary>
    public void RemovePlayer(string playerId)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(playerId, out var player))
                return;

            Player? opponent = null;
            var game = player.Game;

            if (game is not null)
            {
                CancelTurnTimer(game);
                opponent = game.GetOpponent(player);
                game.RemovePlayer(player);

                if (game.IsEmpty)
                    _games.Remove(game);
            }

            _players.Remove(playerId);

            if (opponent is not null)
                Notify(opponent.ConnectionId, new OpponentDisconnectedEvent("Your opponent disconnected."));
        }
    }

    // ── Turn timer management ──

    private void StartTurnTimer(Game game)
    {
        CancelTurnTimer(game);
        var gameCode = game.Code;
        var timer = new Timer(_ => OnTurnTimeout(gameCode), null,
            Game.TurnTimeoutSeconds * 1000, Timeout.Infinite);
        _turnTimers[gameCode] = timer;
    }

    private void CancelTurnTimer(Game game)
    {
        if (_turnTimers.Remove(game.Code, out var timer))
            timer.Dispose();
    }

    private void OnTurnTimeout(string gameCode)
    {
        lock (_lock)
        {
            var game = _games.FirstOrDefault(g => g.Code == gameCode);
            if (game is null || game.Status != GameStatus.Playing) return;

            // Identify current player and opponent before skipping
            var currentPlayer = game.Players.FirstOrDefault(p => p.Symbol == game.CurrentTurn);
            var opponent = currentPlayer is not null ? game.GetOpponent(currentPlayer) : null;

            game.SkipTurn();

            // Notify both players
            if (currentPlayer is not null)
                Notify(currentPlayer.ConnectionId, new TurnTimedOutEvent(false));
            if (opponent is not null)
                Notify(opponent.ConnectionId, new TurnTimedOutEvent(true));

            // Restart timer for the next player's turn
            StartTurnTimer(game);
        }
    }

    private Player? GetPlayer(string id) => _players.GetValueOrDefault(id);

    private void Notify(string playerId, GameEvent evt) => OnPlayerEvent?.Invoke(playerId, evt);
}
