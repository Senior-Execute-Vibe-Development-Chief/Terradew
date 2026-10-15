using System;
using System.Collections.Generic;
using System.IO;

namespace Terradew.Core.Protocol;

public enum MsgType : byte
{
    // Either side.
    Hello = 1,

    // Stardew to Terraria.
    FarmerPulse = 2,
    MapLayout = 3,
    Routes = 4,
    AdjustCoins = 5,
    InventoryAction = 6,
    Craft = 7,
    NpcInteract = 8,
    Buy = 9,
    Sell = 10,
    NurseHeal = 11,
    GiveItem = 12,
    Hurt = 13,
    Heal = 14,
    SaveWorld = 15,
    Command = 16,
    DayStarted = 17,
    Homes = 18,
    RequestRecipes = 19,
    Gathered = 20,

    // Terraria to Stardew.
    WorldState = 40,
    InventoryState = 41,
    CoinAck = 42,
    RecipeList = 43,
    NpcChat = 44,
    ShopStock = 45,
    Notice = 46,
    PlayerDied = 47,
    WorldFacts = 48,
}

/// <summary>A message on the Terradew socket. Each type writes and reads its own payload.</summary>
public abstract class Message
{
    public abstract MsgType Type { get; }

    public abstract void Write(BinaryWriter w);

    public static Message Read(MsgType type, BinaryReader r)
    {
        return type switch
        {
            MsgType.Hello => Hello.ReadFrom(r),
            MsgType.FarmerPulse => FarmerPulse.ReadFrom(r),
            MsgType.MapLayout => MapLayout.ReadFrom(r),
            MsgType.Routes => Routes.ReadFrom(r),
            MsgType.AdjustCoins => AdjustCoins.ReadFrom(r),
            MsgType.InventoryAction => InventoryAction.ReadFrom(r),
            MsgType.Craft => Craft.ReadFrom(r),
            MsgType.NpcInteract => NpcInteract.ReadFrom(r),
            MsgType.Buy => Buy.ReadFrom(r),
            MsgType.Sell => Sell.ReadFrom(r),
            MsgType.NurseHeal => NurseHeal.ReadFrom(r),
            MsgType.GiveItem => GiveItem.ReadFrom(r),
            MsgType.Hurt => Hurt.ReadFrom(r),
            MsgType.Heal => Heal.ReadFrom(r),
            MsgType.SaveWorld => SaveWorld.ReadFrom(r),
            MsgType.Command => Command.ReadFrom(r),
            MsgType.DayStarted => DayStarted.ReadFrom(r),
            MsgType.Homes => Homes.ReadFrom(r),
            MsgType.RequestRecipes => new RequestRecipes(),
            MsgType.Gathered => Gathered.ReadFrom(r),
            MsgType.WorldState => WorldState.ReadFrom(r),
            MsgType.InventoryState => InventoryState.ReadFrom(r),
            MsgType.CoinAck => CoinAck.ReadFrom(r),
            MsgType.RecipeList => RecipeList.ReadFrom(r),
            MsgType.NpcChat => NpcChat.ReadFrom(r),
            MsgType.ShopStock => ShopStock.ReadFrom(r),
            MsgType.Notice => Notice.ReadFrom(r),
            MsgType.PlayerDied => PlayerDied.ReadFrom(r),
            MsgType.WorldFacts => WorldFacts.ReadFrom(r),
            _ => throw new InvalidDataException($"Unknown Terradew message {(int)type}."),
        };
    }
}

public enum Side : byte
{
    Stardew = 1,
    Terraria = 2,
}

public sealed class Hello : Message
{
    public const int CurrentProtocol = 2;
    public int Protocol = CurrentProtocol;
    public Side From;
    public string FarmId = "";
    public int TotalDays;
    /// <summary>Terraria only: the farm this Terraria world already belongs to ("" when new).</summary>
    public string WorldFarmId = "";
    /// <summary>Stardew only: the last coin change Terraria confirmed, so a restarted Terraria knows where to resume.</summary>
    public int CoinSeq;

    public override MsgType Type => MsgType.Hello;

    public override void Write(BinaryWriter w)
    {
        w.Write(Protocol);
        w.Write((byte)From);
        w.Write(FarmId);
        w.Write(TotalDays);
        w.Write(WorldFarmId);
        w.Write(CoinSeq);
    }

    public static Hello ReadFrom(BinaryReader r) => new()
    {
        Protocol = r.ReadInt32(),
        From = (Side)r.ReadByte(),
        FarmId = r.ReadString(),
        TotalDays = r.ReadInt32(),
        WorldFarmId = r.ReadString(),
        CoinSeq = r.ReadInt32(),
    };
}

[Flags]
public enum PulseFlags : byte
{
    None = 0,
    /// <summary>The farmer can act: no menu, no event, no warp in progress.</summary>
    Free = 1,
    /// <summary>The Terraria hotbar has the farmer's hand, not the Stardew toolbar.</summary>
    HandActive = 2,
    /// <summary>The use button is held down with the Terraria hand.</summary>
    UseHeld = 4,
    Swimming = 8,
    Sleeping = 16,
    /// <summary>The Stardew window has focus, so Stardew drives the Terraria body.</summary>
    Focused = 32,
}

/// <summary>Sent every Stardew tick. Positions are Stardew world pixels.</summary>
public sealed class FarmerPulse : Message
{
    public int Tick;
    public string PlaceKey = "";
    public float FeetX;
    public float FeetY;
    public byte Facing;
    public PulseFlags Flags;
    public byte Slot;
    public float AimX;
    public float AimY;
    public int TimeOfDay;
    public float TimeFraction;
    public int TotalDays;

    public override MsgType Type => MsgType.FarmerPulse;

    public bool Has(PulseFlags flag) => (Flags & flag) == flag;

    public override void Write(BinaryWriter w)
    {
        w.Write(Tick);
        w.Write(PlaceKey);
        w.Write(Wire.Finite(FeetX));
        w.Write(Wire.Finite(FeetY));
        w.Write(Facing);
        w.Write((byte)Flags);
        w.Write(Slot);
        w.Write(Wire.Finite(AimX));
        w.Write(Wire.Finite(AimY));
        w.Write(TimeOfDay);
        w.Write(Wire.Finite(TimeFraction));
        w.Write(TotalDays);
    }

    public static FarmerPulse ReadFrom(BinaryReader r) => new()
    {
        Tick = r.ReadInt32(),
        PlaceKey = r.ReadString(),
        FeetX = r.ReadSingle(),
        FeetY = r.ReadSingle(),
        Facing = r.ReadByte(),
        Flags = (PulseFlags)r.ReadByte(),
        Slot = r.ReadByte(),
        AimX = r.ReadSingle(),
        AimY = r.ReadSingle(),
        TimeOfDay = r.ReadInt32(),
        TimeFraction = r.ReadSingle(),
        TotalDays = r.ReadInt32(),
    };
}

/// <summary>What the farmer cannot walk through on the current map, plus the map's biome.</summary>
public sealed class MapLayout : Message
{
    public string PlaceKey = "";
    public int Width;
    public int Height;
    public byte Biome;
    public byte Band;
    public bool Spawns;
    public bool IsMineSlot;
    /// <summary>Changes whenever the map is rebuilt, such as a new mine floor in a reused slot.</summary>
    public int Version;
    public int WaterTiles;
    /// <summary>The Stardew mine level, or 0 outside the mines. A new floor clears the slot's old creatures.</summary>
    public int Floor;
    public bool[] Blocked = Array.Empty<bool>();

    public override MsgType Type => MsgType.MapLayout;

    public override void Write(BinaryWriter w)
    {
        w.Write(PlaceKey);
        w.Write(Width);
        w.Write(Height);
        w.Write(Biome);
        w.Write(Band);
        w.Write(Spawns);
        w.Write(IsMineSlot);
        w.Write(Version);
        w.Write(WaterTiles);
        w.Write(Floor);
        w.WriteBits(Blocked);
    }

    public static MapLayout ReadFrom(BinaryReader r) => new()
    {
        PlaceKey = r.ReadString(),
        Width = r.ReadInt32(),
        Height = r.ReadInt32(),
        Biome = r.ReadByte(),
        Band = r.ReadByte(),
        Spawns = r.ReadBoolean(),
        IsMineSlot = r.ReadBoolean(),
        Version = r.ReadInt32(),
        WaterTiles = r.ReadInt32(),
        Floor = r.ReadInt32(),
        Blocked = r.ReadBits(),
    };

    public bool IsBlocked(int x, int y)
        => x < 0 || y < 0 || x >= Width || y >= Height || Blocked.Length != Width * Height || Blocked[y * Width + x];
}

public readonly record struct RouteEntry(int NpcId, int TileX, int TileY);

/// <summary>The next open map tile for each ground chaser, from Stardew's path search.</summary>
public sealed class Routes : Message
{
    public string PlaceKey = "";
    public List<RouteEntry> Steps = new();

    public override MsgType Type => MsgType.Routes;

    public override void Write(BinaryWriter w)
    {
        w.Write(PlaceKey);
        w.WriteList(Steps, (x, s) => { x.Write(s.NpcId); x.Write(s.TileX); x.Write(s.TileY); });
    }

    public static Routes ReadFrom(BinaryReader r) => new()
    {
        PlaceKey = r.ReadString(),
        Steps = r.ReadList(x => new RouteEntry(x.ReadInt32(), x.ReadInt32(), x.ReadInt32()), 1000),
    };
}

/// <summary>Stardew changed the farmer's money (shipping, a Stardew shop). Copper.</summary>
public sealed class AdjustCoins : Message
{
    public int Seq;
    public long Delta;

    public override MsgType Type => MsgType.AdjustCoins;

    public override void Write(BinaryWriter w)
    {
        w.Write(Seq);
        w.Write(Delta);
    }

    public static AdjustCoins ReadFrom(BinaryReader r) => new() { Seq = r.ReadInt32(), Delta = r.ReadInt64() };
}

public enum InventoryKind : byte
{
    Swap = 1,
    Equip = 2,
    Unequip = 3,
    Trash = 4,
    Use = 5,
}

/// <summary>
/// Inventory slots: 0-49 main (0-9 hotbar), 50-53 coins, 54-57 ammo.
/// Equipment slots are 100 + Terraria armor index (0-2 armor, 3-9 accessories, 10-19 vanity).
/// </summary>
public sealed class InventoryAction : Message
{
    public const int EquipmentBase = 100;
    public InventoryKind Kind;
    public int A;
    public int B;

    public override MsgType Type => MsgType.InventoryAction;

    public override void Write(BinaryWriter w)
    {
        w.Write((byte)Kind);
        w.Write(A);
        w.Write(B);
    }

    public static InventoryAction ReadFrom(BinaryReader r) => new() { Kind = (InventoryKind)r.ReadByte(), A = r.ReadInt32(), B = r.ReadInt32() };
}

public sealed class Craft : Message
{
    public int RecipeIndex;
    public int Times = 1;

    public override MsgType Type => MsgType.Craft;

    public override void Write(BinaryWriter w)
    {
        w.Write(RecipeIndex);
        w.Write(Times);
    }

    public static Craft ReadFrom(BinaryReader r) => new() { RecipeIndex = r.ReadInt32(), Times = r.ReadInt32() };
}

public sealed class NpcInteract : Message
{
    public int NpcId;

    public override MsgType Type => MsgType.NpcInteract;

    public override void Write(BinaryWriter w) => w.Write(NpcId);

    public static NpcInteract ReadFrom(BinaryReader r) => new() { NpcId = r.ReadInt32() };
}

public sealed class Buy : Message
{
    public int NpcId;
    public int Index;

    public override MsgType Type => MsgType.Buy;

    public override void Write(BinaryWriter w)
    {
        w.Write(NpcId);
        w.Write(Index);
    }

    public static Buy ReadFrom(BinaryReader r) => new() { NpcId = r.ReadInt32(), Index = r.ReadInt32() };
}

public sealed class Sell : Message
{
    public int NpcId;
    public int Slot;

    public override MsgType Type => MsgType.Sell;

    public override void Write(BinaryWriter w)
    {
        w.Write(NpcId);
        w.Write(Slot);
    }

    public static Sell ReadFrom(BinaryReader r) => new() { NpcId = r.ReadInt32(), Slot = r.ReadInt32() };
}

public sealed class NurseHeal : Message
{
    public int NpcId;

    public override MsgType Type => MsgType.NurseHeal;

    public override void Write(BinaryWriter w) => w.Write(NpcId);

    public static NurseHeal ReadFrom(BinaryReader r) => new() { NpcId = r.ReadInt32() };
}

/// <summary>Give the farmer a Terraria item, by internal name (preferred) or numeric type.</summary>
public sealed class GiveItem : Message
{
    public string Name = "";
    public int ItemType;
    public int Stack = 1;

    public override MsgType Type => MsgType.GiveItem;

    public override void Write(BinaryWriter w)
    {
        w.Write(Name);
        w.Write(ItemType);
        w.Write(Stack);
    }

    public static GiveItem ReadFrom(BinaryReader r) => new() { Name = r.ReadString(), ItemType = r.ReadInt32(), Stack = r.ReadInt32() };
}

/// <summary>Damage the farmer took from something Stardew owns (a bomb, a trap).</summary>
public sealed class Hurt : Message
{
    public int Amount;

    public override MsgType Type => MsgType.Hurt;

    public override void Write(BinaryWriter w) => w.Write(Amount);

    public static Hurt ReadFrom(BinaryReader r) => new() { Amount = r.ReadInt32() };
}

/// <summary>Healing from Stardew food.</summary>
public sealed class Heal : Message
{
    public int Life;
    public int Mana;

    public override MsgType Type => MsgType.Heal;

    public override void Write(BinaryWriter w)
    {
        w.Write(Life);
        w.Write(Mana);
    }

    public static Heal ReadFrom(BinaryReader r) => new() { Life = r.ReadInt32(), Mana = r.ReadInt32() };
}

/// <summary>Stardew is saving. Terraria saves its world and character to match.</summary>
public sealed class SaveWorld : Message
{
    public string FarmId = "";
    public int TotalDays;

    public override MsgType Type => MsgType.SaveWorld;

    public override void Write(BinaryWriter w)
    {
        w.Write(FarmId);
        w.Write(TotalDays);
    }

    public static SaveWorld ReadFrom(BinaryReader r) => new() { FarmId = r.ReadString(), TotalDays = r.ReadInt32() };
}

public sealed class Command : Message
{
    public string Text = "";

    public override MsgType Type => MsgType.Command;

    public override void Write(BinaryWriter w) => w.Write(Text);

    public static Command ReadFrom(BinaryReader r) => new() { Text = r.ReadString() };
}

public sealed class DayStarted : Message
{
    public int TotalDays;

    public override MsgType Type => MsgType.DayStarted;

    public override void Write(BinaryWriter w) => w.Write(TotalDays);

    public static DayStarted ReadFrom(BinaryReader r) => new() { TotalDays = r.ReadInt32() };
}

/// <summary>A house a Terraria town NPC can live in: a built Terraria House on the farm.</summary>
public readonly record struct HomeEntry(string PlaceKey, int TileX, int TileY);

public sealed class Homes : Message
{
    public List<HomeEntry> Entries = new();

    public override MsgType Type => MsgType.Homes;

    public override void Write(BinaryWriter w)
        => w.WriteList(Entries, (x, e) => { x.Write(e.PlaceKey); x.Write(e.TileX); x.Write(e.TileY); });

    public static Homes ReadFrom(BinaryReader r) => new()
    {
        Entries = r.ReadList(x => new HomeEntry(x.ReadString(), x.ReadInt32(), x.ReadInt32()), 1000),
    };
}

public sealed class RequestRecipes : Message
{
    public override MsgType Type => MsgType.RequestRecipes;

    public override void Write(BinaryWriter w)
    {
    }
}

/// <summary>What the farmer broke or gathered in Stardew. Terraria rolls the Terraria materials it gives.</summary>
public enum GatherKind : byte
{
    Stone = 0,
    CopperNode = 1,
    IronNode = 2,
    GoldNode = 3,
    IridiumNode = 4,
    GemNode = 5,
    MysticStone = 6,
    Wood = 20,
    Hardwood = 21,
}

public sealed class Gathered : Message
{
    public GatherKind Kind;
    public string PlaceKey = "";
    public int MineLevel;
    public int Count = 1;

    public override MsgType Type => MsgType.Gathered;

    public override void Write(BinaryWriter w)
    {
        w.Write((byte)Kind);
        w.Write(PlaceKey);
        w.Write(MineLevel);
        w.Write(Count);
    }

    public static Gathered ReadFrom(BinaryReader r) => new()
    {
        Kind = (GatherKind)r.ReadByte(),
        PlaceKey = r.ReadString(),
        MineLevel = r.ReadInt32(),
        Count = r.ReadInt32(),
    };
}

[Flags]
public enum EntityFlags : ushort
{
    None = 0,
    Town = 1,
    Boss = 2,
    Worm = 4,
    Flyer = 8,
    /// <summary>Inside a stamped block, such as a burrowing worm. Drawn faded.</summary>
    Buried = 16,
    Friendly = 32,
    Hostile = 64,
    Chasing = 128,
}

/// <summary>A Terraria NPC. X and Y are its feet (bottom center) in Terraria world pixels.</summary>
public struct EntityState
{
    public int Id;
    public int NpcType;
    public float X;
    public float Y;
    public short Width;
    public short Height;
    public int Life;
    public int LifeMax;
    public EntityFlags Flags;
    public sbyte Direction;
    public short FrameX;
    public short FrameY;
    public short FrameW;
    public short FrameH;
    public float Rotation;
    public float Scale;
    public byte Alpha;
    public string Name;

    public bool Has(EntityFlags flag) => (Flags & flag) == flag;

    public void Write(BinaryWriter w)
    {
        w.Write(Id);
        w.Write(NpcType);
        w.Write(Wire.Finite(X));
        w.Write(Wire.Finite(Y));
        w.Write(Width);
        w.Write(Height);
        w.Write(Life);
        w.Write(LifeMax);
        w.Write((ushort)Flags);
        w.Write(Direction);
        w.Write(FrameX);
        w.Write(FrameY);
        w.Write(FrameW);
        w.Write(FrameH);
        w.Write(Wire.Finite(Rotation));
        w.Write(Wire.Finite(Scale));
        w.Write(Alpha);
        w.Write(Name ?? "");
    }

    public static EntityState ReadFrom(BinaryReader r) => new()
    {
        Id = r.ReadInt32(),
        NpcType = r.ReadInt32(),
        X = r.ReadSingle(),
        Y = r.ReadSingle(),
        Width = r.ReadInt16(),
        Height = r.ReadInt16(),
        Life = r.ReadInt32(),
        LifeMax = r.ReadInt32(),
        Flags = (EntityFlags)r.ReadUInt16(),
        Direction = r.ReadSByte(),
        FrameX = r.ReadInt16(),
        FrameY = r.ReadInt16(),
        FrameW = r.ReadInt16(),
        FrameH = r.ReadInt16(),
        Rotation = r.ReadSingle(),
        Scale = r.ReadSingle(),
        Alpha = r.ReadByte(),
        Name = r.ReadString(),
    };
}

/// <summary>A Terraria projectile. X and Y are its center in Terraria world pixels.</summary>
public struct ProjectileState
{
    public int Id;
    public int ProjType;
    public float X;
    public float Y;
    public float Rotation;
    public short Frame;
    public short FrameCount;
    public sbyte Direction;
    public byte Alpha;
    public float Scale;
    public bool Hostile;

    public void Write(BinaryWriter w)
    {
        w.Write(Id);
        w.Write(ProjType);
        w.Write(Wire.Finite(X));
        w.Write(Wire.Finite(Y));
        w.Write(Wire.Finite(Rotation));
        w.Write(Frame);
        w.Write(FrameCount);
        w.Write(Direction);
        w.Write(Alpha);
        w.Write(Wire.Finite(Scale));
        w.Write(Hostile);
    }

    public static ProjectileState ReadFrom(BinaryReader r) => new()
    {
        Id = r.ReadInt32(),
        ProjType = r.ReadInt32(),
        X = r.ReadSingle(),
        Y = r.ReadSingle(),
        Rotation = r.ReadSingle(),
        Frame = r.ReadInt16(),
        FrameCount = r.ReadInt16(),
        Direction = r.ReadSByte(),
        Alpha = r.ReadByte(),
        Scale = r.ReadSingle(),
        Hostile = r.ReadBoolean(),
    };
}

/// <summary>An item lying in the Terraria world. X and Y are its center in Terraria pixels.</summary>
public readonly record struct DropState(int Id, int ItemType, int Stack, float X, float Y);

public readonly record struct BuffState(int BuffType, int Time);

/// <summary>What the farmer's Terraria hand is doing this frame, for drawing the held item.</summary>
public struct HeldState
{
    public int ItemType;
    public int Animation;
    public int AnimationMax;
    public float Rotation;
    public sbyte Direction;
    public byte UseStyle;

    public void Write(BinaryWriter w)
    {
        w.Write(ItemType);
        w.Write(Animation);
        w.Write(AnimationMax);
        w.Write(Wire.Finite(Rotation));
        w.Write(Direction);
        w.Write(UseStyle);
    }

    public static HeldState ReadFrom(BinaryReader r) => new()
    {
        ItemType = r.ReadInt32(),
        Animation = r.ReadInt32(),
        AnimationMax = r.ReadInt32(),
        Rotation = r.ReadSingle(),
        Direction = r.ReadSByte(),
        UseStyle = r.ReadByte(),
    };
}

/// <summary>Sent every Terraria tick: the region the farmer is in and what is in it.</summary>
public sealed class WorldState : Message
{
    public int Tick;
    /// <summary>The Stardew place these positions belong to. Stardew ignores the frame if it is not the farmer's place.</summary>
    public string PlaceKey = "";
    public int RegionX;
    public int RegionY;
    public int Life;
    public int LifeMax;
    public int Mana;
    public int ManaMax;
    public int Defense;
    public bool Dead;
    public int Slot;
    public HeldState Held;
    public List<BuffState> Buffs = new();
    public List<EntityState> Entities = new();
    public List<ProjectileState> Projectiles = new();
    public List<DropState> Drops = new();

    public override MsgType Type => MsgType.WorldState;

    public override void Write(BinaryWriter w)
    {
        w.Write(Tick);
        w.Write(PlaceKey);
        w.Write(RegionX);
        w.Write(RegionY);
        w.Write(Life);
        w.Write(LifeMax);
        w.Write(Mana);
        w.Write(ManaMax);
        w.Write(Defense);
        w.Write(Dead);
        w.Write(Slot);
        Held.Write(w);
        w.WriteList(Buffs, (x, b) => { x.Write(b.BuffType); x.Write(b.Time); });
        w.WriteList(Entities, (x, e) => e.Write(x));
        w.WriteList(Projectiles, (x, p) => p.Write(x));
        w.WriteList(Drops, (x, d) => { x.Write(d.Id); x.Write(d.ItemType); x.Write(d.Stack); x.Write(Wire.Finite(d.X)); x.Write(Wire.Finite(d.Y)); });
    }

    public static WorldState ReadFrom(BinaryReader r)
    {
        var state = new WorldState
        {
            Tick = r.ReadInt32(),
            PlaceKey = r.ReadString(),
            RegionX = r.ReadInt32(),
            RegionY = r.ReadInt32(),
            Life = r.ReadInt32(),
            LifeMax = r.ReadInt32(),
            Mana = r.ReadInt32(),
            ManaMax = r.ReadInt32(),
            Defense = r.ReadInt32(),
            Dead = r.ReadBoolean(),
            Slot = r.ReadInt32(),
            Held = HeldState.ReadFrom(r),
        };
        state.Buffs = r.ReadList(x => new BuffState(x.ReadInt32(), x.ReadInt32()), 100);
        state.Entities = r.ReadList(EntityState.ReadFrom, 1000);
        state.Projectiles = r.ReadList(ProjectileState.ReadFrom, 2000);
        state.Drops = r.ReadList(x => new DropState(x.ReadInt32(), x.ReadInt32(), x.ReadInt32(), x.ReadSingle(), x.ReadSingle()), 1000);
        return state;
    }
}

public struct ItemStack
{
    public int ItemType;
    public int Stack;
    public int Prefix;
    public string Name;
    public int Damage;
    public bool Equippable;
    public bool Consumable;

    public bool IsEmpty => ItemType <= 0 || Stack <= 0;

    public void Write(BinaryWriter w)
    {
        w.Write(ItemType);
        w.Write(Stack);
        w.Write(Prefix);
        w.Write(Name ?? "");
        w.Write(Damage);
        w.Write(Equippable);
        w.Write(Consumable);
    }

    public static ItemStack ReadFrom(BinaryReader r) => new()
    {
        ItemType = r.ReadInt32(),
        Stack = r.ReadInt32(),
        Prefix = r.ReadInt32(),
        Name = r.ReadString(),
        Damage = r.ReadInt32(),
        Equippable = r.ReadBoolean(),
        Consumable = r.ReadBoolean(),
    };
}

/// <summary>The farmer's Terraria inventory and equipment. Sent when it changes.</summary>
public sealed class InventoryState : Message
{
    public const int InventorySlots = 58;
    public const int EquipmentSlots = 20;
    public List<ItemStack> Items = new();
    public List<ItemStack> Equipment = new();
    public long Coins;
    /// <summary>The armor sprite slots the character shows (vanity wins over worn armor), for drawing over the farmer.</summary>
    public int HeadSlot;
    public int BodySlot;
    public int LegSlot;
    public bool Male = true;

    public override MsgType Type => MsgType.InventoryState;

    public override void Write(BinaryWriter w)
    {
        w.WriteList(Items, (x, i) => i.Write(x));
        w.WriteList(Equipment, (x, i) => i.Write(x));
        w.Write(Coins);
        w.Write(HeadSlot);
        w.Write(BodySlot);
        w.Write(LegSlot);
        w.Write(Male);
    }

    public static InventoryState ReadFrom(BinaryReader r) => new()
    {
        Items = r.ReadList(ItemStack.ReadFrom, 200),
        Equipment = r.ReadList(ItemStack.ReadFrom, 100),
        Coins = r.ReadInt64(),
        HeadSlot = r.ReadInt32(),
        BodySlot = r.ReadInt32(),
        LegSlot = r.ReadInt32(),
        Male = r.ReadBoolean(),
    };
}

/// <summary>Terraria applied every AdjustCoins up to Seq. Total is the coin count after that, in copper.</summary>
public sealed class CoinAck : Message
{
    public int Seq;
    public long Total;

    public override MsgType Type => MsgType.CoinAck;

    public override void Write(BinaryWriter w)
    {
        w.Write(Seq);
        w.Write(Total);
    }

    public static CoinAck ReadFrom(BinaryReader r) => new() { Seq = r.ReadInt32(), Total = r.ReadInt64() };
}

public struct RecipeEntry
{
    public int RecipeIndex;
    public ItemStack Result;
    public List<ItemStack> Ingredients;
    public bool CanCraft;

    public void Write(BinaryWriter w)
    {
        w.Write(RecipeIndex);
        Result.Write(w);
        w.WriteList(Ingredients ?? new List<ItemStack>(), (x, i) => i.Write(x));
        w.Write(CanCraft);
    }

    public static RecipeEntry ReadFrom(BinaryReader r) => new()
    {
        RecipeIndex = r.ReadInt32(),
        Result = ItemStack.ReadFrom(r),
        Ingredients = r.ReadList(ItemStack.ReadFrom, 50),
        CanCraft = r.ReadBoolean(),
    };
}

public sealed class RecipeList : Message
{
    public List<RecipeEntry> Recipes = new();
    public string Stations = "";

    public override MsgType Type => MsgType.RecipeList;

    public override void Write(BinaryWriter w)
    {
        w.WriteList(Recipes, (x, e) => e.Write(x));
        w.Write(Stations);
    }

    public static RecipeList ReadFrom(BinaryReader r) => new()
    {
        Recipes = r.ReadList(RecipeEntry.ReadFrom, 20000),
        Stations = r.ReadString(),
    };
}

public sealed class NpcChat : Message
{
    public int NpcId;
    public int NpcType;
    public string Name = "";
    public string Text = "";
    public bool HasShop;
    public bool IsNurse;
    public long HealCost;

    public override MsgType Type => MsgType.NpcChat;

    public override void Write(BinaryWriter w)
    {
        w.Write(NpcId);
        w.Write(NpcType);
        w.Write(Name);
        w.Write(Text);
        w.Write(HasShop);
        w.Write(IsNurse);
        w.Write(HealCost);
    }

    public static NpcChat ReadFrom(BinaryReader r) => new()
    {
        NpcId = r.ReadInt32(),
        NpcType = r.ReadInt32(),
        Name = r.ReadString(),
        Text = r.ReadString(),
        HasShop = r.ReadBoolean(),
        IsNurse = r.ReadBoolean(),
        HealCost = r.ReadInt64(),
    };
}

public readonly record struct ShopEntry(ItemStack Item, long Price);

public sealed class ShopStock : Message
{
    public int NpcId;
    public string Name = "";
    public List<ShopEntry> Entries = new();

    public override MsgType Type => MsgType.ShopStock;

    public override void Write(BinaryWriter w)
    {
        w.Write(NpcId);
        w.Write(Name);
        w.WriteList(Entries, (x, e) => { e.Item.Write(x); x.Write(e.Price); });
    }

    public static ShopStock ReadFrom(BinaryReader r) => new()
    {
        NpcId = r.ReadInt32(),
        Name = r.ReadString(),
        Entries = r.ReadList(x => new ShopEntry(ItemStack.ReadFrom(x), x.ReadInt64()), 500),
    };
}

public sealed class Notice : Message
{
    public string Text = "";
    public byte R = 255;
    public byte G = 255;
    public byte B = 255;

    public override MsgType Type => MsgType.Notice;

    public override void Write(BinaryWriter w)
    {
        w.Write(Text);
        w.Write(R);
        w.Write(G);
        w.Write(B);
    }

    public static Notice ReadFrom(BinaryReader r) => new() { Text = r.ReadString(), R = r.ReadByte(), G = r.ReadByte(), B = r.ReadByte() };
}

public sealed class PlayerDied : Message
{
    public long CoinsLost;
    public string Reason = "";

    public override MsgType Type => MsgType.PlayerDied;

    public override void Write(BinaryWriter w)
    {
        w.Write(CoinsLost);
        w.Write(Reason);
    }

    public static PlayerDied ReadFrom(BinaryReader r) => new() { CoinsLost = r.ReadInt64(), Reason = r.ReadString() };
}

public readonly record struct TownResident(int NpcType, string Name, string HomePlaceKey);

/// <summary>Progress Stardew needs to know about: hardmode, bosses beaten, who lives where.</summary>
public sealed class WorldFacts : Message
{
    public bool Hardmode;
    public bool DownedEye;
    public bool DownedEvilBoss;
    public bool DownedSkeletron;
    public bool DownedQueenBee;
    public bool DownedWall;
    public List<TownResident> Residents = new();

    public override MsgType Type => MsgType.WorldFacts;

    public override void Write(BinaryWriter w)
    {
        w.Write(Hardmode);
        w.Write(DownedEye);
        w.Write(DownedEvilBoss);
        w.Write(DownedSkeletron);
        w.Write(DownedQueenBee);
        w.Write(DownedWall);
        w.WriteList(Residents, (x, e) => { x.Write(e.NpcType); x.Write(e.Name); x.Write(e.HomePlaceKey); });
    }

    public static WorldFacts ReadFrom(BinaryReader r) => new()
    {
        Hardmode = r.ReadBoolean(),
        DownedEye = r.ReadBoolean(),
        DownedEvilBoss = r.ReadBoolean(),
        DownedSkeletron = r.ReadBoolean(),
        DownedQueenBee = r.ReadBoolean(),
        DownedWall = r.ReadBoolean(),
        Residents = r.ReadList(x => new TownResident(x.ReadInt32(), x.ReadString(), x.ReadString()), 200),
    };
}
