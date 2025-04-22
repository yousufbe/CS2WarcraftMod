using System;
using System.Collections.Generic;
using System.Drawing;
using CounterStrikeSharp.API.Core;
using WarcraftPlugin.Models;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Events.ExtendedEvents;
using System.Linq;

namespace WarcraftPlugin.Classes
{
    internal class CryptLord : WarcraftClass
    {
        public override string DisplayName => "Crypt Lord";
        public override DefaultClassModel DefaultModel => new();
        public override Color DefaultColor => Color.White;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Impale", "Your attacks may launch enemies into the air."),
            new WarcraftAbility("Spiked Carapace", "Start with extra armor and occasionally reflect damage."),
            new WarcraftAbility("Carrion Beetles", "Your attacks have a chance to inflict bonus damage."),
            new WarcraftCooldownAbility("Ultimate: Locust Swarm", "Summon locusts that steal health and armor from a nearby enemy.", 40f)
        ];

        public override void Register()
        {
            HookEvent<EventPlayerHurtOther>(PlayerHurtOther);
            HookEvent<EventPlayerHurt>(OnPlayerHurt);
            HookEvent<EventPlayerDeath>(PlayerDeath);
            HookEvent<EventPlayerSpawn>(OnPlayerSpawn);
            HookAbility(3, Ultimate);
        }

        private void OnPlayerSpawn(EventPlayerSpawn spawn)
        {
            // Give initial armor on spawn based on ability level
            int carapaceLevel = WarcraftPlayer.GetAbilityLevel(1);
            int spawnArmor = 100 + (25 * (carapaceLevel - 1));
            Player.PlayerPawn.Value.ArmorValue = spawnArmor;
        }

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            if (!@event.Userid.IsAlive() || @event.Userid.UserId == Player.UserId) return;

            int impaleLevel = WarcraftPlayer.GetAbilityLevel(0);
            int beetlesLevel = WarcraftPlayer.GetAbilityLevel(2);

            if (impaleLevel > 0)
            {
                // Calculate chance to proc
                float impaleChance = 0.10f + (impaleLevel - 1) * 0.02f;
                if (Random.Shared.NextDouble() < impaleChance)
                {
                    @event.Userid.PlayerPawn.Value.AbsVelocity.Add(z: 500);
                    Player.PrintToChat($" {ChatColors.Green}You {ChatColors.Gold}impaled {ChatColors.Yellow}{@event.Userid.GetRealPlayerName()}!");
                    @event.Userid.PrintToChat($" {ChatColors.Red}You were impaled by {ChatColors.Purple}{Player.GetRealPlayerName()}!");
                }
            }

            // Carrion Beetles: Bonus Damage
            if (beetlesLevel > 0 && Warcraft.RollDice(beetlesLevel, 20))
            {
                @event.AddBonusDamage(3 * beetlesLevel);
                Player.PrintToChat($" {ChatColors.Green}Your {ChatColors.Gold}Carrion Beetles {ChatColors.Green}dealt bonus damage to {ChatColors.Yellow}{@event.Userid.GetRealPlayerName()}!");
                @event.Userid.PrintToChat($" {ChatColors.Red}{Player.GetRealPlayerName()}'s {ChatColors.Gold}Carrion Beetles {ChatColors.Green}dealt bonus damage to you!");
            }
        }

        private void OnPlayerHurt(EventPlayerHurt hurt)
        {
            if (hurt.Userid != Player || !Player.IsAlive()) return;

            int carapaceLevel = WarcraftPlayer.GetAbilityLevel(1);
            if (carapaceLevel < 1) return;

            var attacker = hurt.Attacker;
            if (attacker == null || !attacker.IsAlive()) return;

            // Calculate mirror damage chance and damage amount based on ability level
            float mirrorChance = 0.2f + 0.05f * (carapaceLevel - 1);  // 20% to 40% chance
            float mirrorDamageMultiplier = 0.1f + 0.025f * (carapaceLevel - 1);  // 10% to 20% mirror damage

            if (Random.Shared.NextDouble() < mirrorChance)
            {
                int reflected = (int)(hurt.DmgHealth * mirrorDamageMultiplier);
                attacker.TakeDamage(reflected, Player, KillFeedIcon.prop_exploding_barrel);

                Player.PrintToChat($" {ChatColors.Green}Your {ChatColors.Gold}Spiked Carapace {ChatColors.Default}reflected some of the damage!");
                attacker.PrintToChat($" {ChatColors.Red}{Player.GetRealPlayerName()}'s {ChatColors.Gold}Spiked Carapace {ChatColors.Red}reflected damage to you!");
            }
        }

        private void PlayerDeath(EventPlayerDeath death)
        {
            // Cleanup or effects on death if needed
        }

        private void Ultimate()
        {
            // Ensure the ultimate is only usable if the player has it unlocked and the cooldown is ready
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            // Get all enemies within a certain range (e.g., 600 units) who are alive
            var enemies = Utilities.GetPlayers()
                .Where(p => p.IsAlive() && p.TeamNum != Player.TeamNum && (p.EyePosition() - Player.EyePosition()).Length() <= 600)
                .ToList();

            // If no enemies are found, notify the player and do not trigger the ability
            if (!enemies.Any())
            {
                Player.PrintToChat("No enemies in range for the Locust Swarm!");
                StartCooldown(3, 5.0f);
                return;
            }

            // Choose a random enemy from the list
            var target = enemies[Random.Shared.Next(enemies.Count())];

            // Apply the effect: steal 25 health and a random amount of armor (between 10 and 30)
            int stolenArmor = Math.Min(Random.Shared.Next(10, 31), target.PlayerPawn.Value.ArmorValue);
            int stolenHealth = Math.Min(25, target.PlayerPawn.Value.Health); // Ensure we don’t steal more health than the target has

            // Deal damage to the target using TakeDamage
            target.TakeDamage(stolenHealth, Player, KillFeedIcon.prop_exploding_barrel);

            // Adjust armor and health for both the caster and the target
            target.SetArmor(target.PlayerPawn.Value.ArmorValue - stolenArmor);
            Player.SetHp(Math.Min(Player.PlayerPawn.Value.Health + stolenHealth, 100));
            Player.SetArmor(Player.PlayerPawn.Value.ArmorValue + stolenArmor);
            int maxArmor = 100 + (25 * (WarcraftPlayer.GetAbilityLevel(1) - 1));
            Player.SetArmor(Math.Min(Player.PlayerPawn.Value.ArmorValue, maxArmor));

            // Print messages to inform the affected players
            Player.PrintToChat($" {ChatColors.Gold}Locust Swarm {ChatColors.Green}stole {ChatColors.Red}{stolenHealth} health {ChatColors.Green}and {ChatColors.Blue}{stolenArmor} armor {ChatColors.Green}from {ChatColors.Yellow}{target.GetRealPlayerName()}!");
            target.PrintToChat($" {ChatColors.Red}You lost {ChatColors.Red}{stolenHealth} health {ChatColors.Red}and {ChatColors.Blue}{stolenArmor} armor {ChatColors.Red}to {ChatColors.Purple}{Player.GetRealPlayerName()}'s {ChatColors.Gold}Locust Swarm!");

            // Start the cooldown
            StartCooldown(3);
        }
    }
}
    