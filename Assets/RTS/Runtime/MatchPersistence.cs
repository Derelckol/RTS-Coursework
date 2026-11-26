using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rts
{
    public sealed partial class PrototypeControls
    {
        private SaveWindow saveWindow;
        public MatchSave CaptureSave()
        {
            if (match == null || !match.HasStarted || match.HasEnded) throw new InvalidOperationException("Збереження доступне лише під час матчу.");
            var sources = activeObstacles.Where(c => c != null && c.GetComponent<GoldDeposit>() != null).Select(c => c.GetComponent<GoldDeposit>()).ToArray();
            foreach (var a in activeTargets) SaveIdentity.Of(a);
            foreach (var m in sources) SaveIdentity.Of(m);
            return new MatchSave
            {
                difficulty = MatchSession.Selected, time = match.ElapsedSeconds, camera = CaptureCamera(), orderTime = orders.SavedTime,
                actors = activeTargets.Where(a => a != null && a.IsAlive).Select(a => new ActorSave
                {
                    id = SaveIdentity.Of(a), side = a.Side, building = a.Building, kind = a.Unit != null ? a.Unit.Kind : default,
                    position = a.transform.position, rotation = a.transform.rotation, scale = a.transform.localScale,
                    upgraded = a.Upgraded, upgradeRemaining = a.UpgradeRemaining, nextBuildingAttack = a.NextBuildingAttack,
                    health = a.Health, maxHealth = a.MaxHealth, cargo = a.Unit != null ? a.Unit.CarriedGold : 0,
                    constructionDuration = !a.IsOperational ? a.Site.SavedDuration : 0,
                    constructionElapsed = !a.IsOperational ? a.Site.SavedElapsed : 0,
                    workRange = !a.IsOperational ? a.Site.WorkRange : 0,
                    builder = !a.IsOperational ? SaveIdentity.Of(a.Site.Builder) : null,
                    order = a.Unit != null ? orders.CaptureOrder(a.Unit) : null,
                    route = a.Unit != null ? a.Unit.CaptureRoute() : null,
                    blocked = a.Unit != null ? a.Unit.BlockedTime : 0,
                    replans = a.Unit != null ? a.Unit.ReplanAttempts : 0
                }).ToArray(),
                mines = sources.Select(m => new MineSave { id = SaveIdentity.Of(m), position = m.transform.position, stock = m.Stock }).ToArray(),
                economies = new[] { economy.CaptureEconomy(), opponentEconomy.CaptureEconomy() },
                knowledge = new[] { fog.CaptureKnowledge(Side.Player), fog.CaptureKnowledge(Side.Opponent) },
                director = director.CaptureDirector()
            };
        }

        internal void RestoreSave(MatchSave save)
        {
            SaveStore.Validate(save);
            construction.Cancel(); selected.Clear(); selectedBuilding = null; selectedSource = null; selecting = false;
            foreach (var old in activeTargets.ToArray())
            {
                old.Defeated -= OnDefeated;
                activeObstacles.Remove(old.Body);
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            activeTargets.Clear(); activeUnits.Clear();
            var sources = new Dictionary<string, GoldDeposit>();
            foreach (var m in save.mines)
            {
                var mine = activeObstacles.Where(c => c != null).Select(c => c.GetComponent<GoldDeposit>())
                    .SingleOrDefault(candidate => candidate != null && (candidate.transform.position - m.position).sqrMagnitude < .001f);
                if (mine == null) throw new InvalidOperationException("Родовища збереження не відповідають цій карті.");
                SaveIdentity.Assign(mine, m.id); mine.RestoreStock(m.stock); sources.Add(m.id, mine);
            }
            var actors = new Dictionary<string, CombatTarget>();
            // First pass creates all objects. No economy credit, commands, or ticks occur here.
            foreach (var a in save.actors)
            {
                var actor = a.building == BuildingKind.None
                    ? (a.side == Side.Player ? economy : opponentEconomy).CreateSavedUnit(a)
                    : construction.CreateSavedBuilding(a);
                SceneManager.MoveGameObjectToScene(actor.gameObject, gameObject.scene);
                SaveIdentity.Assign(actor, a.id); actor.RestoreHealth(a.health);
                actor.RestoreUpgrade(a.upgraded, a.upgradeRemaining, a.nextBuildingAttack);
                MatchActorVisual.Attach(actor, this);
                actor.Defeated += OnDefeated;
                activeTargets.Add(actor); actors.Add(a.id, actor);
                if (actor.Unit != null) activeUnits.Add(actor.Unit);
                else
                {
                    activeObstacles.Add(actor.Body);
                    landscape?.AddApproach(actor.Body);
                }
            }
            Physics.SyncTransforms();
            movement = new UnitMovement(mapBounds, CollectFootprints(), activeUnits.ToArray(), economy.ProductionClearance());
            orders = new UnitOrders(movement, activeTargets.ToArray(), economy, opponentEconomy, match);
            economy.Initialize(movement, activeTargets, RegisterSpawn);
            opponentEconomy.Initialize(movement, activeTargets, RegisterSpawn);
            construction.Initialize(mapBounds, economy, movement, orders, CollectFootprints, RegisterBuilding, opponentEconomy);
            director.Initialize(opponentEconomy, construction, orders, activeTargets, activeObstacles, match.Difficulty);
            orders.SetVisibility(fog);
            orders.Hit += OnCombatHit;
            fog.RestoreKnowledge(save.knowledge, sources);
            // Second pass reconnects stable IDs, then restores routes/reservations in deterministic order.
            foreach (var a in save.actors)
            {
                var actor = actors[a.id];
                if (actor.Site != null && !string.IsNullOrEmpty(a.builder)) actor.Site.Builder = actors[a.builder].Unit;
                if (actor.Unit != null)
                {
                    actor.Unit.SetRoute(a.route);
                    actor.Unit.BlockedTime = a.blocked; actor.Unit.ReplanAttempts = a.replans;
                }
            }
            movement.UpdateObstacles(CollectFootprints(), reroute: true);
            orders.RestoreOrders(save, actors, sources);
            director.RestoreDirector(save.director, actors);
            economy.RestoreEconomy(save.economies[0], actors); opponentEconomy.RestoreEconomy(save.economies[1], actors);
            match.RestoreClock(save.time); RestoreCamera(save.camera);
            interfaceHud.SetPaused(true);
        }
    }

    // Staging keeps the previous scene alive and paused until restoration succeeds.
    public sealed class SaveLoader : MonoBehaviour
    {
        internal static MatchSave Pending;
        internal static string Failure;
        public static bool Busy { get; private set; }
        public static string Message { get; private set; }
        internal static void ClearMessage() => Message = null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Pending = null; Failure = null; Busy = false; Message = null; }
        public static void Load(MatchSave data)
        {
            SaveStore.Validate(data);
            if (Busy) return;
            Busy = true; Message = null;
            foreach (var hud in FindObjectsByType<PrototypeInterface>(FindObjectsSortMode.None)) hud.SetPaused(true);
            var host = new GameObject("Save loading transaction").AddComponent<SaveLoader>();
            DontDestroyOnLoad(host.gameObject);
            host.StartCoroutine(host.Run(data));
        }
        private IEnumerator Run(MatchSave data)
        {
            var previous = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) previous.Add(SceneManager.GetSceneAt(i));
            var selected = MatchSession.Selected;
            MatchSession.SelectSaved(data.difficulty);
            Pending = data; Failure = null;
            AsyncOperation operation = null;
            try
            {
                operation = SceneManager.LoadSceneAsync(MatchSession.MatchScene, LoadSceneMode.Additive);
            }
            catch (Exception error)
            {
                Failure = error.Message;
            }
            if (operation == null)
            {
                Pending = null; Busy = false; MatchSession.SelectSaved(selected);
                Message = "Не вдалося відкрити карту: " + Failure; Destroy(gameObject); yield break;
            }
            yield return operation;
            Scene staging = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);
            Pending = null;
            if (Failure != null)
            {
                yield return SceneManager.UnloadSceneAsync(staging);
                MatchSession.SelectSaved(selected);
                Message = "Не вдалося відновити матч: " + Failure;
            }
            else
            {
                SceneManager.SetActiveScene(staging);
                foreach (var old in previous)
                {
                    foreach (var root in old.GetRootGameObjects()) root.SetActive(false);
                    yield return SceneManager.UnloadSceneAsync(old);
                }
            }
            Busy = false; Destroy(gameObject);
        }
    }
}
