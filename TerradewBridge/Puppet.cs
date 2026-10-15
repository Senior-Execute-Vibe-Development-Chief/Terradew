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

    private readonly HashSet<int> struckThisSwing = new();
    private int lastAnimation;

    private static bool Driving(out FarmerPulse pulse, out WorldStamp.PlaceState place)
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
    }

    public override void SetControls()
    {
        if (!BridgeSystem.Active)
            return;
        Player.controlLeft = false;
        Player.controlRight = false;
        Player.controlUp = false;
        Player.controlDown = false;
        Player.controlJump = false;
        Player.controlHook = false;
        Player.controlMount = false;
        Player.controlQuickHeal = false;
        Player.controlQuickMana = false;
        Player.controlThrow = false;
        Player.controlUseTile = false;
        // The Terraria window keeps its own mouse and keys while it is focused, so Escape opens the
        // inventory and Settings -> Save & Exit works. Stardew drives the body only from the background.
        if (Main.hasFocus || Main.gameMenu)
            return;
        Player.controlUseItem = false;
        if (!Driving(out FarmerPulse pulse, out WorldStamp.PlaceState place))
            return;
        bool hand = pulse.Has(PulseFlags.HandActive) && pulse.Has(PulseFlags.Free);
        if (hand && pulse.Slot < 10 && Player.itemAnimation == 0)
            Player.selectedItem = pulse.Slot;
        Vector2 aim = place.ToWorld(pulse.AimX, pulse.AimY);
        Main.mouseX = (int)(aim.X - Main.screenPosition.X);
        Main.mouseY = (int)(aim.Y - Main.screenPosition.Y);
        Player.mouseInterface = false;
        Player.controlUseItem = hand && pulse.Has(PulseFlags.UseHeld);
        if (Inventory.PendingUse > 0 && Player.itemAnimation == 0)
        {
            Inventory.PendingUse--;
            Player.controlUseItem = true;
        }
    }

    public override void PostUpdate()
    {
        if (!BridgeSystem.Active)
            return;
        Place();
        Player.breath = Player.breathMax;
        if (Player.dead)
        {
            Player.dead = false;
            Player.statLife = Math.Max(1, Player.statLife);
        }
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
        (float x, float y) = WorldProjection.FromFeet(feet.X, feet.Y, Player.width, Player.height);
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
        }
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
            if (struckThisSwing.Contains(i) || !area.Intersects(npc.position.X, npc.position.Y, npc.width, npc.height))
                continue;
            struckThisSwing.Add(i);
            bool critical = Main.rand.Next(100) < crit;
            npc.SimpleStrikeNPC(damage, direction, critical, knockback, item.DamageType);
            npc.immune[Player.whoAmI] = Player.itemAnimation;
        }
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
        int feetY = (int)(Player.position.Y + Player.height);
        return new Rectangle(feetX - SpriteLayout.FarmerWidth / 2, feetY - SpriteLayout.FarmerHeight, SpriteLayout.FarmerWidth, SpriteLayout.FarmerHeight);
    }

    public override bool CanBeHitByNPC(NPC npc, ref int cooldownSlot)
        => !BridgeSystem.Active || npc.Hitbox.Intersects(FarmerBody());

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
    public static bool UsesArc(Item item)
        => item.damage > 0 && !item.noMelee && item.useStyle == ItemUseStyleID.Swing && item.pick == 0 && item.axe == 0 && item.hammer == 0;

    public override bool? CanHitNPC(Item item, Player player, NPC target)
    {
        if (BridgeSystem.Active && UsesArc(item))
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
        // aiStyle 1 is arrows and bullets, 2 is thrown knives and shuriken.
        bool falls = projectile.aiStyle == 1 || projectile.aiStyle == 2;
        if (!falls)
            return;
        float pull = projectile.velocity.Y - before.Y;
        if (pull > 0f && pull < 0.6f)
            projectile.velocity.Y = before.Y;
    }
}
