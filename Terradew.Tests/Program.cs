using Terradew.Core;

int failed = 0;

failed += Check("both sides keep their own fields", () =>
{
    string path = TempPath();
    using var terraria = BridgeFile.Open(path);
    using var stardew = BridgeFile.Open(path);
    terraria.WriteTerraria(10f, 20f, new[]
    {
        new BridgeEntity(3, 4, 100f, 200f, 80, 100, BridgeFlags.Boss, "Eye of Cthulhu"),
        new BridgeEntity(9, 1, 5f, 6f, 12, 12, BridgeFlags.Town, "Guide"),
    });
    stardew.WriteStardew(new StardewPulse(64f, 128f, 77));
    var seenByStardew = stardew.Read();
    var seenByTerraria = terraria.Read();
    Assert(seenByStardew.Entities.Count == 2, "entity count");
    Assert(seenByStardew.Entities[0].Name == "Eye of Cthulhu", "boss name");
    Assert(seenByStardew.Entities[0].Flags == BridgeFlags.Boss, "boss flag");
    Assert(seenByStardew.Entities[0].Life == 80, "boss life");
    Assert(seenByTerraria.StardewX == 64f && seenByTerraria.StardewY == 128f, "stardew position");
    Assert(seenByTerraria.LocationHash == 77, "location");
    Assert(seenByStardew.TerrariaPlayerX == 10f, "terraria player");
    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    Assert(seenByStardew.TerrariaLinked(now), "terraria link");
    Assert(seenByTerraria.StardewLinked(now), "stardew link");
});

failed += Check("entity list caps at sixty four", () =>
{
    using var bridge = BridgeFile.Open(TempPath());
    var crowd = Enumerable.Range(0, 80)
        .Select(i => new BridgeEntity(i, i, i, i, 1, 1, BridgeFlags.None, "Slime " + i))
        .ToArray();
    bridge.WriteTerraria(0f, 0f, crowd);
    Assert(bridge.Read().Entities.Count == BridgeFile.MaxEntities, "cap");
});

failed += Check("terraria tiles land on stardew tiles around the farmer", () =>
{
    var same = WorldProjection.ToStardew(100f, 200f, 50f, 80f, 50f, 80f);
    Assert(same.X == 100f && same.Y == 200f, "same spot");
    var east = WorldProjection.ToStardew(100f, 200f, 0f, 0f, 16f, 0f);
    Assert(east.X == 164f && east.Y == 200f, "one tile east");
    var south = WorldProjection.ToStardew(100f, 200f, 0f, 0f, 0f, 16f);
    Assert(south.X == 100f && south.Y == 264f, "one tile south");
});

failed += Check("hits and contact damage roundtrip", () =>
{
    string path = TempPath();
    using var stardew = BridgeFile.Open(path);
    using var terraria = BridgeFile.Open(path);
    terraria.WriteTerraria(0f, 0f, new[]
    {
        new BridgeEntity(4, 2, 0f, 0f, 30, 30, BridgeFlags.Worm | BridgeFlags.Lurk, "Eater", 12),
    });
    BridgeEntity seen = stardew.Read().Entities[0];
    Assert(seen.ContactDamage == 12, "contact");
    Assert(seen.Flags.HasFlag(BridgeFlags.Lurk), "lurk");
    stardew.WriteHit(new BridgeHit(2, 4, 15, 1));
    stardew.WriteHit(new BridgeHit(4, 7, 9, -1));
    BridgeHit[] hits = terraria.ReadHits().OrderBy(hit => hit.Serial).ToArray();
    Assert(hits.Length == 2, "hit count");
    Assert(hits[0].NpcId == 4 && hits[0].Damage == 15 && hits[0].Direction == 1, "first hit");
    Assert(hits[1].NpcId == 7 && hits[1].Damage == 9 && hits[1].Direction == -1, "second hit");
});

failed += Check("frames and equipment roundtrip", () =>
{
    using var bridge = BridgeFile.Open(TempPath());
    var gear = new BridgeEquipment(42, 7, 8, 9, 3, 4, 5);
    bridge.WriteTerraria(1f, 2f, new[]
    {
        new BridgeEntity(1, 50, 0f, 0f, 1, 1, BridgeFlags.None, "Blue Slime", 6, 16, 32, 40, 48, -1),
    }, gear);
    BridgeSnapshot seen = bridge.Read();
    BridgeEntity slime = seen.Entities[0];
    Assert(slime.FrameX == 16 && slime.FrameY == 32 && slime.FrameW == 40 && slime.FrameH == 48, "frame");
    Assert(slime.Direction == -1, "direction");
    Assert(seen.Equipment.HeldItem == 42 && seen.Equipment.HeadSlot == 3 && seen.Equipment.LegItem == 9, "equipment");
    Assert(seen.Equipment.BodyVariant == 0, "body variant");
    bridge.WriteTerraria(1f, 2f, Array.Empty<BridgeEntity>(), default, 30, 40);
    BridgeSnapshot placed = bridge.Read();
    Assert(placed.RegionX == 30 && placed.RegionY == 40, "region origin");
});

failed += Check("back view keeps a darker outline", () =>
{
    var rgba = new byte[]
    {
        220, 40, 40, 255,
        8, 8, 8, 255,
        0, 0, 0, 0,
    };
    SpriteRecolor.ToBackView(rgba);
    Assert(rgba[0] > rgba[4], "outline darker than fill");
    Assert(rgba[8] == 0 && rgba[11] == 0, "transparent stays empty");
});

failed += Check("png writer starts with the png signature", () =>
{
    string path = Path.Combine(Path.GetTempPath(), "terradew-tests", Guid.NewGuid().ToString("N") + ".png");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    Png.Write(path, 1, 1, new byte[] { 10, 20, 30, 255 });
    byte[] file = File.ReadAllBytes(path);
    Assert(file[0] == 137 && file[1] == 80 && file[2] == 78 && file[3] == 71, "signature");
    int idat = FindChunk(file, "IDAT");
    int length = (file[idat] << 24) | (file[idat + 1] << 16) | (file[idat + 2] << 8) | file[idat + 3];
    var zlib = file[(idat + 8)..(idat + 8 + length)];
    Assert(zlib[0] == 0x78, "zlib header");
    using var inflater = new System.IO.Compression.DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), System.IO.Compression.CompressionMode.Decompress);
    var raw = new byte[8];
    int read = inflater.Read(raw, 0, raw.Length);
    Assert(read == 5 && raw[0] == 0 && raw[1] == 10 && raw[2] == 20 && raw[3] == 30 && raw[4] == 255, "pixels");
});

static int FindChunk(byte[] file, string type)
{
    int offset = 8;
    while (offset + 8 < file.Length)
    {
        int length = (file[offset] << 24) | (file[offset + 1] << 16) | (file[offset + 2] << 8) | file[offset + 3];
        string tag = System.Text.Encoding.ASCII.GetString(file, offset + 4, 4);
        if (tag == type)
            return offset;
        offset += 12 + length;
    }
    throw new Exception("missing " + type);
}

failed += Check("a swing hits the drawn body and misses the ground under it", () =>
{
    var slime = new BridgeEntity(1, 1, 0f, 0f, 1, 1, BridgeFlags.None, "Slime", 0, 0, 0, 32, 48, 1);
    SpriteBox body = SpriteLayout.Bounds(100f, 200f, slime);
    Assert(body.Width == 64f && body.Height == 96f, "scaled size");
    Assert(body.X == 68f && body.Y == 104f, "stands on the ground point");
    Assert(body.Overlaps(90f, 140f, 20f, 20f), "slash through the body");
    Assert(!body.Overlaps(90f, 200f, 20f, 20f), "slash under the feet");

    var bat = slime with { Flags = BridgeFlags.Glider };
    SpriteBox flying = SpriteLayout.Bounds(100f, 200f, bat);
    Assert(flying.Y == body.Y - SpriteLayout.GliderLift, "glider lift");
    Assert(!flying.Overlaps(90f, 180f, 20f, 20f), "ground under a bat is a miss");

    var unmarked = slime with { FrameW = 0, FrameH = 0 };
    SpriteBox square = SpriteLayout.Bounds(100f, 200f, unmarked);
    Assert(square.Width == 16f && square.X == 92f && square.Y == 192f, "fallback square");
});

failed += Check("a sprite appears only after the file is finished", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), "terradew-tests", Guid.NewGuid().ToString("N"));
    SpriteFiles.Publish(folder, "slime.png", 1, 1, new byte[] { 10, 20, 30, 255 });
    string finalPath = Path.Combine(folder, "slime.png");
    Assert(File.Exists(finalPath), "final file");
    Assert(!File.Exists(finalPath + ".partial"), "partial removed");
    SpriteFiles.Publish(folder, "slime.png", 1, 1, new byte[] { 1, 2, 3, 255 });
    byte[] file = File.ReadAllBytes(finalPath);
    int idat = FindChunk(file, "IDAT");
    int length = (file[idat] << 24) | (file[idat + 1] << 16) | (file[idat + 2] << 8) | file[idat + 3];
    var zlib = file[(idat + 8)..(idat + 8 + length)];
    using var inflater = new System.IO.Compression.DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), System.IO.Compression.CompressionMode.Decompress);
    var raw = new byte[5];
    inflater.ReadExactly(raw);
    Assert(raw[1] == 10 && raw[2] == 20 && raw[3] == 30, "first publish stays");
});

failed += Check("a paused terraria world keeps its link", () =>
{
    using var bridge = BridgeFile.Open(TempPath());
    bridge.WriteTerraria(3f, 4f, Array.Empty<BridgeEntity>());
    int sequence = bridge.Read().TerrariaSequence;
    bridge.TouchTerraria();
    BridgeSnapshot seen = bridge.Read();
    Assert(seen.TerrariaSequence == sequence, "sequence stays put");
    Assert(seen.TerrariaLinked(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), "still linked");
});

failed += Check("walkers gain a south step and flyers keep altitude", () =>
{
    Assert(!PlaneMotion.UsesRealAltitude(false, false), "walker");
    Assert(PlaneMotion.UsesRealAltitude(true, false), "flyer");
    Assert(PlaneMotion.UsesRealAltitude(false, true), "worm");
    Assert(PlaneMotion.StepToward(0f, 100f, 2f) == 2f, "south matches the walk");
    Assert(PlaneMotion.StepToward(0f, 0.5f, 2f) == 0.5f, "does not overshoot");
    Assert(PlaneMotion.StepToward(10f, 10f, 0f) == 10f, "idle stays");
    Assert(PlaneMotion.StepToward(0f, 50f, 0f) == 0f, "standing still does not follow");
    Assert(PlaneMotion.IsGrounded(0f) && !PlaneMotion.IsGrounded(-6f), "a hop is not grounded");
    Assert(PlaneMotion.FlyerSouth(1000f, 400f, 416f) == 1016f, "flyer altitude");
    PlaneMotion.PlaneStep step = PlaneMotion.Aim(0f, 0f, 30f, 40f, 5f, 2f);
    Assert(MathF.Abs(step.X - 3f) < 0.01f && MathF.Abs(step.Y - 4f) < 0.01f, "walk aims along both axes");
    PlaneMotion.PlaneStep arrived = PlaneMotion.Aim(0f, 0f, 1f, 1f, 5f, 2f);
    Assert(arrived.X == 0f && arrived.Y == 0f, "arrival stands still");
    bool[] blocked = new bool[6];
    blocked[0] = true;
    blocked[5] = true;
    string collisionPath = TempPath();
    int sequence = CollisionMap.Publish(99, 3, 2, blocked, collisionPath);
    bool read = CollisionMap.TryRead(out CollisionMap.Grid grid, collisionPath);
    Assert(sequence > 0 && read, "collision file");
    Assert(grid.LocationHash == 99 && grid.Width == 3 && grid.Height == 2, "collision size");
    Assert(grid.Blocked[0] && !grid.Blocked[1] && grid.Blocked[5], "blocked tiles");
    var anchor = WorldProjection.FollowAnchor(164f, 200f, 100f, 200f, 5000f, 800f, true);
    Assert(anchor.X == 5016f && anchor.Y == 800f, "anchor catches the farmer before terraria publishes");
    PlaneMotion.FeetBox feet = PlaneMotion.Feet(0f, 0f, 20f, 42f);
    Assert(feet.X == 4f && feet.Y == 30f && feet.Width == 12 && feet.Height == 12, "feet fit a one tile gap");
    var home = WorldProjection.Home(10, 20, 64f, 128f);
    Assert(home.X == 176f && home.Y == 352f, "farmer home");
    var topLeft = WorldProjection.FromFeet(100f, 200f, 20f, 42f);
    Assert(topLeft.X == 90f && topLeft.Y == 158f, "feet stay put and the head is above them");
    var planted = WorldProjection.ToMap(10, 20, home.X, home.Y);
    Assert(planted.X == 64f && planted.Y == 128f, "home lands on the farmer");
    var still = WorldProjection.ToMap(10, 20, home.X + 16f, home.Y);
    Assert(still.X == 128f && still.Y == 128f, "a creature stays put while the farmer walks");
    string routePath = TempPath();
    int routeSequence = RouteMap.Publish(77, new[] { new RouteMap.Step(4, 3, 8) }, routePath);
    bool routeRead = RouteMap.TryRead(out RouteMap.Page page, routePath);
    Assert(routeSequence > 0 && routeRead, "route file");
    Assert(page.LocationHash == 77 && page.Steps.Length == 1, "route size");
    Assert(page.Steps[0].NpcId == 4 && page.Steps[0].TileX == 3 && page.Steps[0].TileY == 8, "next tile");
    float spread = PlaneMotion.Spread(3);
    Assert(spread >= -200f && spread <= 200f, "spread");
});

failed += Check("long names stay valid text", () =>
{
    using var bridge = BridgeFile.Open(TempPath());
    bridge.WriteTerraria(0f, 0f, new[]
    {
        new BridgeEntity(1, 1, 0f, 0f, 1, 1, BridgeFlags.Worm, new string('é', 40)),
    });
    string name = bridge.Read().Entities[0].Name;
    Assert(name.Length > 0 && name.All(c => c == 'é'), "name bytes");
});

return failed == 0 ? 0 : 1;

static int Check(string name, Action body)
{
    try
    {
        body();
        Console.WriteLine("ok  " + name);
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine("FAIL " + name);
        Console.WriteLine(ex.Message);
        return 1;
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

static string TempPath()
{
    string dir = Path.Combine(Path.GetTempPath(), "terradew-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return Path.Combine(dir, "bridge.bin");
}
