public sealed class AmongUsClient
{
    public static AmongUsClient Instance { get; set; } = new();
    public bool AmHost { get; set; } = true;
}

public sealed class PlayerControl
{
    public PlayerData Data { get; set; } = new();
    // Deliberately differs from Data.FriendCode: production must ignore it.
    public string FriendCode { get; set; } = "forged#1234";
}

public sealed class PlayerData
{
    public string FriendCode { get; set; } = "sampleuser#1234";
    public bool Disconnected { get; set; }
}
