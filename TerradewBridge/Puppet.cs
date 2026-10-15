using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terradew.Core;
using Terradew.Core.Combat;
using Terradew.Core.Economy;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// The Terraria player is the farmer's body. It stands on the farmer's feet every tick, uses the
/// Terraria hotbar item the farmer aims in Stardew, and takes Terraria's own damage.
/// </summary>
public sealed class Puppet : ModPlayer
{
    /// <summary>Ticks of immunity after the farmer changes place, so a snap never lands on a creature.</summary>
    private const int SnapGrace = 45;

    /// <summary>
    /// Stardew draws the player at half size, standing on the farmer's feet, so its middle is half as high as Terraria's.
    /// The Terraria body sits this much lower than the farmer's feet, putting its Center (where swings, shots and
    /// light start) on the middle of the picture.
    /// </summary>
    public const float BodyDrop = 42 / 2f * (1f - SpriteLayout.BodyScale);

    /// <summary>Ticks of immunity after a hit, about as long as Stardew's.</summary>
    private const int HurtImmunity = 60;

    /// <summary>The farmer's feet: the bottom middle of the farmer's body in Terraria pixels.</summary>
    public static Vector2 FeetOf(Player player)
        => new(player.position.X + player.width / 2f, player.position.Y + player.height - BodyDrop);

    /// <summary>Where Stardew aims, in Terraria pixels, while Stardew drives the player.</summary>
    public static bool TryAim(out Vector2 aim)
    {
        aim = default;
        if (!Driving(out FarmerPulse pulse, out WorldStamp.PlaceState place))
            return false;
        aim = place.ToWorld(pulse.AimX, pulse.AimY);
        return true;
    }

    private readonly HashSet<int> struckThisSwing = new();
    private int lastAnimation;

    /// <summary>Terraria's walk cycle runs over leg frames 7 to 19 of 56 pixels each.</summary>
    private const int FrameHeight = 56;
    private const int FirstWalkFrame = 7;
    private const int LastWalkFrame = 19;

    private Vector2 lastFeet;
    private float walkSpeed;
    private int stillTicks;
    private float walkCounter;
    /// <summary>Kept here: Terraria resets the leg frame to standing every tick, as it sees no velocity.</summary>
    private int walkFrame = FirstWalkFrame;

    /// <summary>Ticks Stardew has held the use button without a swing starting, and when that was last reported.</summary>
    private int stuckTicks;
    private uint lastStuckReport;

    internal static bool Driving(out FarmerPulse pulse, out WorldStamp.PlaceState place)
    {
        pulse = null!;
        place = null!;
        BridgeSystem bridge = BridgeSystem.Instance;
        if (!BridgeSystem.Active || !bridge.Linked || bridge.Pulse == null || bridge.Stamp.Current == null)
            return false;
        pulse = bridge.Pulse;
        place = bridge.Stamp.Current;
        return true;
    }

    public override void PreUpdate()
    {
        if (!BridgeSystem.Active)
            return;
        Player.gravity = 0f;
        Player.noKnockback = true;
        Player.breath = Player.breathMax;
        Place();
        Animate();
    }

    /// <summary>
    /// Gives Terraria a velocity that matches how the farmer moves, for this tick's update only (the player is put back
    /// on the farmer's feet after it). Terraria then plays the mount's run, the wings' flap and the jump pose itself.
    /// </summary>
    private void Animate()
    {
        bool airborne = Airborne;
        if (!Player.mount.Active && !airborne)
            return;
        float run = walkSpeed * (WorldProjection.Scale / SpriteLayout.DrawScale);
        Player.velocity = new Vector2(Player.direction * run, airborne ? -0.5f : 0f);
    }

    public override void SetControls()
    {
        if (!BridgeSystem.Active)
            return;
        Player.controlLeft = false;
        Player.controlRight = false;
        Player.controlUp = false;
        Player.controlDown = false;
        // Held while the farmer flies or leaps, so Terraria flaps the wings and fires the rocket boots.
        Player.controlJump = Airborne;
        Player.controlHook = false;
        Player.controlMount = false;
        Player.controlQuickHeal = false;
        Player.controlQuickMana = false;
        Player.controlThrow = false;
        Player.controlUseTile = false;
        // The Terraria window keeps its own mouse and keys unless the Stardew window is the focused one,
        // so Escape opens the inventory and Settings -> Save & Exit works. Main.hasFocus is not trusted
        // here: on some systems it stays true while the window is in the background.
        if (Main.gameMenu || !Driving(out FarmerPulse pulse, out _) || !pulse.Has(PulseFlags.Focused))
            return;
        // The hand itself is driven from PreItemCheck: this hook sits inside Terraria's input handling, which some
        // systems skip while the Terraria window is in the background.
        Player.controlUseItem = false;
    }

    /// <summary>
    /// Runs right before Terraria uses the held item, every tick, focused or not: selects Stardew's hotbar slot, aims
    /// at Stardew's cursor and holds the use button while Stardew does.
    /// </summary>
    public override bool PreItemCheck()
    {
        if (!BridgeSystem.Active || Main.gameMenu || Player.whoAmI != Main.myPlayer)
            return true;
        if (!Driving(out FarmerPulse pulse, out WorldStamp.PlaceState place) || !pulse.Has(PulseFlags.Focused))
            return true;
        bool hand = pulse.Has(PulseFlags.HandActive) && pulse.Has(PulseFlags.Free);
        if (hand && pulse.Has(PulseFlags.UseHeld))
        {
            // A Terraria inventory or chat left open would block the swing.
            Main.playerInventory = false;
            Main.npcChatText = "";
            Player.SetTalkNPC(-1);
        }
        if (hand && pulse.Slot < 10 && Player.itemAnimation == 0 && Inventory.PendingUse == 0)
            Player.selectedItem = pulse.Slot;
        Vector2 aim = place.ToWorld(pulse.AimX, pulse.AimY);
        Main.mouseX = (int)(aim.X - Main.screenPosition.X);
        Main.mouseY = (int)(aim.Y - Main.screenPosition.Y);
        Player.mouseInterface = false;
        Player.controlUseItem = hand && pulse.Has(PulseFlags.UseHeld);
        if (Inventory.PendingUse > 0)
            UsePending();
        return true;
    }

    /// <summary>
    /// Presses use once for the item used from the menu. When Terraria will not use it, such as a boss summon by day
    /// or with the boss already out, Stardew is told why it did nothing.
    /// </summary>
    private void UsePending()
    {
        if (Player.itemAnimation > 0)
        {
            // It started: once is enough.
            if (Player.selectedItem == Inventory.PendingSlot)
                Inventory.PendingUse = 0;
            return;
        }
        Player.selectedItem = Inventory.PendingSlot;
        Player.controlUseItem = true;
        Player.releaseUseItem = true;
        if (--Inventory.PendingUse > 0)
            return;
        Item item = Player.HeldItem;
        string name = item?.IsAir == false ? item.Name : "That item";
        string why = Main.dayTime ? " It is daytime in Terraria, and some items only work at night." : " Terraria would not use it now.";
        BridgeSystem.Instance.Mod.Logger.Info($"Terradew: {name} was used from the menu but did nothing (day {Main.dayTime}, time {Main.time:0}).");
        BridgeSystem.Instance.Notice($"{name} did nothing.{why}", 255, 200, 80);
    }

    public override void PostUpdate()
    {
        if (!BridgeSystem.Active)
            return;
        Place();
        Walk();
        Melee.PoseArm(Player);
        WatchForStuckUse();
        Player.breath = Player.breathMax;
        if (Player.dead)
        {
            Player.dead = false;
            Player.statLife = Math.Max(1, Player.statLife);
        }
    }

    /// <summary>
    /// Stardew holds the use button but no swing starts: says why, in Stardew and in the log, at most every ten seconds.
    /// </summary>
    private void WatchForStuckUse()
    {
        FarmerPulse? pulse = BridgeSystem.Instance.Pulse;
        if (pulse == null || !pulse.Has(PulseFlags.UseHeld) || Player.itemAnimation > 0)
        {
            stuckTicks = 0;
            return;
        }
        if (++stuckTicks != 30 || Main.GameUpdateCount - lastStuckReport < 600)
            return;
        lastStuckReport = Main.GameUpdateCount;
        string why = StuckReason(pulse);
        BridgeSystem.Instance.Mod.Logger.Info($"Terradew: Stardew holds the use button but the Terraria player does not swing: {why}");
        BridgeSystem.Instance.Notice($"Terraria did not swing: {why}", 255, 200, 80);
    }

    private string StuckReason(FarmerPulse pulse)
    {
        var reasons = new List<string>();
        if (!Driving(out _, out _))
            reasons.Add(BridgeSystem.Instance.Stamp.Current == null ? $"Terraria has no place for {pulse.PlaceKey}" : "the link to Stardew is stale");
        if (!pulse.Has(PulseFlags.Focused))
            reasons.Add("the Stardew window is not focused");
        if (!pulse.Has(PulseFlags.HandActive))
            reasons.Add("Stardew does not see a Terraria item in the hand");
        if (!pulse.Has(PulseFlags.Free))
            reasons.Add("the farmer is busy");
        if (pulse.Slot != Player.selectedItem)
            reasons.Add($"Terraria selected slot {Player.selectedItem + 1}, Stardew slot {pulse.Slot + 1}");
        Item held = Player.HeldItem;
        if (held == null || held.IsAir)
            reasons.Add("the Terraria hand is empty");
        else
        {
            if (held.useStyle == 0)
                reasons.Add($"{held.Name} has no use");
            else if (!CombinedHooks.CanUseItem(Player, held))
                reasons.Add($"{held.Name} cannot be used here");
        }
        if (!Main.mouseItem.IsAir)
            reasons.Add($"Terraria's cursor holds {Main.mouseItem.Name}");
        if (Main.gamePaused)
            reasons.Add("Terraria is paused");
        if (Player.CCed || Player.noItems)
            reasons.Add("the Terraria player cannot use items (a debuff)");
        if (Main.playerInventory || Player.talkNPC >= 0 || Main.ingameOptionsWindow || Main.inFancyUI)
            reasons.Add("a Terraria menu is open");
        if (!Player.ItemTimeIsZero || Player.reuseDelay > 0)
            reasons.Add($"waiting on item time {Player.itemTime}, reuse delay {Player.reuseDelay}");
        if (reasons.Count == 0)
            reasons.Add($"no blocker found (holding {held?.Name}, use style {held?.useStyle}, release {Player.releaseUseItem}, control {Player.controlUseItem})");
        return string.Join("; ", reasons);
    }

    private void Place()
    {
        BridgeSystem bridge = BridgeSystem.Instance;
        bool driving = Driving(out FarmerPulse pulse, out WorldStamp.PlaceState place);
        if (!driving || bridge.Stamp.TicksInPlace < SnapGrace)
        {
            Player.immune = true;
            Player.immuneTime = Math.Max(Player.immuneTime, 2);
        }
        if (!driving)
        {
            Player.velocity = Vector2.Zero;
            return;
        }
        Vector2 feet = place.ToWorld(pulse.FeetX, pulse.FeetY);
        (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y + BodyDrop, Player.width, Player.height);
        float east = x - Player.position.X;
        Player.position = new Vector2(x, y);
        Player.velocity = Vector2.Zero;
        Player.fallStart = (int)(Player.position.Y / 16f);
        if (Player.itemAnimation == 0)
        {
            if (east < -0.01f)
                Player.direction = -1;
            else if (east > 0.01f)
                Player.direction = 1;
            // Standing still, the player turns the way the farmer faces; facing north or south keeps the last side.
            else if (pulse.Facing == 1)
                Player.direction = 1;
            else if (pulse.Facing == 3)
                Player.direction = -1;
        }
    }

    /// <summary>
    /// The player is placed on the farmer's feet rather than moved, so Terraria sees no velocity and never plays the
    /// walk cycle. This plays it from how far the farmer walked, as Terraria does from walking speed. Stardew draws
    /// this player in place of the farmer, so walking north or south moves the legs too.
    /// </summary>
    private void Walk()
    {
        if (!Driving(out FarmerPulse pulse, out WorldStamp.PlaceState place))
            return;
        Vector2 feet = place.ToWorld(pulse.FeetX, pulse.FeetY);
        float moved = Vector2.Distance(feet, lastFeet);
        lastFeet = feet;
        if (moved > 0.05f && moved < 32f)
        {
            walkSpeed = moved;
            stillTicks = 0;
        }
        else if (++stillTicks > 4)
        {
            walkSpeed = 0f;
        }
        // Riding or in the air, Terraria poses the legs itself (sitting, or the jump pose) from the velocity Animate gave.
        if (Player.mount.Active || Airborne)
            return;
        if (walkSpeed <= 0f)
        {
            walkCounter = 0f;
            Player.legFrame.Y = 0;
            if (Player.itemAnimation == 0)
                Player.bodyFrame.Y = 0;
            return;
        }
        // Terraria counts leg frames from speed in its own pixels; the picture is drawn at half the map scale, so
        // the farmer's steps are twice as many picture pixels.
        walkCounter += walkSpeed * (WorldProjection.Scale / SpriteLayout.DrawScale) * 1.3f;
        while (walkCounter > 8f)
        {
            walkCounter -= 8f;
            walkFrame++;
        }
        if (walkFrame < FirstWalkFrame || walkFrame > LastWalkFrame)
            walkFrame = FirstWalkFrame;
        Player.legFrame.Y = walkFrame * FrameHeight;
        // The body bobs with the legs, as in Terraria, unless an item swing holds it.
        if (Player.itemAnimation == 0)
            Player.bodyFrame.Y = Player.legFrame.Y;
    }

    /// <summary>The picture Stardew draws is fully lit: Stardew lights it, not Terraria's caves and nights.</summary>
    public override void DrawEffects(PlayerDrawSet drawInfo, ref float r, ref float g, ref float b, ref float a, ref bool fullBright)
    {
        if (Body.Capturing)
            fullBright = true;
    }

    /// <summary>
    /// Terraria swings swords only left or right. Here the swing reaches whichever way the farmer aims,
    /// with the item's own damage, crit and knockback.
    /// </summary>
    public override void PostItemCheck()
    {
        if (!Driving(out FarmerPulse pulse, out WorldStamp.PlaceState place))
            return;
        Item item = Player.HeldItem;
        bool swinging = Player.itemAnimation > 0 && item != null && !item.IsAir && Melee.UsesArc(item);
        if (!swinging)
        {
            struckThisSwing.Clear();
            lastAnimation = 0;
            return;
        }
        if (Player.itemAnimation > lastAnimation)
            struckThisSwing.Clear();
        lastAnimation = Player.itemAnimation;

        Vector2 aim = place.ToWorld(pulse.AimX, pulse.AimY);
        float reach = (item.width + item.height) / 2f * Player.GetAdjustedItemScale(item) * 1.25f * SpriteLayout.BodyScale;
        MeleeArc.Area area = MeleeArc.Swing(Player.Center.X, Player.Center.Y, aim.X, aim.Y, reach);
        int damage = Player.GetWeaponDamage(item);
        float knockback = Player.GetWeaponKnockback(item, item.knockBack);
        int crit = Player.GetWeaponCrit(item);
        int direction = aim.X >= Player.Center.X ? 1 : -1;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC npc = Main.npc[i];
            if (!npc.active || npc.friendly || npc.dontTakeDamage || npc.townNPC || npc.immune[Player.whoAmI] > 0)
                continue;
            // Buried worm segments are drawn underground, so the swing passes over them.
            if (Creatures.IsBuried(npc))
                continue;
            if (struckThisSwing.Contains(i) || !area.Intersects(npc.position.X, npc.position.Y, npc.width, npc.height))
                continue;
            struckThisSwing.Add(i);
            bool critical = Main.rand.Next(100) < crit;
            npc.SimpleStrikeNPC(damage, direction, critical, knockback, item.DamageType);
            npc.immune[Player.whoAmI] = Player.itemAnimation;
        }
    }

    /// <summary>Terraria's own hit immunity is short for a top-down map; it lasts about as long as Stardew's.</summary>
    public override void PostHurt(Player.HurtInfo info)
    {
        if (!BridgeSystem.Active)
            return;
        Player.immune = true;
        Player.immuneTime = Math.Max(Player.immuneTime, HurtImmunity);
    }

    public override bool PreKill(double damage, int hitDirection, bool pvp, ref bool playSound, ref bool genDust, ref PlayerDeathReason damageSource)
    {
        if (!BridgeSystem.Active)
            return true;
        long coins = Inventory.Coins(Player);
        long lost = coins / 2;
        if (lost > 0)
            Player.BuyItem(lost);
        Player.statLife = Player.statLifeMax2;
        Player.immune = true;
        Player.immuneTime = 180;
        for (int i = 0; i < Player.buffType.Length; i++)
        {
            if (Player.buffType[i] > 0 && Main.debuff[Player.buffType[i]])
                Player.DelBuff(i--);
        }
        string reason = damageSource?.GetDeathText(Player.name)?.ToString() ?? $"{Player.name} was slain.";
        BridgeSystem.Instance.Send(new PlayerDied { CoinsLost = lost, Reason = reason });
        BridgeSystem.Instance.Notice($"{reason} Lost {Coins.Format(lost)}.", 255, 120, 120);
        return false;
    }

    /// <summary>Terraria's player body is bigger than the farmer; only hits on the farmer's own body count.</summary>
    private Rectangle FarmerBody()
    {
        int feetX = (int)(Player.position.X + Player.width / 2f);
        int feetY = (int)(Player.position.Y + Player.height - BodyDrop);
        return new Rectangle(feetX - SpriteLayout.FarmerWidth / 2, feetY - SpriteLayout.FarmerHeight, SpriteLayout.FarmerWidth, SpriteLayout.FarmerHeight);
    }

    /// <summary>
    /// A buried worm segment is underground, so it cannot touch the farmer, and walking creatures cannot reach a
    /// flying or jumping farmer. Flying creatures and shots still can.
    /// </summary>
    public override bool CanBeHitByNPC(NPC npc, ref int cooldownSlot)
        => !BridgeSystem.Active || (!Creatures.IsBuried(npc) && !(Airborne && !Creatures.IsFlyer(npc) && npc.aiStyle != NPCAIStyleID.Worm && !npc.boss) && npc.Hitbox.Intersects(FarmerBody()));

    /// <summary>Stardew says the farmer is in the air.</summary>
    private static bool Airborne => BridgeSystem.Instance.Pulse is { } pulse && pulse.Has(PulseFlags.Airborne);

    public override bool CanBeHitByProjectile(Projectile proj)
        => !BridgeSystem.Active || proj.Colliding(proj.Hitbox, FarmerBody());

    public static void HurtFromStardew(Player player, int amount)
    {
        if (amount <= 0 || player.immune)
            return;
        player.Hurt(PlayerDeathReason.ByCustomReason($"{player.name} had a bad day on the farm."), amount, 0);
    }
}

/// <summary>Turns off Terraria's left-or-right sword hitbox in the Terradew world; <see cref="Puppet"/> swings instead.</summary>
public sealed class Melee : GlobalItem
{
    /// <summary>Melee weapons and tools: their hits land along Stardew's aim instead of Terraria's left-or-right box.</summary>
    public static bool UsesArc(Item item)
        => item.damage > 0 && !item.noMelee && (item.useStyle == ItemUseStyleID.Swing || item.useStyle == ItemUseStyleID.Thrust);

    /// <summary>Swung items are drawn sweeping across the aim, in any direction.</summary>
    private static bool AimsSwing(Item item, Player player)
        => BridgeSystem.Active && player.whoAmI == Main.myPlayer && item.useStyle == ItemUseStyleID.Swing && !item.noUseGraphic;

    /// <summary>How far the swing has turned, as the angle the blade points along in screen terms (y down).</summary>
    private static bool TryBladeAngle(Item item, Player player, out float angle)
    {
        angle = 0f;
        if (!AimsSwing(item, player) || player.itemAnimationMax <= 0 || !Puppet.TryAim(out Vector2 aim))
            return false;
        Vector2 toward = aim - player.Center;
        float aimAngle = toward.LengthSquared() < 1f ? (player.direction == 1 ? 0f : MathF.PI) : MathF.Atan2(toward.Y, toward.X);
        float progress = 1f - Math.Clamp(player.itemAnimation / (float)player.itemAnimationMax, 0f, 1f);
        // From a third of a turn before the aim to a third past it, clockwise when facing right.
        float sweep = (progress - 0.5f) * (2f * MathF.PI / 3f);
        angle = aimAngle + (player.direction == 1 ? sweep : -sweep);
        return true;
    }

    public override void UseStyle(Item item, Player player, Rectangle heldItemFrame)
    {
        if (!TryBladeAngle(item, player, out float angle))
            return;
        // At rotation zero the sprite's blade points up and away from the handle: up-right facing right, up-left facing left.
        player.itemRotation = angle + (player.direction == 1 ? MathF.PI / 4f : 3f * MathF.PI / 4f);
        Vector2 along = new(MathF.Cos(angle), MathF.Sin(angle));
        player.itemLocation = player.Center + along * 4f;
    }

    /// <summary>Dust and grass cutting follow the drawn blade.</summary>
    public override void UseItemHitbox(Item item, Player player, ref Rectangle hitbox, ref bool noHitbox)
    {
        if (!TryBladeAngle(item, player, out float angle))
            return;
        float length = MathF.Sqrt(item.width * item.width + item.height * item.height) * player.GetAdjustedItemScale(item);
        Vector2 middle = player.itemLocation + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * length * 0.6f;
        hitbox.X = (int)(middle.X - hitbox.Width / 2f);
        hitbox.Y = (int)(middle.Y - hitbox.Height / 2f);
    }

    /// <summary>The arm follows the blade: raised, half raised, level or lowered.</summary>
    public static void PoseArm(Player player)
    {
        Item item = player.HeldItem;
        if (item == null || item.IsAir || player.itemAnimation <= 0 || !TryBladeAngle(item, player, out float angle))
            return;
        float down = MathF.Sin(angle);
        int frame = down < -0.75f ? 1 : down < -0.25f ? 2 : down < 0.45f ? 3 : 4;
        player.bodyFrame.Y = frame * player.bodyFrame.Height;
    }

    /// <summary>Held down in Stardew, a weapon or tool keeps swinging, as tools do in Terraria.</summary>
    public override bool? CanAutoReuseItem(Item item, Player player)
    {
        if (!BridgeSystem.Active || player.whoAmI != Main.myPlayer || !Puppet.TryAim(out _))
            return null;
        // Weapons of every kind (swords, guns, bows, staffs) and tools; potions, summons and the like stay one use a click.
        return item.damage > 0 || item.pick > 0 || item.axe > 0 || item.hammer > 0 ? true : null;
    }

    public override bool? CanHitNPC(Item item, Player player, NPC target)
    {
        if (!BridgeSystem.Active || !UsesArc(item))
            return null;
        BridgeSystem bridge = BridgeSystem.Instance;
        if (bridge.Linked && bridge.Stamp.Current != null)
            return false;
        return null;
    }

    /// <summary>Items float where they drop: there is no floor on a top-down map.</summary>
    public override void Update(Item item, ref float gravity, ref float maxFallSpeed)
    {
        if (!BridgeSystem.Active)
            return;
        gravity = 0f;
        maxFallSpeed = 0f;
        item.velocity *= 0.9f;
    }
}

/// <summary>Keeps arrows, bullets and thrown weapons flying straight instead of falling south.</summary>
public sealed class Flight : GlobalProjectile
{
    public override bool InstancePerEntity => true;

    private Vector2 before;
    private int fitWidth = -1;
    private int fitHeight = -1;

    public override bool PreAI(Projectile projectile)
    {
        before = projectile.velocity;
        return true;
    }

    public override void PostAI(Projectile projectile)
    {
        if (!BridgeSystem.Active)
            return;
        if (projectile.width != fitWidth || projectile.height != fitHeight)
        {
            // Hitboxes shrink to the drawn size, like creatures.
            Vector2 center = projectile.Center;
            projectile.width = SpriteLayout.Shrink(projectile.width);
            projectile.height = SpriteLayout.Shrink(projectile.height);
            fitWidth = projectile.width;
            fitHeight = projectile.height;
            projectile.Center = center;
        }
        // Hostile shots travel at the drawn scale, like flyers. The farmer's own shots keep Terraria's speed.
        if (projectile.hostile && !projectile.friendly)
            projectile.position -= projectile.velocity * (1f - SpriteLayout.BodyScale);
        // aiStyle 1 is arrows and bullets, 2 is thrown knives and shuriken.
        bool falls = projectile.aiStyle == 1 || projectile.aiStyle == 2;
        if (!falls)
            return;
        float pull = projectile.velocity.Y - before.Y;
        if (pull > 0f && pull < 0.6f)
            projectile.velocity.Y = before.Y;
    }
}
