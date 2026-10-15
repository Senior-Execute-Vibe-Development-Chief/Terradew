using StardewValley;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>The Stardew end of the socket. Holds the latest of everything Terraria sent.</summary>
internal sealed class Bridge : IDisposable
{
    private readonly Link link;
    private int seenConnection;
    private long worldAtMs;

    public Bridge(int port)
    {
        link = Link.Connect(port);
    }

    public WorldState? World { get; private set; }
    public InventoryState? Inventory { get; private set; }
    public WorldFacts? Facts { get; private set; }
    public RecipeList? Recipes { get; private set; }
    public Dictionary<int, ShopStock> Shops { get; } = new();
    public Hello? TerrariaHello { get; private set; }
    /// <summary>The Wall of Flesh fight, while it runs. Null when there is none.</summary>
    public WallFight? Wall { get; private set; }

    public event Action? Connected;
    public event Action<NpcChat>? Chat;
    public event Action<PlayerDied>? Died;
    public event Action<CoinAck>? CoinAck;
    public event Action<Notice>? Notice;

    public bool IsConnected => link.Connected;

    /// <summary>Terraria is connected and sending world frames.</summary>
    public bool Linked => link.Connected && World != null && Link.NowMs() - worldAtMs < 2500;

    public string LastError => link.LastError;

    public bool Send(Message message) => link.Send(message);

    /// <summary>World positions are valid for the farmer's current place only when Terraria reports the same place.</summary>
    public bool WorldIsFor(string placeKey) => Linked && World != null && World.PlaceKey == placeKey;

    public void Pump()
    {
        if (link.ConnectionCount != seenConnection)
        {
            seenConnection = link.ConnectionCount;
            World = null;
            Inventory = null;
            TerrariaHello = null;
            Wall = null;
            Connected?.Invoke();
        }
        int budget = 400;
        while (budget-- > 0 && link.TryReceive(out Message message))
        {
            switch (message)
            {
                case Hello hello:
                    TerrariaHello = hello;
                    break;
                case WorldState state:
                    World = state;
                    worldAtMs = Link.NowMs();
                    break;
                case InventoryState inventory:
                    Inventory = inventory;
                    break;
                case WorldFacts facts:
                    Facts = facts;
                    break;
                case RecipeList recipes:
                    Recipes = recipes;
                    break;
                case ShopStock shop:
                    Shops[shop.NpcId] = shop;
                    break;
                case NpcChat chat:
                    Chat?.Invoke(chat);
                    break;
                case PlayerDied died:
                    Died?.Invoke(died);
                    break;
                case Core.Protocol.CoinAck ack:
                    CoinAck?.Invoke(ack);
                    break;
                case WallFight wall:
                    Wall = wall.Active ? wall : null;
                    break;
                case Core.Protocol.Notice notice:
                    Notice?.Invoke(notice);
                    break;
            }
        }
    }

    public void Dispose() => link.Dispose();
}
