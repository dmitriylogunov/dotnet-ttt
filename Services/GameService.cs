using TicTacToe.Models;

namespace TicTacToe.Services;

/// <summary>
/// Manages connected players, active games, and FIFO matchmaking.
/// Registered as a singleton — all state lives in memory.
/// </summary>
public class GameService
{
    private const int MaxConcurrentGames = 10;

    private readonly Dictionary<string, Player> _players = new();
    private readonly List<Game> _games = [];
    private readonly object _lock = new();

    /// <summary>
    /// Register a new player and try to match them into a game.
    /// Returns the game they were placed into.
    /// </summary>
    public (Game game, bool gameStarted) AddPlayer(string connectionId, string name)
    {
        lock (_lock)
        {
            var player = new Player(connectionId, name);
            _players[connectionId] = player;

            // Find a waiting game or create a new one
            var game = _games.FirstOrDefault(g => g.Status == GameStatus.Waiting);

            if (game is null)
            {
                if (_games.Count(g => g.Status != GameStatus.Finished) >= MaxConcurrentGames)
                    throw new InvalidOperationException("Server is full. Try again later.");

                game = new Game();
                _games.Add(game);
            }

            game.AddPlayer(player);
            return (game, game.Status == GameStatus.Playing);
        }
    }

    /// <summary>
    /// Process a player's move. Returns the result and the opponent.
    /// </summary>
    public (TurnResult result, Player? opponent) MakeTurn(string connectionId, string cellId)
    {
        lock (_lock)
        {
            var player = GetPlayer(connectionId);
            if (player?.Game is null)
                return (TurnResult.Fail("Not in a game."), null);

            var opponent = player.Game.GetOpponent(player);
            var result = player.Game.MakeTurn(player, cellId);
            return (result, opponent);
        }
    }

    /// <summary>
    /// Handle a player disconnecting. Returns the opponent to notify (if any).
    /// </summary>
    public Player? RemovePlayer(string connectionId)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(connectionId, out var player))
                return null;

            Player? opponent = null;
            var game = player.Game;

            if (game is not null)
            {
                opponent = game.GetOpponent(player);
                game.RemovePlayer(player);

                // Clean up empty games
                if (game.IsEmpty)
                    _games.Remove(game);
            }

            _players.Remove(connectionId);
            return opponent;
        }
    }

    public Player? GetPlayer(string connectionId) =>
        _players.GetValueOrDefault(connectionId);
}
