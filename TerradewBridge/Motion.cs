using System;
using Terradew.Core.Protocol;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace TerradewBridge;

/// <summary>
/// Tells Stardew how the Terraria character moves. Terraria works out its accessories, buffs and mount every tick
/// even while Stardew drives the body, so the numbers are read straight from the player.
/// </summary>
internal static class Motion
{
    /// <summary>An unchanged report is still resent this often.</summary>
    private const int ResendTicks = 60;

    /// <summary>An unequipped character's top walking speed, in Terraria pixels a tick.</summary>
    private const float BaseRunSpeed = 3f;

    private static Movement? last;
    private static int lastSent = int.MinValue / 2;

    public static void Publish(Link link, Player player, int tick)
    {
        if (player == null || !player.active)
            return;
        Movement now = Read(player);
        bool same = last != null && Math.Abs(last.Speed - now.Speed) < 0.01f && last.FlightTicks == now.FlightTicks
            && Math.Abs(last.FlySpeed - now.FlySpeed) < 0.01f && last.MountType == now.MountType && last.MountJumps == now.MountJumps;
        if (same && tick - lastSent < ResendTicks)
            return;
        last = now;
        lastSent = tick;
        link.Send(now);
    }

    private static Movement Read(Player player)
    {
        var movement = new Movement();
        if (player.mount.Active)
        {
            movement.MountType = (short)player.mount.Type;
            movement.MountJumps = player.mount.JumpHeight(player, 0f) > 0;
            // Mounts run much faster than feet; Stardew's paths are short, so only part of that carries over.
            movement.Speed = Math.Clamp(1f + (player.mount.RunSpeed / BaseRunSpeed - 1f) * 0.5f, 1f, 2.2f);
            return movement;
        }
        // Hermes Boots and the like raise the sprint speed, Swiftness and some armor the move speed.
        float run = Math.Max(player.maxRunSpeed, player.accRunSpeed) / BaseRunSpeed;
        movement.Speed = Math.Clamp(player.moveSpeed * (1f + (run - 1f) * 0.5f), 0.5f, 2.2f);
        if (player.wingTimeMax > 0 && player.wingsLogic > 0)
        {
            WingStats wings = player.GetWingStats(player.wingsLogic);
            movement.FlightTicks = (short)Math.Clamp(player.wingTimeMax, 0, short.MaxValue);
            movement.FlySpeed = Math.Max(wings.AccRunSpeedOverride, run * BaseRunSpeed);
        }
        if (player.rocketTimeMax > 0)
        {
            // Rocket boots burn a unit of rocket time about every six ticks.
            movement.FlightTicks = (short)Math.Clamp(movement.FlightTicks + player.rocketTimeMax * 6, 0, short.MaxValue);
            if (movement.FlySpeed <= 0f)
                movement.FlySpeed = Math.Max(player.maxRunSpeed, player.accRunSpeed);
        }
        return movement;
    }
}
