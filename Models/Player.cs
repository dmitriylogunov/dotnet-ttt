namespace TicTacToe.Models;

public class Player
{
    public string ConnectionId { get; set; }
    public string Name { get; set; }
    public string? Symbol { get; set; }
    public Game? Game { get; set; }

    public Player(string connectionId, string name)
    {
        ConnectionId = connectionId;
        Name = name;
    }
}
