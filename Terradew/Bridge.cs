using StardewValley;
using Terradew.Core.Protocol;

namespace Terradew;

/// <summary>The Stardew end of the socket. Holds the latest of everything Terraria sent.</summary>
internal sealed class Bridge : IDisposable
{
    private readonly Link link;
    private int seenConnection;
    private long worldAtMs;
    private WallFight? wall;
    private long wallAtMs;

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
    public WallFight? Wall => wall != null && Link.NowMs() - wallAtMs < 2500 ? wall : null;

    private VillagerMoves? moves;
    private long movesAtMs;

    private PlayerLook? look;
    private long lookAtMs;

    /// <summary>The Terraria player as Terraria draws it. Null once Terraria stops sending (it resends at least twice a second).</summary>
    public PlayerLook? Look => look != null && Link.NowMs() - lookAtMs < 1500 ? look : null;

    /// <summary>What busy villagers are doing about enemies, and today's danger. Null once Terraria stops sending.</summary>
    public VillagerMoves? Moves => moves != null && Link.NowMs() - movesAtMs < 3000 ? moves : null;

    public event Action? Connected;
    public event Action<NpcChat>? Chat;
    public event Action<PlayerDied>? Died;
    public event Action<CoinAck>? CoinAck;
    public event Action<Notice>? Notice;
    public event Action<StationPlaced>? StationPlaced;
    public event Action<GiftTaken>? GiftTaken;

    public bool IsConnected => link.Connected;

    /// <summary>Terraria is connected and sending world frames.</summary>
    public bool Linked => link.Connected && World != null && Link.NowMs() - worldAtMs < 2500;

    public string LastError => link.LastError;

    public bool Send(Message message) => link.Send(message);

    /// <summary>World positions are valid for the farmer's current place only when Terraria reports the same place.</summary>
    public bool WorldIsFor(string placeKey) => Linked && World != null && World.PlaceKey == placeKey;

    public void Pump()
    {
        CheckConnection();
        int budget = 400;
        while (budget-- > 0 && link.TryReceive(out Message message))
            Apply(message, raiseEvents: true);
    }

    /// <summary>
    /// Drains Terraria's messages without raising events. Used while the farmer is not in play, so stale
    /// chat, deaths, coin acks and notices never show up later. State messages still update.
    /// </summary>
    public void DropTransient()
    {
        CheckConnection();
        while (link.TryReceive(out Message message))
            Apply(message, raiseEvents: false);
    }

    private void CheckConnection()
    {
        if (link.ConnectionCount != seenConnection)
        {
            seenConnection = link.ConnectionCount;
            World = null;
            Inventory = null;
            Facts = null;
            Recipes = null;
            Shops.Clear();
            TerrariaHello = null;
            wall = null;
            Connected?.Invoke();
        }
    }

    private void Apply(Message message, bool raiseEvents)
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
                if (raiseEvents)
                    Chat?.Invoke(chat);
                break;
            case PlayerDied died:
                if (raiseEvents)
                    Died?.Invoke(died);
                break;
            case Core.Protocol.CoinAck ack:
                if (raiseEvents)
                    CoinAck?.Invoke(ack);
                break;
            case WallFight fight:
                wall = fight.Active ? fight : null;
                wallAtMs = Link.NowMs();
                break;
            case Core.Protocol.StationPlaced placed:
                // Always raised, even while transient messages are dropped: a station Terraria took must end up placed or refunded.
                StationPlaced?.Invoke(placed);
                break;
            case Core.Protocol.GiftTaken taken:
                // Always raised: Terraria already took the item, so the villager must still get it.
                GiftTaken?.Invoke(taken);
                break;
            case VillagerMoves moves:
                this.moves = moves;
                movesAtMs = Link.NowMs();
                break;
            case PlayerLook playerLook:
                look = playerLook;
                lookAtMs = Link.NowMs();
                break;
            case Core.Protocol.Notice notice:
                if (raiseEvents)
                    Notice?.Invoke(notice);
                break;
        }
    }

    public void Dispose() => link.Dispose();
}
