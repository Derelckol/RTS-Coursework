using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts.Editor
{
    internal static class MatchChecks
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        public static void Run()
        {
            string scene = SceneManager.GetActiveScene().path;
            CheckIntro(new Fixture(true, startMatch: false));
            EditorSceneManager.OpenScene(scene);
            CheckEconomies(new Fixture(false));
            EditorSceneManager.OpenScene(scene);
            CheckDirector(new Fixture(true, durablePlayerHall: true));
            EditorSceneManager.OpenScene(scene);
            CheckDefense(new Fixture(false));
            EditorSceneManager.OpenScene(scene);
            CheckBaseProtection(new Fixture(false));
            EditorSceneManager.OpenScene(scene);
            CheckPlayerDevelopment(new Fixture(true));
            EditorSceneManager.OpenScene(scene);
            CheckOutcome(new Fixture(false), MatchOutcome.Victory);
            EditorSceneManager.OpenScene(scene);
            CheckOutcome(new Fixture(false), MatchOutcome.Defeat);
            EditorSceneManager.OpenScene(scene);
            CheckOutcome(new Fixture(false), MatchOutcome.Draw);
            EditorSceneManager.OpenScene(scene);
            CheckDifficulties(scene);
            EditorSceneManager.OpenScene(scene);
            CheckRaidDuringDefense(new Fixture(true, durablePlayerHall: true));
            EditorSceneManager.OpenScene(scene);
            CheckEarlyDefense(new Fixture(true), BuildingKind.TownHall);
            EditorSceneManager.OpenScene(scene);
            CheckEarlyDefense(new Fixture(true), BuildingKind.None);
            EditorSceneManager.OpenScene(scene);
            CheckRetaliation(new Fixture(true));
            EditorSceneManager.OpenScene(scene);
            CheckStationsAndDetection(new Fixture(true));
            BreachChecks.Run();
            EditorSceneManager.OpenScene(scene);
            var fresh = new Fixture(false, startMatch: false);
            Require(!fresh.Match.HasStarted && fresh.Match.Outcome == MatchOutcome.Ongoing &&
                fresh.Player.Gold == fresh.Rival.Gold &&
                fresh.Player.Used == 3 && fresh.Rival.Used == 3 && fresh.Player.Reserved == 0 &&
                fresh.Rival.Reserved == 0 && fresh.Hall(Side.Player).IsAlive && fresh.Hall(Side.Opponent).IsAlive,
                "A clean reload retained match state.");
        }

        private static void CheckStationsAndDetection(Fixture fixture)
        {
            fixture.Tick(3000);
            var assignments = Get<System.Collections.IDictionary>(fixture.Director, "stations");
            var initial = new Dictionary<PrototypeUnit, (string role, Vector3 position)>();
            foreach (System.Collections.DictionaryEntry entry in assignments)
            {
                object station = entry.Value;
                var position = (Vector3?)station.GetType().GetField("Position").GetValue(station);
                if (position.HasValue)
                {
                    initial.Add((PrototypeUnit)entry.Key,
                        (station.GetType().GetField("Role").GetValue(station).ToString(), position.Value));
                }
            }
            Require(initial.Values.Select(entry => entry.role).Distinct().Count() == 3 &&
                initial.Values.Select(entry => entry.position).Distinct().Count() == initial.Count,
                "Guard, reserve and assault did not get distinct positions.");
            fixture.Tick(200);
            foreach (var entry in initial)
            {
                object station = assignments[entry.Key];
                Require((Vector3?)station.GetType().GetField("Position").GetValue(station) == entry.Value.position,
                    "An assigned station was replaced without a change of role or obstruction.");
            }

            PrototypeUnit scout = fixture.Units(Side.Player, UnitKind.Archer)[0];
            CombatTarget actor = scout.GetComponent<CombatTarget>();
            var settings = new SerializedObject(actor);
            settings.FindProperty("maxHealth").floatValue = 12000f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            actor.Initialize();
            var visibility = new TestVisibility(actor);
            typeof(OpponentDirector).GetField("visibility", PrivateInstance).SetValue(fixture.Director, visibility);
            scout.transform.position = new Vector3(29.5f, 0.85f, 18.5f);
            fixture.Orders.Move(new[] { scout }, new Vector3(28.5f, 0, 9.5f));
            fixture.Tick(75);
            Require(fixture.Director.Mode != OpponentMode.Defend, "The director tracked a hidden scout.");

            visibility.Hidden = false;
            fixture.Until(() => fixture.Director.Mode == OpponentMode.Defend, 120);
            fixture.Until(() => initial.Any(entry => entry.Value.role == "Guard" &&
                    fixture.Orders.IsAttacking(entry.Key, actor)) &&
                initial.Any(entry => entry.Value.role == "Reserve" && fixture.Orders.IsAttacking(entry.Key, actor)), 120);
            Require(!fixture.Orders.IsThreatening(scout, Side.Opponent) &&
                initial.Count(entry => fixture.Orders.IsAttacking(entry.Key, actor)) < initial.Count,
                "Passive detection failed or dispatched the entire army.");
            CombatTarget northernBuilding = fixture.Actors
                .Where(target => target.Side == Side.Opponent && target.Unit == null)
                .OrderBy(target => (target.transform.position - scout.transform.position).sqrMagnitude).First();
            float northernHealth = northernBuilding.Health;
            fixture.Orders.Interact(new[] { scout }, northernBuilding);
            fixture.Until(() => northernBuilding.Health < northernHealth, 350);

            fixture.Orders.Move(new[] { scout }, new Vector3(29.5f, 0, 24.5f));
            fixture.Until(() => fixture.Director.Mode != OpponentMode.Defend, 300);
            fixture.Until(() => initial.Any(entry => entry.Value.role == "Guard" &&
                At(entry.Key, entry.Value.position) && !fixture.Orders.IsResponding(entry.Key)), 400);

            fixture.Orders.Halt(scout);
            scout.transform.position = new Vector3(24.5f, 0.85f, -18.5f);
            fixture.Orders.Move(new[] { scout }, new Vector3(23.5f, 0, -9.5f));
            fixture.Until(() => fixture.Director.Mode == OpponentMode.Defend &&
                initial.Any(entry => fixture.Orders.IsAttacking(entry.Key, actor)), 180);
            PrototypeUnit southernWorker = fixture.Units(Side.Opponent, UnitKind.Worker)
                .OrderBy(unit => (unit.transform.position - scout.transform.position).sqrMagnitude).First();
            CombatTarget workerTarget = southernWorker.GetComponent<CombatTarget>();
            float workerHealth = workerTarget.Health;
            fixture.Orders.Interact(new[] { scout }, workerTarget);
            fixture.Until(() => workerTarget.Health < workerHealth, 350);
            Require(fixture.Director.RaidsLaunched == 0, "Approaches triggered after the first wave.");
            Debug.Log("Formation verification: distinct stable posts; hidden scout ignored; northern and mining approaches intercepted; reserve helped and guards returned.");
        }

        private sealed class TestVisibility : ISideVisibility
        {
            private readonly CombatTarget testActor;
            public bool Hidden = true;
            public TestVisibility(CombatTarget actor) => testActor = actor;
            public bool IsVisible(Side observer, CombatTarget target) => !Hidden || target != testActor;
        }

        private static void CheckRaidDuringDefense(Fixture fixture)
        {
            fixture.Tick(10250);
            PrototypeUnit invader = fixture.Units(Side.Player, UnitKind.Infantry)[0];
            var actor = invader.GetComponent<CombatTarget>();
            var actorSettings = new SerializedObject(actor);
            actorSettings.FindProperty("maxHealth").floatValue = 20000f;
            actorSettings.ApplyModifiedPropertiesWithoutUndo();
            actor.Initialize();
            invader.transform.position = new Vector3(33.5f, 0.85f, 0.5f);
            fixture.Until(() => fixture.Director.Mode == OpponentMode.Defend, 120);
            fixture.Tick(200);
            Require(fixture.Actors.Any(unit => unit.Side == Side.Opponent && unit.Unit != null &&
                fixture.Orders.IsAttacking(unit.Unit, actor)), "A strong threat received no defense.");
            invader.transform.position = new Vector3(-23.5f, 0.85f, 1.5f);
            fixture.Until(() => fixture.Director.Mode != OpponentMode.Defend, 180);
            fixture.Until(() => fixture.Director.RaidsLaunched == 1, 1100);
            var raiders = Get<HashSet<PrototypeUnit>>(fixture.Director, "raiders");
            var garrison = Get<HashSet<PrototypeUnit>>(fixture.Director, "garrison");
            Require(!garrison.Overlaps(raiders),
                "Defense prevented role recovery and preparation of the next raid.");
            var casualty = raiders.First().GetComponent<CombatTarget>();
            casualty.Damage(casualty.MaxHealth);
            var preRecovery = fixture.Actors.Where(unit => unit.Side == Side.Opponent &&
                unit.Unit != null && unit.Unit.CanAttack).ToHashSet();
            fixture.Until(() => fixture.Actors.Any(unit => unit.Side == Side.Opponent &&
                unit.Unit != null && unit.Unit.CanAttack && !preRecovery.Contains(unit)), 1600);
            Require(fixture.Units(Side.Opponent, UnitKind.Worker).Length + fixture.Rival.Pending(UnitKind.Worker) <=
                fixture.Director.Profile.DesiredWorkers, "Worker recovery exceeded the profile cap.");
        }

        private static void CheckEarlyDefense(Fixture fixture, BuildingKind attacked)
        {
            fixture.Tick(3000);
            var attacker = fixture.Units(Side.Player, UnitKind.Archer)[0];
            attacker.transform.position = new Vector3(25.5f, 0.85f, 0.5f);
            var victim = attacked == BuildingKind.TownHall ? fixture.Hall(Side.Opponent) :
                fixture.Units(Side.Opponent, UnitKind.Worker)[0].GetComponent<CombatTarget>();
            if (victim.Unit != null)
            {
                fixture.Orders.Halt(victim.Unit);
                victim.transform.position = new Vector3(26.5f, 0.85f, 2.5f);
            }
            fixture.Orders.Interact(new[] { attacker }, victim);
            var target = attacker.GetComponent<CombatTarget>();
            var guards = Get<HashSet<PrototypeUnit>>(fixture.Director, "garrison");
            var soldiers = fixture.Actors.Where(actor => actor.Side == Side.Opponent &&
                actor.Unit != null && actor.Unit.CanAttack).Select(actor => actor.Unit).ToArray();
            fixture.Until(() => guards.Any(unit => fixture.Orders.IsAttacking(unit, target)) &&
                soldiers.Any(unit => !guards.Contains(unit) && fixture.Orders.IsAttacking(unit, target)), 100);
            Require(fixture.Director.RaidsLaunched == 0 && fixture.Director.Mode == OpponentMode.Defend &&
                victim.Health < victim.MaxHealth, "Defense did not engage before the first wave.");
            Debug.Log($"Early defense {attacked}: {soldiers.Count(unit => fixture.Orders.IsAttacking(unit, target))}/{soldiers.Length} responding.");
        }

        private static void CheckRetaliation(Fixture fixture)
        {
            fixture.Tick(3000);
            var guards = Get<HashSet<PrototypeUnit>>(fixture.Director, "garrison");
            var guard = guards.First();
            var future = fixture.Actors.First(actor => actor.Side == Side.Opponent && actor.Unit != null &&
                actor.Unit.CanAttack && !guards.Contains(actor.Unit)).Unit;
            typeof(PrototypeControls).GetField("director", PrivateInstance).SetValue(fixture.Controls, null);
            var archer = fixture.Units(Side.Player, UnitKind.Archer)[0];
            archer.transform.position = new Vector3(19.5f, 0.85f, -13.5f);
            var attacker = archer.GetComponent<CombatTarget>();
            foreach (PrototypeUnit soldier in new[] { guard, future })
            {
                fixture.Orders.Halt(soldier);
                soldier.transform.position = new Vector3(20.5f, 0.85f, -13.5f);
                fixture.Orders.Move(new[] { soldier }, new Vector3(25.5f, 0.85f, -13.5f));
                var victim = soldier.GetComponent<CombatTarget>();
                fixture.Orders.Interact(new[] { archer }, victim);
                fixture.Until(() => fixture.Orders.IsResponding(soldier) &&
                    fixture.Orders.IsAttacking(soldier, attacker), 80);
                fixture.Orders.Halt(soldier);
                soldier.transform.position = new Vector3(35.5f, 0.85f, -13.5f);
            }
        }

        private static void CheckDifficulties(string scene)
        {
            DifficultyProfile easy = MatchSession.Profile(DifficultyLevel.Easy);
            Require(easy.FirstRaidDelay == 210f && easy.RaidInterval == 150f && easy.FirstSquadSize == 2 &&
                easy.SquadGrowth == 1 && easy.MaxSquadSize == 5 && easy.DesiredWorkers == 3 &&
                easy.DecisionInterval == 1.2f && easy.CapacityBuffer == 1 &&
                easy.SquadComposition.SequenceEqual(new[]
                {
                    UnitKind.Infantry, UnitKind.Archer, UnitKind.Infantry, UnitKind.Cavalry
                }), "The verified easy profile changed.");
            var selected = typeof(MatchSession).GetProperty(nameof(MatchSession.Selected));
            try
            {
                foreach (DifficultyLevel level in new[] { DifficultyLevel.Medium, DifficultyLevel.Hard })
                {
                    selected.SetValue(null, level);
                    EditorSceneManager.OpenScene(scene);
                    var fixture = new Fixture(true, startMatch: false);
                    DifficultyProfile profile = MatchSession.Profile(level);
                    Require(fixture.Match.Difficulty == profile && fixture.Director.Profile == profile &&
                        fixture.Player.Gold == 140 && fixture.Rival.Gold == 140 &&
                        ReferenceEquals(fixture.Player.Products, fixture.Rival.Products),
                        "The chosen profile was not passed through or changed the common economy.");
                    fixture.Tick(3000);
                    Require(Get<float>(fixture.Director, "elapsed") == 0f &&
                        Get<float>(fixture.Director, "nextRaid") == profile.FirstRaidDelay,
                        "The raid countdown advanced before closing the intro.");
                    fixture.Match.StartMatch();
                    Invoke(fixture.Match, "OpenMenuConfirmation");
                    fixture.Hud.SetPaused(true);
                    Require(fixture.Match.CancelMenuConfirmation() && fixture.Hud.IsPaused,
                        "Cancelling the menu confirmation unpaused the match.");
                    fixture.Tick(100);
                    Require(Get<float>(fixture.Director, "elapsed") == 0f, "Pause ran the director.");
                    fixture.Hud.SetPaused(false);
                    fixture.Until(() => fixture.Director.RaidsLaunched == 1,
                        Mathf.CeilToInt((profile.FirstRaidDelay + 25f) / 0.02f));
                    Require(fixture.Director.LastRaidAt >= profile.FirstRaidDelay &&
                        fixture.Director.LastRaidSize == profile.FirstSquadSize,
                        $"The first {level} raid departed at {fixture.Director.LastRaidAt:F2}s with " +
                        $"{fixture.Director.LastRaidSize}/{profile.FirstSquadSize} soldiers. {fixture.Director.DebugRoster}");
                    Debug.Log($"Difficulty verification: {level} first raid {fixture.Director.LastRaidAt:F2}s " +
                        $"with {fixture.Director.LastRaidSize} soldiers.");
                }
            }
            finally
            {
                selected.SetValue(null, DifficultyLevel.Easy);
            }
        }

        private static void CheckIntro(Fixture fixture)
        {
            var original = fixture.Actors.Select(actor =>
                (actor.transform.position, actor.Health, actor.IsOperational)).ToArray();
            var stock = fixture.Mines.Select(mine => mine.Stock).ToArray();
            int gold = fixture.Player.Gold;
            float firstRaid = Get<float>(fixture.Director, "nextRaid");
            var layout = new HudLayout(1366, 768, true);
            Vector2 minimap = layout.MinimapContent(PrototypeSceneBuilder.MatchMap).center;
            Require(!(bool)Invoke(fixture.Controls, "TryFocusMinimap", layout, minimap) &&
                (bool)Invoke(fixture.Controls, "IsUiCovered", layout, new Vector2(500, 200)),
                "World input was not blocked by the intro.");
            fixture.Tick(3000);
            Require(!fixture.Match.HasStarted && Get<float>(fixture.Match, "elapsed") == 0f &&
                Get<float>(fixture.Director, "elapsed") == 0f && Get<float>(fixture.Director, "nextRaid") == firstRaid &&
                fixture.Player.Gold == gold && fixture.Rival.Gold == gold &&
                fixture.Player.Used == 3 && fixture.Rival.Used == 3 &&
                fixture.Player.Reserved == 0 && fixture.Rival.Reserved == 0 &&
                stock.SequenceEqual(fixture.Mines.Select(mine => mine.Stock)) &&
                original.SequenceEqual(fixture.Actors.Select(actor =>
                    (actor.transform.position, actor.Health, actor.IsOperational))),
                "A minute in the intro changed the match.");

            fixture.Hud.SetPaused(true);
            fixture.Match.StartMatch();
            Require(fixture.Match.BlocksWorldInput &&
                !(bool)Invoke(fixture.Controls, "TryFocusMinimap", layout, minimap),
                "Starting exposed the world to the activating click.");
            fixture.Tick(50);
            Require(Get<float>(fixture.Match, "elapsed") == 0f && fixture.Rival.Gold == gold,
                "Closing the intro bypassed pause.");
            fixture.Hud.SetPaused(false);
            fixture.Tick(1);
            Require(Get<float>(fixture.Match, "elapsed") > 0f && fixture.Rival.Gold < gold &&
                fixture.Director.RaidsLaunched == 0, "Starting did not resume from the original state.");
        }

        private static void CheckEconomies(Fixture fixture)
        {
            PrototypeEconomy player = fixture.Player;
            PrototypeEconomy rival = fixture.Rival;
            Require(ReferenceEquals(player.Products, rival.Products) && player.Gold == rival.Gold &&
                player.Used == 3 && rival.Used == 3 && player.Maximum == rival.Maximum, "Starting sides differ.");
            int startingGold = player.Gold;
            var workerProduct = rival.Products.Single(item => item.Kind == UnitKind.Worker);
            Require(!rival.Enqueue(fixture.Hall(Side.Player), workerProduct) &&
                !player.Enqueue(fixture.Hall(Side.Opponent), workerProduct), "A side ordered from another town hall.");
            Require(rival.Enqueue(fixture.Hall(Side.Opponent), workerProduct) && rival.Reserved == 1 &&
                player.Reserved == 0 && rival.Gold == startingGold - workerProduct.Cost && player.Gold == startingGold,
                "Opponent production changed the player's account.");
            fixture.Until(() => rival.Used == 4, 180);
            Require(rival.Reserved == 0 && fixture.Units(Side.Opponent, UnitKind.Worker).Length == 2 && player.Used == 3,
                "Opponent production spawned a player unit or reserved the wrong population.");

            GoldDeposit mine = fixture.Mines.Single(item => item.name == "Opponent Gold");
            PrototypeUnit opponentWorker = fixture.Units(Side.Opponent, UnitKind.Worker)[0];
            int stock = mine.Stock;
            int rivalBeforeGathering = rival.Gold;
            fixture.Orders.Gather(new[] { opponentWorker }, mine);
            fixture.Until(() => opponentWorker.CarriedGold > 0, 280);
            Require(mine.Stock == stock - rival.LoadSize && rival.Gold == rivalBeforeGathering,
                "Gathering credited the opponent before delivery.");
            fixture.Until(() => rival.Gold > rivalBeforeGathering, 320);
            Require(rival.Gold == rivalBeforeGathering + rival.LoadSize && player.Gold == startingGold,
                "Opponent delivery changed the wrong account.");

            var house = fixture.Construction.Blueprints.Single(item => item.Kind == BuildingKind.House);
            int before = rival.Gold;
            int maximum = rival.Maximum;
            var foundation = fixture.Construction.TryBuild(house, opponentWorker, new Vector3(29.5f, 0, 5.5f));
            Require(foundation != null && foundation.Side == Side.Opponent && !foundation.IsOperational &&
                rival.Gold == before - house.Cost && player.Gold == startingGold &&
                rival.Maximum == maximum, "Opponent foundation did not follow the shared cost or capacity rules.");
            fixture.Until(() => foundation.IsOperational, 500);
            Require(rival.Maximum > maximum && player.Maximum == maximum, "Opponent house altered player capacity.");
        }

        private static void CheckDirector(Fixture fixture)
        {
            OpponentDirector director = fixture.Director;
            int initialGold = fixture.Rival.Gold;
            int barracksCost = fixture.Construction.Blueprints.Single(item => item.Kind == BuildingKind.Barracks).Cost;
            int workerCost = fixture.Rival.Products.Single(item => item.Kind == UnitKind.Worker).Cost;
            fixture.Tick(1);
            Require(fixture.Rival.Gold == initialGold - barracksCost - workerCost &&
                fixture.Rival.Pending(UnitKind.Worker) == 1 && fixture.Player.Gold == initialGold &&
                fixture.Actors.Count(actor => actor.Side == Side.Opponent && actor.Building == BuildingKind.Barracks) == 1,
                "The director did not pay for the first barracks and worker.");
            fixture.Tick(70);
            Require(fixture.Rival.Pending(UnitKind.Worker) == 1 &&
                fixture.Actors.Count(actor => actor.Side == Side.Opponent && actor.Building == BuildingKind.Barracks) == 1,
                "The next decision duplicated reserved construction or production.");

            fixture.Hud.SetPaused(true);
            int gold = fixture.Rival.Gold;
            int raids = director.RaidsLaunched;
            float progress = fixture.Actors.Single(actor => actor.Side == Side.Opponent &&
                actor.Building == BuildingKind.Barracks).Site.Progress;
            fixture.Tick(600);
            Require(gold == fixture.Rival.Gold && raids == director.RaidsLaunched &&
                progress == fixture.Actors.Single(actor => actor.Side == Side.Opponent &&
                    actor.Building == BuildingKind.Barracks).Site.Progress,
                "Opponent decisions or construction advanced during pause.");
            fixture.Hud.SetPaused(false);

            PrototypeUnit invader = fixture.Units(Side.Player, UnitKind.Infantry)[0];
            Vector3 original = invader.transform.position;
            invader.transform.position = new Vector3(24.5f, 0.85f, 0.5f);
            var threat = invader.GetComponent<CombatTarget>();
            fixture.Until(() => director.Mode == OpponentMode.Defend, 100);
            Require(fixture.Actors.Any(actor => actor.Side == Side.Opponent && actor.Unit != null &&
                fixture.Orders.IsAttacking(actor.Unit, threat)), "Defenders did not target the intruder.");
            invader.transform.position = original;
            fixture.Until(() => director.Mode != OpponentMode.Defend, 110);

            fixture.Until(() => director.RaidsLaunched >= 1, 14000);
            float firstRaid = director.LastRaidAt;
            int firstSize = director.LastRaidSize;
            Require(fixture.Actors.Any(actor => actor.Side == Side.Opponent &&
                    actor.Building == BuildingKind.Barracks && actor.IsOperational) &&
                fixture.Units(Side.Opponent, UnitKind.Worker).Length == 3 &&
                fixture.Actors.Count(actor => actor.Side == Side.Opponent && actor.Unit != null &&
                    fixture.Orders.IsAttacking(actor.Unit, fixture.Hall(Side.Player))) >= 2 &&
                fixture.Mines.Single(mine => mine.name == "Opponent Gold").Stock < 1600 &&
                fixture.Player.Gold == initialGold && firstSize == 2 && firstRaid >= 210f && firstRaid <= 240f,
                "The AI did not gather resources, recruit or launch the first squad on schedule.");
            fixture.Until(() => director.RaidsLaunched >= 2, 11500);
            Require(director.LastRaidSize == 3 && director.LastRaidAt - firstRaid >= 150f &&
                director.LastRaidAt - firstRaid <= 180f, "The second wave did not grow or stay on schedule.");
            Debug.Log($"Training balance waves: first {firstRaid:F2}s ({firstSize}); " +
                $"second {director.LastRaidAt:F2}s ({director.LastRaidSize}); base hit {fixture.Match.FirstBaseAttackAt:F2}s.");
        }

        private static void CheckOutcome(Fixture fixture, MatchOutcome expected)
        {
            CombatTarget playerHall = fixture.Hall(Side.Player);
            CombatTarget rivalHall = fixture.Hall(Side.Opponent);
            if (expected == MatchOutcome.Victory)
            {
                var hall = fixture.Construction.Blueprints.Single(item => item.Kind == BuildingKind.TownHall);
                CombatTarget unfinished = fixture.Construction.TryBuild(hall,
                    fixture.Units(Side.Opponent, UnitKind.Worker)[0], new Vector3(29.5f, 0, 5.5f));
                Require(unfinished != null && !unfinished.IsOperational, "Expected an unfinished replacement hall.");
                rivalHall.Damage(rivalHall.MaxHealth);
            }
            else if (expected == MatchOutcome.Defeat)
            {
                playerHall.Damage(playerHall.MaxHealth);
            }
            else
            {
                playerHall.Damage(playerHall.MaxHealth - 1f);
                rivalHall.Damage(rivalHall.MaxHealth - 1f);
                PrototypeUnit blue = fixture.Units(Side.Player, UnitKind.Archer)[0];
                PrototypeUnit red = fixture.Units(Side.Opponent, UnitKind.Archer)[0];
                blue.transform.position = new Vector3(25.5f, 0.85f, 0.5f);
                red.transform.position = new Vector3(-25.5f, 0.85f, 0.5f);
                fixture.Orders.Interact(new[] { blue }, rivalHall);
                fixture.Orders.Interact(new[] { red }, playerHall);
            }

            Require(fixture.Match.Outcome == MatchOutcome.Ongoing, "The result was evaluated before the simulation step.");
            fixture.Tick(1);
            Require(fixture.Match.Outcome == expected, $"Wrong match result: expected {expected}.");
            var layout = new HudLayout(1366, 768, true);
            Require(!(bool)Invoke(fixture.Controls, "TryFocusMinimap", layout,
                layout.MinimapContent(PrototypeSceneBuilder.MatchMap).center), "Minimap input was active after the match.");
            float orderTime = Get<float>(fixture.Orders, "time");
            fixture.Tick(100);
            Require(orderTime == Get<float>(fixture.Orders, "time") && fixture.Match.Outcome == expected,
                "The result did not stop and preserve simulation state.");
        }

        private static void CheckDefense(Fixture fixture)
        {
            PrototypeUnit blue = fixture.Units(Side.Player, UnitKind.Infantry)[0];
            PrototypeUnit red = fixture.Units(Side.Opponent, UnitKind.Infantry)[0];
            fixture.Units(Side.Player, UnitKind.Archer)[0].transform.position = new Vector3(-29.5f, 0.85f, 20.5f);
            var post = new Vector3(-23.5f, 0.85f, 0.5f);
            blue.transform.position = post;
            red.transform.position = new Vector3(-19.5f, 0.85f, 0.5f);
            CombatTarget blueActor = blue.GetComponent<CombatTarget>();
            CombatTarget redActor = red.GetComponent<CombatTarget>();
            fixture.Orders.Interact(new[] { red }, blueActor);
            fixture.Until(() => fixture.Orders.StateOf(blue) == UnitState.Defend, 40);
            Vector3 pausedPosition = blue.transform.position;
            fixture.Hud.SetPaused(true);
            fixture.Tick(200);
            Require(blue.transform.position == pausedPosition && redActor.Health == redActor.MaxHealth &&
                fixture.Orders.IsAttacking(blue, redActor), "Pause changed a pending self-defense order.");
            fixture.Hud.SetPaused(false);
            fixture.Until(() => redActor.Health < redActor.MaxHealth, 150);
            Require(fixture.Orders.StateOf(fixture.Units(Side.Player, UnitKind.Worker)[0]) == UnitState.Idle,
                "A worker responded with an attack.");
            red.transform.position = new Vector3(10.5f, 0.85f, 0.5f);
            fixture.Orders.Halt(red);
            fixture.Tick(1);
            Require(fixture.Orders.StateOf(blue) == UnitState.ReturnToPost,
                "The defender chased outside its defense zone.");
            fixture.Until(() => At(blue, post) && fixture.Orders.StateOf(blue) == UnitState.Idle, 230);

            red.transform.position = new Vector3(-19.5f, 0.85f, 1.5f);
            fixture.Until(() => fixture.Orders.StateOf(blue) == UnitState.Defend, 120);
            var retreat = new Vector3(-21.5f, 0.85f, -10.5f);
            fixture.Orders.Move(new[] { blue }, retreat);
            fixture.Tick(35);
            Require(fixture.Orders.StateOf(blue) == UnitState.Move, "Auto-defense cancelled a manual retreat.");
            fixture.Until(() => At(blue, retreat) && fixture.Orders.StateOf(blue) == UnitState.Idle, 240);
            red.transform.position = new Vector3(-19.5f, 0.85f, -10.5f);
            fixture.Until(() => fixture.Orders.IsAttacking(blue, redActor), 100);
            CombatTarget chosen = fixture.Hall(Side.Opponent);
            fixture.Orders.Interact(new[] { blue }, chosen);
            fixture.Tick(65);
            Require(fixture.Orders.StateOf(blue) == UnitState.Attack && fixture.Orders.IsAttacking(blue, chosen),
                "A nearby threat replaced the explicitly chosen attack target.");
        }

        private static void CheckBaseProtection(Fixture fixture)
        {
            PrototypeUnit defender = fixture.Units(Side.Player, UnitKind.Infantry)[0];
            var post = new Vector3(-24.5f, 0.85f, 0.5f);
            defender.transform.position = post;
            fixture.Units(Side.Player, UnitKind.Archer)[0].transform.position = new Vector3(-23.5f, 0.85f, 20.5f);
            PrototypeUnit redArcher = fixture.Units(Side.Opponent, UnitKind.Archer)[0];
            redArcher.transform.position = new Vector3(-32.5f, 0.85f, 0.5f);
            CombatTarget redActor = redArcher.GetComponent<CombatTarget>();
            fixture.Orders.Interact(new[] { redArcher }, fixture.Hall(Side.Player));
            fixture.Until(() => fixture.Orders.IsAttacking(defender, redActor) && fixture.Match.FirstBaseAttackAt >= 0, 60);
            float firstAlert = fixture.Match.FirstBaseAttackAt;
            float nextAlert = Get<float>(fixture.Match, "nextAttackAlert");
            fixture.Tick(55);
            Require(fixture.Match.FirstBaseAttackAt == firstAlert &&
                Get<float>(fixture.Match, "nextAttackAlert") == nextAlert, "Repeated shots reset the base alert.");
            redActor.Damage(redActor.MaxHealth);
            fixture.Until(() => At(defender, post) && fixture.Orders.StateOf(defender) == UnitState.Idle, 250);

            PrototypeUnit worker = fixture.Units(Side.Player, UnitKind.Worker)[0];
            worker.transform.position = new Vector3(-19.5f, 0.85f, 0.5f);
            PrototypeUnit attacker = fixture.Units(Side.Opponent, UnitKind.Infantry)[0];
            attacker.transform.position = new Vector3(-16.5f, 0.85f, 0.5f);
            fixture.Orders.Interact(new[] { attacker }, worker.GetComponent<CombatTarget>());
            fixture.Until(() => fixture.Orders.IsAttacking(defender, attacker.GetComponent<CombatTarget>()), 120);
            Require(!worker.CanAttack, "A threatened worker gained a weapon.");
        }

        private static void CheckPlayerDevelopment(Fixture fixture)
        {
            PrototypeUnit builder = fixture.Units(Side.Player, UnitKind.Worker)[0];
            var settings = fixture.Construction.Blueprints;
            var barracks = settings.Single(item => item.Kind == BuildingKind.Barracks);
            var house = settings.Single(item => item.Kind == BuildingKind.House);
            PrototypeEconomy.Product Product(UnitKind kind) => fixture.Player.Products.Single(item => item.Kind == kind);
            Require(fixture.Player.Enqueue(fixture.Hall(Side.Player), Product(UnitKind.Worker)),
                "Starting gold did not cover a second worker.");
            CombatTarget producer = fixture.Construction.TryBuild(barracks, builder, new Vector3(-29.5f, 0, 5.5f));
            Require(producer != null, "The player could not place or afford a starting barracks.");
            fixture.Until(() => fixture.Units(Side.Player, UnitKind.Worker).Length == 2, 220);
            PrototypeUnit miner = fixture.Units(Side.Player, UnitKind.Worker).Single(unit => unit != builder);
            fixture.Orders.Gather(new[] { miner }, fixture.Mines.Single(item => item.name == "Player Gold"));
            fixture.Until(() => producer.IsOperational, 550);
            CombatTarget expansion = fixture.Construction.TryBuild(house, builder, new Vector3(-33.5f, 0, -4.5f));
            Require(expansion != null, "The player could not build a house during development.");
            fixture.Until(() => expansion.IsOperational && fixture.Player.Gold >= 70, 2250);
            Require(fixture.Player.Enqueue(producer, Product(UnitKind.Infantry)) &&
                fixture.Player.Enqueue(producer, Product(UnitKind.Archer)), "The player could not fund two defenders.");
            fixture.Until(() => fixture.Units(Side.Player, UnitKind.Infantry).Length == 2 &&
                fixture.Units(Side.Player, UnitKind.Archer).Length == 2, 600);
            float ready = Get<float>(fixture.Match, "elapsed");
            Require(ready < 150 && fixture.Director.RaidsLaunched == 0,
                "Player development took too long for the training window.");
            Debug.Log($"Training balance player: barracks, house, second worker and two extra soldiers ready at {ready:F2}s.");
        }

        private static bool At(PrototypeUnit unit, Vector3 point) =>
            GridNavigation.ToPlane(unit.transform.position - point).sqrMagnitude < 0.15f;

        private static T Get<T>(object instance, string field) =>
            (T)instance.GetType().GetField(field, PrivateInstance).GetValue(instance);

        private static object Invoke(object instance, string method, params object[] parameters) =>
            instance.GetType().GetMethod(method, PrivateInstance).Invoke(instance, parameters);

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class Fixture
        {
            public readonly PrototypeControls Controls;
            public readonly PrototypeEconomy Player;
            public readonly PrototypeEconomy Rival;
            public readonly PrototypeConstruction Construction;
            public readonly UnitOrders Orders;
            public readonly OpponentDirector Director;
            public readonly PrototypeMatch Match;
            public readonly PrototypeInterface Hud;
            public readonly List<CombatTarget> Actors;
            public readonly GoldDeposit[] Mines;

            public Fixture(bool runDirector, bool durablePlayerHall = false, bool startMatch = true)
            {
                Controls = UnityEngine.Object.FindFirstObjectByType<PrototypeControls>();
                Director = Controls.GetComponent<OpponentDirector>();
                Construction = Controls.GetComponent<PrototypeConstruction>();
                Hud = Controls.GetComponent<PrototypeInterface>();
                Match = Controls.GetComponent<PrototypeMatch>();
                foreach (CombatTarget actor in UnityEngine.Object.FindObjectsByType<CombatTarget>(FindObjectsSortMode.None))
                {
                    actor.Initialize();
                    if (durablePlayerHall && actor.Side == Side.Player && actor.Building == BuildingKind.TownHall)
                    {
                        var settings = new SerializedObject(actor);
                        settings.FindProperty("maxHealth").floatValue = 12000;
                        settings.ApplyModifiedPropertiesWithoutUndo();
                        actor.Initialize();
                    }
                }

                Mines = UnityEngine.Object.FindObjectsByType<GoldDeposit>(FindObjectsSortMode.None);
                foreach (GoldDeposit mine in Mines)
                {
                    mine.Initialize();
                }
                Invoke(Controls, "Awake");
                if (startMatch)
                {
                    Match.StartMatch();
                }
                if (!runDirector)
                {
                    typeof(PrototypeControls).GetField("director", PrivateInstance).SetValue(Controls, null);
                }
                Actors = Get<List<CombatTarget>>(Controls, "activeTargets");
                Orders = Get<UnitOrders>(Controls, "orders");
                Player = Get<PrototypeEconomy>(Controls, "economy");
                Rival = Get<PrototypeEconomy>(Controls, "opponentEconomy");
            }

            public CombatTarget Hall(Side side) => Actors.Single(actor =>
                actor.Side == side && actor.Building == BuildingKind.TownHall && actor.IsOperational);

            public PrototypeUnit[] Units(Side side, UnitKind kind) => Actors.Where(actor =>
                actor.Side == side && actor.Unit != null && actor.Unit.Kind == kind).Select(actor => actor.Unit).ToArray();

            public void Tick(int steps)
            {
                for (int index = 0; index < steps; index++)
                {
                    Invoke(Controls, "AdvanceSimulation", 0.02f);
                }
            }

            public void Until(Func<bool> done, int steps)
            {
                for (int index = 0; index < steps && !done(); index++)
                {
                    Tick(1);
                }
                string units = string.Join(", ", Actors.Where(actor => actor.Side == Side.Opponent && actor.Unit != null)
                    .Select(actor => $"{actor.Unit.Kind} {actor.transform.position} {Orders.StateOf(actor.Unit)}"));
                Require(done(), $"Match scenario exceeded its time limit. {Match.Outcome}, AI {Director.Mode}, raids {Director.RaidsLaunched}, rival gold {Rival.Gold}, population {Rival.Used}/{Rival.Maximum}, reserved {Rival.Reserved}. {units}");
            }
        }
    }
}
