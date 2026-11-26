using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rts
{
    public sealed partial class PrototypeUnit
    {
        internal Vector3[] CaptureRoute() => waypoints.ToArray();
    }
    public sealed partial class CombatTarget
    {
        internal void RestoreHealth(float value) => Health = value;
    }
    public sealed partial class GoldDeposit
    {
        internal void RestoreStock(int value) => Stock = value;
    }
    public sealed partial class ConstructionSite
    {
        internal float SavedDuration => duration;
        internal float SavedElapsed => elapsed;
        internal void RestoreProgress(float value) => elapsed = value;
    }
    public sealed partial class PrototypeMatch
    {
        internal void RestoreClock(float value)
        {
            elapsed = value;
            HasStarted = true;
            startFrame = Time.frameCount;
            Outcome = MatchOutcome.Ongoing;
        }
    }
    public sealed partial class PrototypeEconomy
    {
        internal EconomySave CaptureEconomy() => new EconomySave
        {
            side = Owner, gold = Gold, used = Used, reserved = Reserved, maximum = Maximum,
            jobs = queues.SelectMany(pair => pair.Value.Select(job => new JobSave
            {
                building = SaveIdentity.Of(pair.Key), kind = job.Product.Kind,
                remaining = job.Remaining, retry = job.RetryIn
            })).ToArray()
        };
        internal void RestoreEconomy(EconomySave data, Dictionary<string, CombatTarget> actors)
        {
            Gold = data.gold;
            // Recomputed from operational buildings and living units, never credited twice.
            if (Used != data.used || Maximum != data.maximum) throw new InvalidOperationException("Населення не відповідає об’єктам карти.");
            Reserved = 0;
            foreach (var saved in data.jobs)
            {
                var product = Products.Single(p => p.Kind == saved.kind);
                if (saved.remaining > product.Seconds + .001f) throw new InvalidOperationException("Час виробництва не відповідає правилам матчу.");
                queues[actors[saved.building]].Enqueue(new Job(product) { Remaining = saved.remaining, RetryIn = saved.retry });
                Reserved++;
            }
        }
        internal CombatTarget CreateSavedUnit(ActorSave saved)
        {
            var prefab = Products.Single(p => p.Kind == saved.kind).Prefab;
            var unit = Instantiate(prefab, saved.position, saved.rotation);
            unit.transform.localScale = saved.scale;
            unit.SetTeam(saved.side, unitMaterial);
            var actor = unit.GetComponent<CombatTarget>();
            actor.Initialize();
            if (Mathf.Abs(actor.MaxHealth - saved.maxHealth) > .001f) throw new InvalidOperationException("Характеристики юніта змінилися.");
            unit.CarriedGold = saved.cargo;
            unit.SetSelected(false);
            return actor;
        }
    }
    public sealed partial class PrototypeConstruction
    {
        internal CombatTarget CreateSavedBuilding(ActorSave saved)
        {
            var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = saved.building.ToString();
            item.transform.SetPositionAndRotation(saved.position, saved.rotation);
            item.transform.localScale = saved.scale;
            var renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = saved.side == Side.Player ? buildingMaterial : opponentMaterial;
            ConstructionSite site = null;
            if (saved.constructionDuration > 0)
            {
                site = new ConstructionSite(saved.constructionDuration, saved.workRange, target =>
                {
                    renderer.SetPropertyBlock(null);
                    (saved.side == Side.Player ? economy : opponentEconomy).RegisterBuilding(target);
                });
                site.RestoreProgress(saved.constructionElapsed);
                var tint = new MaterialPropertyBlock();
                tint.SetColor(BaseColor, new Color(.65f, .45f, .25f));
                renderer.SetPropertyBlock(tint);
            }
            var actor = item.AddComponent<CombatTarget>();
            actor.InitializeConstruction(saved.building, saved.side, saved.maxHealth, site);
            site?.Attach(actor);
            return actor;
        }
    }
    public sealed partial class MatchVisibility
    {
        internal KnowledgeSave CaptureKnowledge(Side side) => new KnowledgeSave
        {
            explored = cells[(int)side].Select(value => (int)value).ToArray(),
            buildings = CaptureBuildings(side),
            mines = obstacles.Where(body => body != null && body.GetComponent<GoldDeposit>() != null)
                .Select(body => body.GetComponent<GoldDeposit>()).Where(mine => IsKnown(side, mine))
                .Select(mine => new MineKnowledge { id = SaveIdentity.Of(mine), stock = KnownStock(side, mine) }).ToArray()
        };
        internal void RestoreKnowledge(KnowledgeSave[] data, Dictionary<string, GoldDeposit> sources)
        {
            for (int side = 0; side < 2; side++)
            {
                cells[side] = data[side].explored.Select(value => value == 0 ? VisibilityState.Unexplored : VisibilityState.Explored).ToArray();
                RestoreBuildings((Side)side, data[side].buildings);
                knownMines[side].Clear(); mineStock[side].Clear();
                foreach (var mine in data[side].mines)
                {
                    int key = sources[mine.id].GetInstanceID();
                    knownMines[side].Add(key); mineStock[side][key] = mine.stock;
                }
            }
            Refresh();
        }
    }
    public sealed partial class UnitOrders
    {
        internal float SavedTime => time;
        internal OrderSave CaptureOrder(PrototypeUnit unit)
        {
            var o = orders[unit];
            return new OrderSave
            {
                state = o.State == UnitState.Build && (o.Target == null || o.Target.IsOperational) ? UnitState.Idle : o.State,
                target = o.Target != null && o.Target.IsAlive ? SaveIdentity.Of(o.Target) : null, source = SaveIdentity.Of(o.Source),
                blockedThreat = o.BlockedThreat != null && o.BlockedThreat.IsAlive ? SaveIdentity.Of(o.BlockedThreat) : null, nextAction = o.NextAction,
                nextPlan = o.NextPlan, nextScan = o.NextScan, harvestAt = o.HarvestAt,
                lastProgress = o.LastProgress, retryThreatAt = o.RetryThreatAt, failedPlans = o.FailedPlans,
                lastTargetPosition = o.LastTargetPosition, defensePost = o.DefensePost, breachGoal = o.BreachGoal,
                progressPosition = o.ProgressPosition, selfDefense = o.SelfDefense
            };
        }
        internal void RestoreOrders(MatchSave data, Dictionary<string, CombatTarget> actors, Dictionary<string, GoldDeposit> sources)
        {
            time = data.orderTime;
            CombatTarget Target(string id) => !string.IsNullOrEmpty(id) ? actors[id] : null;
            foreach (var a in data.actors.Where(a => a.building == BuildingKind.None))
            {
                var unit = actors[a.id].Unit; var s = a.order; var o = orders[unit];
                o.State = s.state; o.Target = Target(s.target); o.Source = !string.IsNullOrEmpty(s.source) ? sources[s.source] : null;
                o.BlockedThreat = Target(s.blockedThreat); o.NextAction = s.nextAction; o.NextPlan = s.nextPlan;
                o.NextScan = s.nextScan; o.HarvestAt = s.harvestAt; o.LastProgress = s.lastProgress;
                o.RetryThreatAt = s.retryThreatAt; o.FailedPlans = s.failedPlans;
                o.LastTargetPosition = s.lastTargetPosition; o.DefensePost = s.defensePost; o.BreachGoal = s.breachGoal;
                o.ProgressPosition = s.progressPosition; o.SelfDefense = s.selfDefense;
                // A hidden target is only a remembered destination, never a live tracking reference.
                if (o.Target != null && o.Target.Side != unit.Team && !CanSee(unit.Team, o.Target))
                {
                    o.Target = null;
                    o.State = UnitState.Move;
                    o.BreachGoal = o.LastTargetPosition;
                    unit.Stop();
                    movement.TryMoveNear(unit, o.LastTargetPosition, p => (p - o.LastTargetPosition).sqrMagnitude < 16, 4);
                }
            }
        }
    }
    public sealed partial class OpponentDirector
    {
        internal DirectorSave CaptureDirector() => new DirectorSave
        {
            elapsed = elapsed, nextDecision = nextDecision, nextRaid = nextRaid, scoutRetry = scoutRetry,
            lastRaidAt = LastRaidAt, explorationOffset = explorationOffset, nextReserveKind = nextReserveKind,
            raidsLaunched = RaidsLaunched, lastRaidSize = LastRaidSize, mode = Mode,
            scout = scout != null && scout.IsAvailable ? SaveIdentity.Of(scout) : null,
            defenseTarget = defenseTarget != null && defenseTarget.IsAlive ? SaveIdentity.Of(defenseTarget) : null, scoutGoal = scoutGoal,
            raiders = raiders.Where(u => u != null && u.IsAvailable).Select(u => SaveIdentity.Of(u)).ToArray(),
            garrison = garrison.Where(u => u != null && u.IsAvailable).Select(u => SaveIdentity.Of(u)).ToArray(),
            assembly = assembly.Where(u => u != null && u.IsAvailable).Select(u => SaveIdentity.Of(u)).ToArray(),
            threats = localThreats.Where(a => a != null && a.IsAlive).Select(a => SaveIdentity.Of(a)).ToArray(),
            stations = stations.Where(p => p.Key != null && p.Key.IsAvailable).Select(p => new StationSave
            { unit = SaveIdentity.Of(p.Key), role = (int)p.Value.Role, slot = p.Value.Slot,
                positioned = p.Value.Position.HasValue, position = p.Value.Position ?? default, retry = p.Value.RetryAt }).ToArray(),
            contacts = contactExpiry.Where(p => p.Key != null && p.Key.IsAlive).Select(p => new ContactSave { target = SaveIdentity.Of(p.Key), expires = p.Value }).ToArray(),
            attempts = siteAttempts.Select(p => new SiteAttemptSave { kind = p.Key, attempts = p.Value }).ToArray()
        };
        internal void RestoreDirector(DirectorSave s, Dictionary<string, CombatTarget> actors)
        {
            elapsed = s.elapsed; nextDecision = s.nextDecision; nextRaid = s.nextRaid; scoutRetry = s.scoutRetry;
            LastRaidAt = s.lastRaidAt; explorationOffset = s.explorationOffset; nextReserveKind = s.nextReserveKind;
            RaidsLaunched = s.raidsLaunched; LastRaidSize = s.lastRaidSize; Mode = s.mode; scoutGoal = s.scoutGoal;
            scout = string.IsNullOrEmpty(s.scout) ? null : actors[s.scout].Unit;
            defenseTarget = string.IsNullOrEmpty(s.defenseTarget) ? null : actors[s.defenseTarget];
            void Set(HashSet<PrototypeUnit> set, string[] ids) { set.Clear(); foreach (string id in ids) set.Add(actors[id].Unit); }
            Set(raiders, s.raiders); Set(garrison, s.garrison); Set(assembly, s.assembly);
            localThreats.Clear(); contactExpiry.Clear(); stations.Clear(); siteAttempts.Clear();
            foreach (string id in s.threats) if (CanSee(actors[id])) localThreats.Add(actors[id]);
            foreach (var c in s.contacts) if (CanSee(actors[c.target])) contactExpiry.Add(actors[c.target], c.expires);
            if (defenseTarget != null && !CanSee(defenseTarget)) defenseTarget = null;
            foreach (var p in s.stations) stations.Add(actors[p.unit].Unit, new Station
            { Role = (Role)p.role, Slot = p.slot, Position = p.positioned ? p.position : (Vector3?)null, RetryAt = p.retry });
            foreach (var a in s.attempts) siteAttempts.Add(a.kind, a.attempts);
        }
    }
}
