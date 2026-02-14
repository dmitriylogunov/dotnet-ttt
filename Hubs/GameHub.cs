using Microsoft.AspNetCore.SignalR;
using TicTacToe.Services;

namespace TicTacToe.Hubs;

public class GameHub(GameService gameService) : Hub
{
    /// <summary>
    /// Client calls this after connecting with their chosen name.
    /// Matches them into a game (FIFO). When two players are paired,
    /// both receive "PairPlayers" with opponent info and symbols.
    /// </summary>
    public async Task NewPlayer(string name)
    {
        try
        {
            var (game, gameStarted) = gameService.AddPlayer(Context.ConnectionId, name);

            if (!gameStarted)
            {
                // First player — waiting for an opponent
                await Clients.Caller.SendAsync("WaitingForOpponent", new { gameCode = game.Code });
                return;
            }

            // Two players matched — notify both
            foreach (var player in game.Players)
            {
                var opponent = game.GetOpponent(player)!;
                await Clients.Client(player.ConnectionId).SendAsync("PairPlayers", new
                {
                    opponent = new { name = opponent.Name },
                    symbol = player.Symbol,
                    gameCode = game.Code,
                    yourTurn = player.Symbol == game.CurrentTurn
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            await Clients.Caller.SendAsync("ServerFull", new { message = ex.Message });
        }
    }

    /// <summary>
    /// Client clicks a cell. Server validates, then relays to opponent.
    /// On game over, both players receive the result.
    /// </summary>
    public async Task MakeTurn(string cellId)
    {
        var (result, opponent) = gameService.MakeTurn(Context.ConnectionId, cellId);

        if (!result.Success)
        {
            await Clients.Caller.SendAsync("TurnError", new { error = result.ErrorMessage });
            return;
        }

        // Relay the move to the opponent
        if (opponent is not null)
        {
            await Clients.Client(opponent.ConnectionId).SendAsync("OpponentTurn", new { cellId });
        }

        // Check for game over
        if (result.WinnerSymbol is not null)
        {
            var player = gameService.GetPlayer(Context.ConnectionId)!;
            await Clients.Caller.SendAsync("GameOver", new
            {
                result = "win",
                message = "You win!",
                winningCells = result.WinningCells
            });

            if (opponent is not null)
            {
                await Clients.Client(opponent.ConnectionId).SendAsync("GameOver", new
                {
                    result = "lose",
                    message = "You lose!",
                    winningCells = result.WinningCells
                });
            }
        }
        else if (result.IsDraw)
        {
            await Clients.Caller.SendAsync("GameOver", new
            {
                result = "draw",
                message = "It's a draw!",
                winningCells = (string[]?)null
            });

            if (opponent is not null)
            {
                await Clients.Client(opponent.ConnectionId).SendAsync("GameOver", new
                {
                    result = "draw",
                    message = "It's a draw!",
                    winningCells = (string[]?)null
                });
            }
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var opponent = gameService.RemovePlayer(Context.ConnectionId);

        if (opponent is not null)
        {
            await Clients.Client(opponent.ConnectionId).SendAsync("OpponentDisconnected", new
            {
                message = "Your opponent disconnected."
            });
        }

        await base.OnDisconnectedAsync(exception);
    }
}
