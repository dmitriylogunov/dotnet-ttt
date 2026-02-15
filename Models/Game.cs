namespace TicTacToe.Models;

public class Game
{
    public const int MaxMovesOnBoard = 4;
    public const int TurnTimeoutSeconds = 4;

    private static readonly string[][] WinSets =
    [
        ["c1", "c2", "c3"], ["c4", "c5", "c6"], ["c7", "c8", "c9"], // rows
        ["c1", "c4", "c7"], ["c2", "c5", "c8"], ["c3", "c6", "c9"], // cols
        ["c1", "c5", "c9"], ["c3", "c5", "c7"]                      // diagonals
    ];

    private readonly List<string> _xMoveHistory = [];
    private readonly List<string> _oMoveHistory = [];

    public string Code { get; } = GenerateCode();
    public Dictionary<string, string?> Field { get; } = new()
    {
        ["c1"] = null,
        ["c2"] = null,
        ["c3"] = null,
        ["c4"] = null,
        ["c5"] = null,
        ["c6"] = null,
        ["c7"] = null,
        ["c8"] = null,
        ["c9"] = null
    };

    public List<Player> Players { get; } = [];
    public string CurrentTurn { get; private set; } = "x";
    public GameStatus Status { get; private set; } = GameStatus.Waiting;
    public string? Winner { get; private set; }
    public string[]? WinningCells { get; private set; }

    public bool IsFull => Players.Count >= 2;
    public bool IsEmpty => Players.Count == 0;

    public void AddPlayer(Player player)
    {
        if (IsFull)
            throw new InvalidOperationException("Game is full.");

        player.Symbol = Players.Count == 0 ? "x" : "o";
        player.Game = this;
        Players.Add(player);

        if (IsFull)
            Status = GameStatus.Playing;
    }

    public void RemovePlayer(Player player)
    {
        Players.Remove(player);
        player.Game = null;
        player.Symbol = null;
    }

    public Player? GetOpponent(Player player) =>
        Players.FirstOrDefault(p => p.ConnectionId != player.ConnectionId);

    public TurnResult MakeTurn(Player player, string cellId)
    {
        if (Status != GameStatus.Playing)
            return TurnResult.Fail("Game is not in progress.");

        if (player.Symbol != CurrentTurn)
            return TurnResult.Fail("Not your turn.");

        if (!Field.ContainsKey(cellId))
            return TurnResult.Fail("Invalid cell.");

        if (Field[cellId] is not null)
            return TurnResult.Fail("Cell already taken.");

        // Place the move and track it per player
        Field[cellId] = player.Symbol;
        var history = player.Symbol == "x" ? _xMoveHistory : _oMoveHistory;
        history.Add(cellId);

        // Remove the oldest move of THIS player if they exceed the limit
        string? clearedCellId = null;
        if (history.Count > MaxMovesOnBoard)
        {
            clearedCellId = history[0];
            Field[clearedCellId] = null;
            history.RemoveAt(0);
        }

        var result = CheckWinner();

        if (result is not null)
        {
            Status = GameStatus.Finished;
            return result with { ClearedCellId = clearedCellId };
        }

        // Switch turn
        CurrentTurn = CurrentTurn == "x" ? "o" : "x";
        return TurnResult.Ok(clearedCellId);
    }

    /// <summary>
    /// Skip the current player's turn (called on timeout). No piece is placed.
    /// </summary>
    public void SkipTurn()
    {
        if (Status != GameStatus.Playing) return;
        CurrentTurn = CurrentTurn == "x" ? "o" : "x";
    }

    private TurnResult? CheckWinner()
    {
        foreach (var set in WinSets)
        {
            var a = Field[set[0]];
            var b = Field[set[1]];
            var c = Field[set[2]];

            if (a is not null && a == b && b == c)
            {
                Winner = a;
                WinningCells = set;
                return TurnResult.Win(a, set);
            }
        }

        // Check draw — all cells filled, no winner
        if (Field.Values.All(v => v is not null))
        {
            Winner = "draw";
            return TurnResult.Draw();
        }

        return null;
    }

    private static string GenerateCode()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var random = Random.Shared;
        return new string(Enumerable.Range(0, 4)
            .Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }
}

public enum GameStatus { Waiting, Playing, Finished }

public record TurnResult(bool Success, string? ErrorMessage, string? WinnerSymbol, string[]? WinningCells, bool IsDraw, string? ClearedCellId = null)
{
    public static TurnResult Ok(string? clearedCellId = null) => new(true, null, null, null, false, clearedCellId);
    public static TurnResult Fail(string error) => new(false, error, null, null, false);
    public static TurnResult Win(string symbol, string[] cells) => new(true, null, symbol, cells, false);
    public static TurnResult Draw() => new(true, null, null, null, true);
}
