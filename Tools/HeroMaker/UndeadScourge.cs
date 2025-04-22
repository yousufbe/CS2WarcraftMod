using System;
using System.Collections.Generic;
using System.Drawing;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WarcraftPlugin.Models;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Events.ExtendedEvents;

namespace WarcraftPlugin.Classes
{
    internal class UndeadScourge : WarcraftClass
    {
        public override string DisplayName => "Undead Scourge";
        public override DefaultClassModel DefaultModel => new();
        public override Color DefaultColor => Color.White;

        private const float SuicideBomberRadius = 384.0f;
        private const float SuicideBomberDamage = 90.0f;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Vampiric Aura", "Steal life from your enemies with each strike."),
            new WarcraftAbility("Unholy Aura", "Increase your movement speed with dark energy."),
            new WarcraftAbility("Levitation", "Defy gravity to jump higher and move freely."),
            new WarcraftCooldownAbility("Ultimate: Suicide Bomber", "Detonate upon death or by will, damaging nearby enemies.", 50f)
        ];

        public override void Register()
        {
            HookEvent<EventPlayerSpawn>(PlayerSpawn);
            HookEvent<EventPlayerDeath>(PlayerDeath);
            HookEvent<EventPlayerHurtOther>(PlayerHurtOther);
            HookEvent<EventRoundStart>(RoundStart);
            HookAbility(3, Ultimate);
        }

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            if (!@event.Userid.IsAlive() || @event.Userid.UserId == Player.UserId) return;

            int vampiricLevel = WarcraftPlayer.GetAbilityLevel(0);
            if (vampiricLevel > 0 && Player.PlayerPawn.Value.Health < Player.PlayerPawn.Value.MaxHealth)
            {
                float healthDrained = @event.DmgHealth * (0.1f * vampiricLevel);
                Player.SetHp((int)Math.Min(Player.PlayerPawn.Value.Health + healthDrained, Player.PlayerPawn.Value.MaxHealth));
                @event.AddBonusDamage(0);
            }
        }

        private void PlayerSpawn(EventPlayerSpawn spawn)
        {
            if (Player.IsAlive()) new UndeadAurasEffect(Player).Start();
        }

        public override void PlayerChangingToAnotherRace()
        {
            base.PlayerChangingToAnotherRace();
            ResetPlayerStats();
        }

        private void ResetPlayerStats()
        {
            if (Player.IsAlive())
            {
                Player.PlayerPawn.Value.GravityScale = 1.0f;
                Player.PlayerPawn.Value.VelocityModifier = 1.0f;
            }
        }

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) > 0 && IsAbilityReady(3))
            {
                Warcraft.SpawnExplosion(Player.EyePosition(), SuicideBomberDamage, SuicideBomberRadius, Player, KillFeedIcon.prop_exploding_barrel);

                Player.TakeDamage(Player.PlayerPawn.Value.Health, Player, KillFeedIcon.prop_exploding_barrel);

                StartCooldown(3);
            }
        }

        private void PlayerDeath(EventPlayerDeath death)
        {
            if (WarcraftPlayer.GetAbilityLevel(3) > 0)
            {
                WarcraftPlugin.Instance.AddTimer(0.1f, () =>
                    Warcraft.SpawnExplosion(Player.PlayerPawn.Value.AbsOrigin, SuicideBomberDamage, SuicideBomberRadius, Player));
            }
        }

        private void RoundStart(EventRoundStart start) => StartCooldown(3);
    }

    internal class UndeadAurasEffect(CCSPlayerController owner) : WarcraftEffect(owner, 9999)
    {
        public override void OnStart()
        {
            ApplyAuras();

            // Check and print the individual messages
            var levitationLevel = Owner.GetWarcraftPlayer().GetAbilityLevel(2);
            var unholyAuraLevel = Owner.GetWarcraftPlayer().GetAbilityLevel(1);

            if (levitationLevel > 0)
            {
                var gravityScale = 1.0f - (0.1f * levitationLevel);
                Owner.PrintToChat($" {ChatColors.Green}[Levitation] {ChatColors.Yellow}Your gravity has been set to {ChatColors.Gold}{gravityScale:F1}x{ChatColors.Yellow}.");
            }

            if (unholyAuraLevel > 0)
            {
                var speedModifier = 1.0f + (0.1f * unholyAuraLevel);
                Owner.PrintToChat($" {ChatColors.Green}[Unholy Aura] {ChatColors.Yellow}Your speed has been set to {ChatColors.Gold}{speedModifier:F1}x{ChatColors.Yellow}.");
            }
        }

        public override void OnTick() => ApplyAuras();
        public override void OnFinish()
        {
        }

        private void ApplyAuras()
        {
            if (!Owner.IsAlive()) return;
            Owner.PlayerPawn.Value.GravityScale = 1.0f - (0.1f * Owner.GetWarcraftPlayer().GetAbilityLevel(2));
            Owner.PlayerPawn.Value.VelocityModifier = 1.0f + (0.1f * Owner.GetWarcraftPlayer().GetAbilityLevel(1));
        }

        //private void ResetAuras()
        //{
        //    Owner.PlayerPawn.Value.GravityScale = 1.0f;
        //    Owner.PlayerPawn.Value.VelocityModifier = 1.0f;
        //}
    }
}