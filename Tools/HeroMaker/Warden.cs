using System;
using System.Collections.Generic;
using System.Drawing;
using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Models;
using WarcraftPlugin.Events;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Timers;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Events.ExtendedEvents;
using System.Linq;

namespace WarcraftPlugin.Classes
{
    internal class Warden : WarcraftClass
    {
        public override string DisplayName => "Warden";
        public override DefaultClassModel DefaultModel => new();
        public override Color DefaultColor => Color.White;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Fan of Knives", "Chance to disguise as an enemy and infiltrate their team."),
            new WarcraftAbility("Warden's Cloak", "Reduces damage and reflects it when attacked from behind."),
            new WarcraftAbility("Shadow Strike", "Chance to poison enemies, dealing damage over time."),
            new WarcraftAbility("Ultimate: Vengeance", "Respawn once per round with your weapons.")
        ];

        public override void Register()
        {
            HookEvent<EventPlayerHurtOther>(PlayerHurtOther);
            HookEvent<EventPlayerHurt>(OnPlayerHurt);
            HookEvent<EventPlayerDeath>(PlayerDeath);
            HookEvent<EventPlayerSpawn>(PlayerSpawn);
            HookEvent<EventRoundStart>(OnRoundStart);
            HookEvent<EventRoundEnd>(OnRoundEnd);
        }

        private static readonly Random _random = new();
        private bool hasRespawnedThisRound = false;
        private bool isVengeanceRespawn = false;
        private List<string> savedWeapons = new();
        private Timer? respawnTimer, restoreWeaponsTimer;

        private void OnRoundStart(EventRoundStart start)
        {
            hasRespawnedThisRound = false;
            savedWeapons.Clear();
            isVengeanceRespawn = false;
        }

        private void OnRoundEnd(EventRoundEnd end)
        {
            respawnTimer?.Kill();
            restoreWeaponsTimer?.Kill();
        }

        private void PlayerSpawn(EventPlayerSpawn spawn)
        {
            //var enemyTeam = Player.Team == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
            //HighlightSpawnPoints(enemyTeam);

            if (!Player.IsAlive()) return;

            if (isVengeanceRespawn)
            {
                isVengeanceRespawn = false; // Reset after the Vengeance respawn
                return; // Skip Fan of Knives
            }

            int abilityLevel = WarcraftPlayer.GetAbilityLevel(0);
            if (abilityLevel < 1) return; // No ability = No effect

            float baseChance = 0.044f; // 4.4% per level
            float maxChance = 0.22f;   // Cap at 22%

            float fanOfKnivesChance = Math.Clamp(baseChance * abilityLevel, 0.0f, maxChance);

            if (Random.Shared.NextDouble() <= fanOfKnivesChance)
            {
                new MoleEffect(Player, 9999f).Start();
            }
        }

        //DEBUG to check spawnpoints
        //private void HighlightSpawnPoints(CsTeam enemyTeam)
        //{
        //    var spawnPoints = Utilities.FindAllEntitiesByDesignerName<CInfoPlayerTerrorist>(enemyTeam == CsTeam.Terrorist ? "info_player_terrorist" : "info_player_counterterrorist").ToList();

        //    foreach (var spawnPoint in spawnPoints)
        //    {
        //        Warcraft.CreateBoxAroundPoint(spawnPoint.AbsOrigin, 20, 20, 20).Show(duration: 100);
        //    }
        //}

        private void OnPlayerHurt(EventPlayerHurt hurt)
        {
            if (hurt.Userid != Player || !Player.IsAlive()) return;

            int cloakLevel = WarcraftPlayer.GetAbilityLevel(1);
            if (cloakLevel < 1) return;

            var attacker = hurt.Attacker;
            if (attacker == null || !attacker.IsAlive()) return;

            // Get attacker and victim angles
            var attackerAngle = attacker.PlayerPawn.Value.EyeAngles.Y;
            var victimAngle = Player.PlayerPawn.Value.EyeAngles.Y;

            // Check if attacker is behind the victim
            if (Math.Abs(attackerAngle - victimAngle) <= 50)
            {
                float damageReduction = 0.10f * cloakLevel;  // 10% per level
                float reflectDamage = 0.03f * cloakLevel;   // 3% per level

                int reducedDamage = (int)(hurt.DmgHealth * (1 - damageReduction));
                int reflected = (int)(hurt.DmgHealth * reflectDamage);

                // Ignore the reduced damage
                hurt.IgnoreDamage(hurt.DmgHealth - reducedDamage);

                // Reflect damage to the attacker
                attacker.TakeDamage(reflected, Player, KillFeedIcon.prop_exploding_barrel);

                // Chat messages for feedback
                Player.PrintToChat($" {ChatColors.Green}Warden's Cloak reduced damage by {damageReduction * 100:F0}% and reflected {reflected} damage!");
                attacker.PrintToChat($" {ChatColors.Red}Your attack was partially reflected by {Player.GetRealPlayerName()}'s Warden's Cloak!");
            }

            int currentHealth = Player.PlayerPawn.Value.Health;
            int incomingDamage = hurt.DmgHealth;

            if (incomingDamage >= currentHealth)
            {
                SaveWeapons();
            }
        }

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            if (!@event.Userid.IsAlive() || @event.Userid.UserId == Player.UserId) return;

            int shadowStrikeLevel = WarcraftPlayer.GetAbilityLevel(2);
            double roll = _random.NextDouble();
            float baseChance = 0.05f; // 5% per level
            float maxChance = 0.25f;  // 25% max
            float shadowStrikeChance = Math.Clamp(baseChance * shadowStrikeLevel, 0.0f, maxChance);

            if (shadowStrikeLevel > 0 && roll <= shadowStrikeChance)
            {
                var effects = WarcraftPlugin.Instance.EffectManager.GetEffectsByType<ShadowStrikeEffect>() ?? new List<ShadowStrikeEffect>();
                var isVictimPoisoned = effects.Any(x => x.Victim.Handle == @event.Userid.Handle);

                if (!isVictimPoisoned)
                {
                    new ShadowStrikeEffect(Player, 5, 1, @event.Userid, 3).Start(); // 5s duration, 1s interval, 3 damage per tick
                    Player.PrintToChat($" {ChatColors.Green}You poisoned {ChatColors.Purple}{@event.Userid.GetRealPlayerName()}{ChatColors.Green} with {ChatColors.Gold}Shadow Strike!");
                    @event.Userid.PrintToChat($" {ChatColors.Red}{Player.GetRealPlayerName()}{ChatColors.Red} poisoned you with {ChatColors.Green}Shadow Strike!");
                }
            }
        }

        private void PlayerDeath(EventPlayerDeath death)
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || hasRespawnedThisRound) return;

            hasRespawnedThisRound = true;
            isVengeanceRespawn = true;

            Player.PrintToCenter("Vengeance activated! Respawning in 5 seconds...");

            // Respawn after 5 seconds
            respawnTimer = WarcraftPlugin.Instance.AddTimer(5.0f, () =>
            {
                if (Player.IsAlive()) return; // Avoid double respawn issues

                Player.Respawn();

                // Restore weapons after respawn
                restoreWeaponsTimer = WarcraftPlugin.Instance.AddTimer(1.0f, () =>
                {
                    RestoreWeapons();
                });

                Player.PrintToCenter($"You have returned with Vengeance!");
            });
        }

        // Save weapons before dying
        private void SaveWeapons()
        {
            savedWeapons.Clear();

            var weaponService = Player.PlayerPawn?.Value.WeaponServices;
            if (weaponService == null || weaponService.MyWeapons.Count() == 0) return;

            foreach (var weapon in weaponService.MyWeapons)
            {
                if (weapon.Value != null && !string.IsNullOrEmpty(weapon.Value.DesignerName))
                {
                    savedWeapons.Add(weapon.Value.DesignerName);
                }
            }
        }

        // Restore weapons after respawning
        private void RestoreWeapons()
        {
            if (!Player.IsAlive()) return;

            Player.PlayerPawn.Value.WeaponServices!.PreventWeaponPickup = false;

            if (savedWeapons.Count == 0) return;

            foreach (var weapon in savedWeapons)
            {
                Player.GiveNamedItem(weapon);
            }
        }

        internal class ShadowStrikeEffect(CCSPlayerController owner, float duration, float onTickInterval, CCSPlayerController victim, float damage)
        : WarcraftEffect(owner, duration, onTickInterval: onTickInterval)
        {
            public CCSPlayerController Victim = victim;
            private CParticleSystem _poisonParticle;

            public override void OnStart()
            {
                Victim.PrintToCenterAlert("[POISONED]");
                _poisonParticle = Warcraft.SpawnParticle(Victim.PlayerPawn.Value.AbsOrigin, "particles/generic_gameplay/poison.vpcf", Duration);
                _poisonParticle.SetParent(Victim.PlayerPawn.Value);
            }

            public override void OnTick()
            {
                if (!Victim.IsAlive()) Destroy();
                Victim.TakeDamage(damage, Owner, KillFeedIcon.prop_exploding_barrel);
            }

            public override void OnFinish()
            {
                _poisonParticle.RemoveIfValid();
            }
        }
    }

    internal class MoleEffect(CCSPlayerController owner, float duration = 999f) : WarcraftEffect(owner, duration)
    {

        public override void OnStart()
        {
            var enemyTeam = Owner.Team == CsTeam.CounterTerrorist ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
            var spawnPoints = Utilities.FindAllEntitiesByDesignerName<CInfoPlayerTerrorist>(enemyTeam == CsTeam.Terrorist ? "info_player_terrorist" : "info_player_counterterrorist").ToList();

            if (spawnPoints.Count == 0)
            {
                Owner.PrintToChat($" {ChatColors.Red}No valid spawn points found for enemy team.");
                Destroy();
                return;
            }

            Owner.PrintToCenter($"Fan of Knives! Teleport in 6 Seconds.");

            WarcraftPlugin.Instance.AddTimer(6.0f, () => // Delay before teleporting
            {
                var randomIndex = Random.Shared.Next(spawnPoints.Count);
                var chosenSpawn = spawnPoints[randomIndex];

                var enemyPlayer = Utilities.GetPlayers().FirstOrDefault(x => x.Team == enemyTeam && x.IsAlive());
                if (enemyPlayer == null)
                {
                    Owner.PrintToChat($" {ChatColors.Red}No alive enemy found for disguise.");
                    Destroy();
                    return;
                }
                var enemyModel = enemyPlayer.PlayerPawn.Value.CBodyComponent.SceneNode.GetSkeletonInstance().ModelState.ModelName;

                if (!string.IsNullOrEmpty(enemyModel))
                {
                    Owner.PlayerPawn.Value.SetModel(enemyModel);
                    Owner.PrintToChat($" {ChatColors.Green}You are now disguised as an enemy and teleported to enemy spawn!");
                }

                Owner.PlayerPawn.Value.Teleport(chosenSpawn.AbsOrigin, Owner.PlayerPawn.Value.AbsRotation, new Vector());
            });
        }

        public override void OnTick() { }

        public override void OnFinish()
        {
            Owner.GetWarcraftPlayer().GetClass().SetDefaultAppearance();
            Owner.PrintToChat($" {ChatColors.Red}Your mole disguise has worn off.");
        }
    }
}