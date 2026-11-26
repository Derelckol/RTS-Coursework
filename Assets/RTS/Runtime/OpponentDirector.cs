using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rts
{
    public enum OpponentMode { Develop, Assemble, Raid, Defend }

    public sealed partial class OpponentDirector : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float detectionRadius = 12f;
        [SerializeField, Min(0.1f)] private float baseDefenseRadius = 14f;
        [SerializeField, Min(0.1f)] private float pursuitRadius = 18f;
        [SerializeField, Min(0)] private int reserveResponders = 2;
        [SerializeField] private MonoBehaviour visibilitySource;
        [SerializeField, Min(0.1f)] private float rallyRadius = 4f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField] private bool traceDefense;
#endif

        private enum Role { Guard, Reserve, Assault }

        private sealed class Station
        {
            public Role Role;
            public int Slot;
            public Vector3? Position;
            public float RetryAt;
        }

        private static readonly Vector2Int[] BuildDirections =
        {
            new Vector2Int(0, 1), new Vector2Int(-1, 1), new Vector2Int(1, 1), new Vector2Int(-1, 0),
            new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, -1), new Vector2Int(1, -1)
        };

        private readonly Dictionary<BuildingKind, int> siteAttempts = new Dictionary<BuildingKind, int>();
        private readonly HashSet<PrototypeUnit> raiders = new HashSet<PrototypeUnit>();
        private readonly HashSet<PrototypeUnit> garrison = new HashSet<PrototypeUnit>();
        private readonly HashSet<PrototypeUnit> assembly = new HashSet<PrototypeUnit>();
        private readonly HashSet<CombatTarget> localThreats = new HashSet<CombatTarget>();
        private readonly Dictionary<CombatTarget, float> contactExpiry = new Dictionary<CombatTarget, float>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly Dictionary<int, (string reason, float at)> defenseReports =
            new Dictionary<int, (string reason, float at)>();
#endif
        private readonly Dictionary<PrototypeUnit, Station> stations = new Dictionary<PrototypeUnit, Station>();
        private readonly List<GoldDeposit> mines = new List<GoldDeposit>();
        private List<CombatTarget> actors;
        private PrototypeEconomy economy;
        private PrototypeConstruction construction;
        private UnitOrders orders;
        private ISideVisibility visibility;
        private MatchVisibility fog;
        private PrototypeUnit scout;
        private Vector3 scoutGoal;
        private float scoutRetry;
        private int explorationOffset;
        private float elapsed;
        private float nextDecision;
        private float nextRaid;
        private CombatTarget defenseTarget;
        private int nextReserveKind;
        public DifficultyProfile Profile { get; private set; }

        public OpponentMode Mode { get; private set; }
        public int RaidsLaunched { get; private set; }
        public int LastRaidSize { get; private set; }
        public float LastRaidAt { get; private set; } = -1f;
        public string ProductionReason { get; private set; } = "Waiting for match start";
        public string RaidReason { get; private set; } = "Waiting for match start";
#if UNITY_EDITOR
        public string DebugRoster
        {
            get
            {
                if (economy == null)
                {
                    return string.Empty;
                }

                int workers = actors.Count(actor => actor.Side == Side.Opponent &&
                    actor.Unit != null && actor.Unit.Kind == UnitKind.Worker);
                int soldiers = actors.Count(actor => actor.Side == Side.Opponent &&
                    actor.Unit != null && actor.Unit.CanAttack);
                var queues = actors.Where(actor => actor.Side == Side.Opponent &&
                        actor.Unit == null && actor.IsOperational)
                    .Select(actor => new { actor.DisplayName, Queue = economy.QueueSummary(actor) })
                    .Where(entry => !string.IsNullOrEmpty(entry.Queue))
                    .Select(entry => $"{entry.DisplayName}: {entry.Queue}");
                string report = string.Join("\n", queues);
                return $"State: {Mode} | Gold: {economy.Gold}\n" +
                    $"Population: {economy.Used}/{economy.Maximum} (+{economy.Reserved} reserved)\n" +
                    $"Workers: {workers}/{Profile.DesiredWorkers} (+{economy.Pending(UnitKind.Worker)} queued)\n" +
                    $"Military: {soldiers} | Garrison: {garrison.Count} | " +
                    $"Reserve: {stations.Count(entry => entry.Value.Role == Role.Reserve)} | " +
                    $"Assembling: {assembly.Count} | Raiders: {raiders.Count}\n" +
                    $"Production: {ProductionReason}\nRaid: {RaidReason}\nDefense: {DefenseReason}\n" +
                    (string.IsNullOrEmpty(orders.LastTacticalIssue) ? string.Empty : $"{orders.LastTacticalIssue}\n") +
                    (string.IsNullOrEmpty(report) ? "Queues: empty" : report);
            }
        }
#endif

        public void Initialize(PrototypeEconomy resources, PrototypeConstruction building,
            UnitOrders unitOrders, List<CombatTarget> liveActors, IReadOnlyList<Collider> obstacles,
            DifficultyProfile profile)
        {
            economy = resources;
            construction = building;
            orders = unitOrders;
            visibility = visibilitySource as ISideVisibility;
            fog = visibility as MatchVisibility;
            orders.ConfigureOpponentDefense(pursuitRadius, CanSee, localThreats.Contains,
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                TraceDefense
#else
                null
#endif
            );
            actors = liveActors;
            Profile = profile;
            nextRaid = profile.FirstRaidDelay;
            mines.Clear();
            foreach (Collider body in obstacles)
            {
                if (body.TryGetComponent(out GoldDeposit source))
                {
                    mines.Add(source);
                }
            }
        }

        public void Tick(float deltaTime)
        {
            elapsed += deltaTime;
            if (elapsed < nextDecision)
            {
                return;
            }

            nextDecision = elapsed + Mathf.Max(0.1f, Profile.DecisionInterval);
            Decide();
        }

        private void Decide()
        {
            CombatTarget[] owned = actors.Where(actor => actor.IsAlive && actor.Side == Side.Opponent).ToArray();
            CombatTarget home = owned.FirstOrDefault(actor => actor.IsOperational &&
                actor.Building == BuildingKind.TownHall);
            if (home == null)
            {
                ProductionReason = "No operational town hall";
                RaidReason = ProductionReason;
                return;
            }

            raiders.RemoveWhere(unit => unit == null || !unit.IsAvailable);
            garrison.RemoveWhere(unit => unit == null || !unit.IsAvailable);
            PrototypeUnit[] workers = owned.Where(actor => actor.Unit != null && actor.Unit.Kind == UnitKind.Worker)
                .Select(actor => actor.Unit).ToArray();
            PrototypeUnit[] soldiers = owned.Where(actor => actor.Unit != null && actor.Unit.CanAttack)
                .Select(actor => actor.Unit).ToArray();
            CombatTarget[] barracks = owned.Where(actor => actor.IsOperational &&
                actor.Building == BuildingKind.Barracks).ToArray();
            foreach (PrototypeUnit guard in soldiers.Where(unit => !raiders.Contains(unit) && !garrison.Contains(unit))
                         .OrderBy(unit => DistanceSquared(unit.transform.position, home.transform.position))
                         .Take(Mathf.Max(0, Profile.GarrisonSize - garrison.Count)))
            {
                garrison.Add(guard);
            }
            UnitKind[] formation = Formation();
            Develop(home, barracks, owned, workers, soldiers, formation);

            Defend(home, owned, soldiers);
            Explore(home, soldiers, workers);

            CombatTarget objective = actors.Where(actor => actor.IsAlive && actor.IsOperational &&
                    actor.Side == Side.Player && actor.Building == BuildingKind.TownHall && CanSee(actor))
                .OrderBy(actor => (actor.transform.position - home.transform.position).sqrMagnitude)
                .FirstOrDefault();
            bool knownBase = objective != null;
            Vector3 rememberedBase = objective != null ? objective.transform.position : default;
            if (fog != null && !knownBase)
                knownBase = fog.TryKnownBase(Side.Opponent, out rememberedBase);
            if (objective != null) Attack(raiders, objective);
            else if (knownBase)
                foreach (PrototypeUnit unit in raiders.Where(unit => orders.StateOf(unit) == UnitState.Idle))
                    orders.Move(new[] { unit }, rememberedBase);
            var available = soldiers.Where(unit => unit != scout && !raiders.Contains(unit) && !garrison.Contains(unit)).ToArray();
            Assemble(available, formation);
            Vector3 rally = AssaultAnchor(home);
            float readinessRadius = rallyRadius + 2f;
            bool NearRally(PrototypeUnit unit) =>
                DistanceSquared(unit.transform.position, rally) <= readinessRadius * readinessRadius;
            MaintainStations(home, owned, soldiers);

            Mode = defenseTarget != null ? OpponentMode.Defend : raiders.Count > 0 ? OpponentMode.Raid :
                barracks.Length == 0 ? OpponentMode.Develop : OpponentMode.Assemble;
            if (elapsed < nextRaid)
            {
                RaidReason = $"Next departure in {nextRaid - elapsed:F1}s";
                return;
            }
            if (!knownBase)
            {
                RaidReason = "Searching for the enemy base";
                return;
            }

            var ready = available.Where(unit => assembly.Contains(unit) && NearRally(unit) &&
                !orders.IsResponding(unit)).ToList();
            var squad = new List<PrototypeUnit>();
            foreach (UnitKind kind in formation)
            {
                PrototypeUnit member = ready.Find(unit => unit.Kind == kind);
                if (member != null)
                {
                    ready.Remove(member);
                    squad.Add(member);
                }
            }

            if (squad.Count < formation.Length)
            {
                int minimum = Mathf.Min(formation.Length, Mathf.Max(2, formation.Length - 1));
                if (elapsed < nextRaid + Profile.RaidWait || squad.Count + ready.Count < minimum)
                {
                    RaidReason = $"Assembling {squad.Count + ready.Count}/{formation.Length} at rally; " +
                        $"{squad.Count} match preferred roles";
                    return;
                }
                squad.AddRange(ready.Take(formation.Length - squad.Count));
            }

            raiders.UnionWith(squad);
            assembly.ExceptWith(squad);
            if (objective != null) Attack(squad, objective);
            else orders.Move(squad, rememberedBase);
            if (defenseTarget == null)
            {
                Mode = OpponentMode.Raid;
            }
            RaidsLaunched++;
            LastRaidSize = squad.Count;
            LastRaidAt = elapsed;
            nextRaid = elapsed + Mathf.Max(0.1f, Profile.RaidInterval);
            RaidReason = $"Wave {RaidsLaunched} departed with {LastRaidSize}";
        }

        private void Explore(CombatTarget home, PrototypeUnit[] soldiers, PrototypeUnit[] workers)
        {
            if (fog == null) return;
            bool needBase = !fog.TryKnownBase(Side.Opponent, out _);
            bool needMine = !mines.Any(mine => fog.KnownStock(Side.Opponent, mine) > 0);
            if (!needBase && !needMine)
            {
                scout = null;
                return;
            }
            if (scout == null || !scout.IsAvailable)
                scout = soldiers.Where(unit => !raiders.Contains(unit) && !garrison.Contains(unit))
                    .FirstOrDefault() ?? soldiers.FirstOrDefault(unit => !raiders.Contains(unit)) ??
                    workers.FirstOrDefault(unit => unit.CarriedGold == 0 &&
                        orders.StateOf(unit) != UnitState.Build);
            if (scout == null || orders.IsResponding(scout)) return;
            if (orders.StateOf(scout) == UnitState.Move &&
                DistanceSquared(scout.transform.position, scoutGoal) > 1f) return;
            if (elapsed < scoutRetry) return;
            scoutRetry = elapsed + 2f;
            if (fog.TryExplorationPoint(Side.Opponent, scout.transform.position, explorationOffset, out scoutGoal))
            {
                orders.Move(new[] { scout }, scoutGoal);
                explorationOffset = orders.StateOf(scout) == UnitState.Move ? 0 : explorationOffset + 1;
            }
        }

        private static float DistanceSquared(Vector3 left, Vector3 right) =>
            GridNavigation.ToPlane(left - right).sqrMagnitude;

        private static Vector3 TowardFront(CombatTarget home) =>
            home.transform.position.x >= 0f ? Vector3.left : Vector3.right;

        private static Vector3 AssaultAnchor(CombatTarget home) =>
            home.transform.position + TowardFront(home) * 12f + Vector3.forward * 2f;

        private static Vector3 ReserveAnchor(CombatTarget home) =>
            home.transform.position + TowardFront(home) * 8f + Vector3.forward * 7f;

        private void Assemble(PrototypeUnit[] available, UnitKind[] formation)
        {
            var eligible = new HashSet<PrototypeUnit>(available);
            assembly.RemoveWhere(unit => !eligible.Contains(unit));
            foreach (var group in formation.GroupBy(kind => kind))
            {
                int missing = group.Count() - assembly.Count(unit => unit.Kind == group.Key);
                foreach (PrototypeUnit unit in available.Where(unit => unit.Kind == group.Key &&
                             !assembly.Contains(unit)).Take(Mathf.Max(0, missing)))
                {
                    assembly.Add(unit);
                }
            }

            if (elapsed >= nextRaid + Profile.RaidWait)
            {
                foreach (PrototypeUnit unit in available.Where(unit => !assembly.Contains(unit))
                             .Take(Mathf.Max(0, formation.Length - assembly.Count)))
                {
                    assembly.Add(unit);
                }
            }
        }

        private void MaintainStations(CombatTarget home, CombatTarget[] owned, PrototypeUnit[] soldiers)
        {
            foreach (PrototypeUnit unit in stations.Keys.Where(unit => unit == null || !unit.IsAvailable ||
                         raiders.Contains(unit)).ToArray())
            {
                stations.Remove(unit);
            }

            var minesNearHome = mines.Where(source => source != null &&
                (fog == null ? source.Stock : fog.KnownStock(Side.Opponent, source)) > 0 &&
                DistanceSquared(source.transform.position, home.transform.position) <=
                baseDefenseRadius * baseDefenseRadius).ToArray();
            foreach (PrototypeUnit unit in soldiers)
            {
                if (raiders.Contains(unit) || unit == scout)
                {
                    continue;
                }

                Role role = garrison.Contains(unit) ? Role.Guard : assembly.Contains(unit) ? Role.Assault : Role.Reserve;
                if (!stations.TryGetValue(unit, out Station station) || station.Role != role)
                {
                    int slot = 0;
                    while (stations.Values.Any(other => other.Role == role && other.Slot == slot))
                    {
                        slot++;
                    }
                    station = new Station { Role = role, Slot = slot };
                    stations[unit] = station;
                    if (orders.StateOf(unit) == UnitState.Move)
                    {
                        orders.Halt(unit);
                    }
                }

                if (orders.StateOf(unit) != UnitState.Idle || elapsed < station.RetryAt)
                {
                    continue;
                }

                bool Available(Vector3 point) => IsParkingArea(point, home, owned, minesNearHome) &&
                    !stations.Any(entry => entry.Key != unit && entry.Value.Position.HasValue &&
                        DistanceSquared(entry.Value.Position.Value, point) < 2.56f);
                station.RetryAt = elapsed + 2f;
                if (station.Position is Vector3 saved && Available(saved))
                {
                    if (DistanceSquared(unit.transform.position, saved) < 0.20f)
                    {
                        continue;
                    }
                    if (orders.MoveNear(unit, saved, 0.18f, Available))
                    {
                        continue;
                    }
                }

                Vector3 anchor = role switch
                {
                    Role.Assault => AssaultAnchor(home),
                    Role.Reserve => ReserveAnchor(home),
                    _ => GuardAnchor(home, minesNearHome, station.Slot)
                };
                station.Position = orders.MoveNear(unit, anchor, rallyRadius, Available) ? unit.Destination : null;
            }
        }

        private static Vector3 GuardAnchor(CombatTarget home, GoldDeposit[] mines, int slot)
        {
            Vector3 position = home.transform.position;
            return (slot % 3) switch
            {
                0 => position + TowardFront(home) * 6f + Vector3.back * 2f,
                1 when mines.Length > 0 => mines.OrderBy(mine => DistanceSquared(position, mine.transform.position))
                    .First().transform.position + TowardFront(home) * 4f + Vector3.back * 2f,
                _ => position - TowardFront(home) * 3f + Vector3.forward * 5f
            };
        }

        private static bool IsParkingArea(Vector3 point, CombatTarget home, CombatTarget[] owned, GoldDeposit[] mines)
        {
            if (owned.Any(building => building.Unit == null && building.DistanceFrom(point, 0f) < 2.6f))
            {
                return false;
            }
            Vector2 from = GridNavigation.ToPlane(home.transform.position);
            Vector2 offset = GridNavigation.ToPlane(point) - from;
            foreach (GoldDeposit mine in mines)
            {
                Vector2 lane = GridNavigation.ToPlane(mine.transform.position) - from;
                float along = Mathf.Clamp01(Vector2.Dot(offset, lane) / Mathf.Max(0.01f, lane.sqrMagnitude));
                if ((offset - lane * along).sqrMagnitude < 4f ||
                    CombatTarget.DistanceToFootprint(point, 0f, mine.Body.bounds) < 2.2f)
                {
                    return false;
                }
            }
            return true;
        }

        public string DefenseReason { get; private set; } = "No local threat";

        public void ContactDefeated(CombatTarget target)
        {
            if (localThreats.Contains(target))
            {
                nextDecision = elapsed;
            }
        }

        private void Defend(CombatTarget home, CombatTarget[] owned, PrototypeUnit[] soldiers)
        {
            // Grace applies only to currently visible enemies crossing the zone edge.
            // Hidden contacts are dropped immediately, without reading their position.
            var threats = actors.Where(actor =>
            {
                if (!actor.IsAlive || actor.Side != Side.Player || actor.Unit == null || !CanSee(actor))
                {
                    return false;
                }
                if (IsThreat(actor, home, owned))
                {
                    contactExpiry[actor] = elapsed + 1.5f;
                    return true;
                }
                return contactExpiry.TryGetValue(actor, out float expiry) && elapsed < expiry &&
                    DistanceSquared(actor.transform.position, home.transform.position) <= pursuitRadius * pursuitRadius;
            }).ToArray();
            var detected = new HashSet<CombatTarget>(threats);
            foreach (CombatTarget target in localThreats)
            {
                if (!detected.Contains(target))
                {
                    orders.ClearDefenseContact(target);
                    contactExpiry.Remove(target);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    TraceDefense(null, target, "contact left; previous refusals cleared");
#endif
                }
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            foreach (CombatTarget target in threats)
            {
                if (!localThreats.Contains(target))
                {
                    TraceDefense(null, target, "contact detected by the base");
                }
            }
#endif
            localThreats.Clear();
            localThreats.UnionWith(threats);
            var assignmentCount = new Dictionary<CombatTarget, int>();
            int responders = 0;
            int reserveHelp = 0;
            foreach (PrototypeUnit unit in soldiers)
            {
                CombatTarget target = orders.ActiveResponse(unit);
                if (target != null)
                {
                    assignmentCount.TryGetValue(target, out int count);
                    assignmentCount[target] = count + 1;
                    responders++;
                    if (!garrison.Contains(unit))
                    {
                        reserveHelp++;
                    }
                }
            }
            defenseTarget = threats.OrderBy(actor =>
                DistanceSquared(actor.transform.position, home.transform.position)).FirstOrDefault();
            if (defenseTarget != null)
            {
                bool critical = threats.Any(threat =>
                {
                    CombatTarget victim = orders.ThreatenedAsset(threat.Unit, Side.Opponent);
                    return victim != null && (victim.Building == BuildingKind.TownHall ||
                        victim.Unit != null && victim.Unit.Kind == UnitKind.Worker);
                });
                float threatStrength = threats.Sum(threat => CombatStrength(threat.Unit));
                bool assault = threats.Length >= 3 || critical;
                float committedStrength = soldiers.Where(orders.IsResponding).Sum(CombatStrength);
                float requiredStrength = threatStrength * 1.25f;
                foreach (PrototypeUnit unit in soldiers.OrderBy(unit => !garrison.Contains(unit))
                    .ThenBy(unit => raiders.Contains(unit)).ThenBy(unit => assembly.Contains(unit)).ThenBy(unit =>
                    DistanceSquared(unit.transform.position, defenseTarget.transform.position)))
                {
                    if (orders.IsResponding(unit))
                    {
                        ReportDefense(unit, "active defense");
                        continue;
                    }
                    bool guard = garrison.Contains(unit);
                    bool locallyAvailable = !raiders.Contains(unit) ||
                        DistanceSquared(unit.transform.position, home.transform.position) <=
                            baseDefenseRadius * baseDefenseRadius;
                    if (!locallyAvailable)
                    {
                        ReportDefense(unit, "skip: raider outside base, continuing mission");
                        continue;
                    }
                    if (!assault && !guard && committedStrength >= requiredStrength)
                    {
                        ReportDefense(unit, $"skip: scout covered, committed strength {committedStrength:F1} >= {requiredStrength:F1}");
                        continue;
                    }
                    bool accepted = false;
                    foreach (CombatTarget threat in threats
                        .OrderByDescending(target => orders.IsThreatening(target.Unit, Side.Opponent))
                        .ThenBy(target => assignmentCount.TryGetValue(target, out int count) ? count : 0)
                        .ThenBy(target => DistanceSquared(target.transform.position, unit.transform.position)))
                    {
                        if (!orders.TryRespond(unit, threat, home.transform.position))
                        {
                            continue;
                        }
                        assignmentCount.TryGetValue(threat, out int assigned);
                        assignmentCount[threat] = assigned + 1;
                        responders++;
                        reserveHelp += guard ? 0 : 1;
                        committedStrength += CombatStrength(unit);
                        accepted = true;
                        ReportDefense(unit, assault ? "engaged: group or critical asset threatened" : "engaged: scout support");
                        break;
                    }
                    if (!accepted)
                    {
                        ReportDefense(unit, "skip: all visible targets refused; inspect route/cooldown records");
                    }
                }
                DefenseReason = responders > 0 ? $"{responders} responding to local threats" :
                    "No available soldier can reach a local threat";
                return;
            }

            DefenseReason = "No local threat";
            foreach (PrototypeUnit unit in soldiers)
            {
                ReportDefense(unit, orders.IsResponding(unit) ? "self defense" : "no visible local threat; returning to role");
            }
        }

        private static float CombatStrength(PrototypeUnit unit) =>
            unit.Damage / Mathf.Max(0.05f, unit.AttackInterval) *
            Mathf.Sqrt(Mathf.Clamp01(unit.GetComponent<CombatTarget>().Health /
                unit.GetComponent<CombatTarget>().MaxHealth));

        private void ReportDefense(PrototypeUnit unit, string reason)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!traceDefense)
            {
                return;
            }
            int id = unit.GetInstanceID();
            string category = reason.StartsWith("skip: scout covered") ? "skip: scout covered" : reason;
            if (defenseReports.TryGetValue(id, out var previous) &&
                previous.reason == category && elapsed - previous.at < 2f)
            {
                return;
            }
            defenseReports[id] = (category, elapsed);
            TraceDefense(unit, null, $"{reason}; {orders.DefenseProgress(unit)}");
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void TraceDefense(PrototypeUnit unit, CombatTarget target, string message)
        {
            if (!traceDefense)
            {
                return;
            }

            string actor = unit == null ? "base" : $"{unit.Kind}#{unit.GetInstanceID()} " +
                (garrison.Contains(unit) ? "guard" : raiders.Contains(unit) ? "raider" :
                    assembly.Contains(unit) ? "assembly" : "reserve");
            string contact = target == null ? "-" : $"{target.DisplayName}#{target.GetInstanceID()}";
            Debug.Log($"DEF {elapsed:F2} {actor} -> {contact}: {message}");
        }
#endif

        private UnitKind[] Formation()
        {
            UnitKind[] kinds = Profile.SquadComposition.Where(kind => kind != UnitKind.Worker).ToArray();
            if (kinds.Length == 0)
            {
                kinds = new[] { UnitKind.Infantry };
            }

            int size = Mathf.Clamp(Profile.FirstSquadSize + RaidsLaunched * Profile.SquadGrowth,
                1, Mathf.Max(1, Profile.MaxSquadSize));
            var formation = new UnitKind[size];
            for (int index = 0; index < formation.Length; index++)
            {
                formation[index] = kinds[index % kinds.Length];
            }
            return formation;
        }

        private void Develop(CombatTarget home, CombatTarget[] barracks, CombatTarget[] owned,
            PrototypeUnit[] workers, PrototypeUnit[] soldiers, UnitKind[] formation)
        {
            int queued = barracks.Sum(economy.PendingAt);
            int standing = soldiers.Count(unit => !raiders.Contains(unit));
            int rosterGap = formation.Length + Profile.GarrisonSize + Profile.ReserveSoldiers - standing - queued;
            int roleGap = formation.GroupBy(kind => kind).Sum(entry =>
                RaidRoleDeficit(soldiers, entry.Key, entry.Count()));
            int gap = Mathf.Max(rosterGap, roleGap);
            bool nearCap = economy.Used + economy.Reserved >= economy.Maximum - Profile.CapacityBuffer;
            int barracksCost = construction.Blueprints.First(item => item.Kind == BuildingKind.Barracks).Cost;
            int houseCost = construction.Blueprints.First(item => item.Kind == BuildingKind.House).Cost;
            int cheapestSoldier = economy.Products.Where(item => item.Kind != UnitKind.Worker).Min(item => item.Cost);
            string barracksProgress = string.Empty;
            if (barracks.Length == 0 ||
                (barracks.Length < Profile.BarracksLimit && gap >= Profile.MilitaryQueueLimit &&
                 economy.Gold >= barracksCost + cheapestSoldier * 2))
            {
                barracksProgress = EnsureConstruction(BuildingKind.Barracks, home, owned, workers);
            }
            bool pendingHouse = owned.Any(actor => actor.Building == BuildingKind.House && !actor.IsOperational);
            if (nearCap || pendingHouse)
            {
                EnsureConstruction(BuildingKind.House, home, owned, workers);
            }

            GoldDeposit mine = mines.Where(source => source != null &&
                    (fog == null ? source.Stock : fog.KnownStock(Side.Opponent, source)) > 0)
                .OrderBy(source => (source.transform.position - home.transform.position).sqrMagnitude)
                .FirstOrDefault();
            if (mine != null)
            {
                foreach (PrototypeUnit worker in workers)
                {
                    if (worker != scout && orders.StateOf(worker) == UnitState.Idle)
                    {
                        orders.Gather(new[] { worker }, mine);
                    }
                }
            }

            if (workers.Length == 0 && economy.Pending(UnitKind.Worker) == 0)
            {
                OrderProduction(home, UnitKind.Worker);
            }
            if (barracks.Length == 0)
            {
                ProductionReason = $"Barracks: {barracksProgress}";
            }
            else
            {
                TrainMilitary(barracks, soldiers, formation, gap, rosterGap);
            }

            // Recover a single worker at a time, leaving the next military purchase funded.
            var workerProduct = economy.Products.First(item => item.Kind == UnitKind.Worker);
            int reservedGold = barracks.Length == 0 && !actors.Any(actor => actor.Side == Side.Opponent &&
                actor.Building == BuildingKind.Barracks) ? barracksCost :
                nearCap && !pendingHouse ? houseCost : gap > 0 ? cheapestSoldier : 0;
            if (workers.Length > 0 && workers.Length < Profile.DesiredWorkers &&
                economy.Pending(UnitKind.Worker) == 0 && economy.Gold >= workerProduct.Cost + reservedGold &&
                (gap <= 0 || barracks.Length == 0 || economy.Used + economy.Reserved < economy.Maximum - 1))
            {
                OrderProduction(home, UnitKind.Worker);
            }
        }

        private int RaidRoleDeficit(PrototypeUnit[] soldiers, UnitKind kind, int target)
        {
            int ready = soldiers.Count(unit => unit.Kind == kind && !raiders.Contains(unit) && !garrison.Contains(unit));
            return Mathf.Max(0, target - ready - economy.Pending(kind));
        }

        private void TrainMilitary(CombatTarget[] barracks, PrototypeUnit[] soldiers, UnitKind[] formation,
            int gap, int rosterGap)
        {
            if (barracks.Any(economy.IsExitBlocked))
            {
                ProductionReason = "Completed unit waiting for a free exit";
            }
            else if (gap <= 0)
            {
                ProductionReason = "Standing army and reserve target reached";
                return;
            }
            else
            {
                ProductionReason = "Training queues at priority limit";
            }

            foreach (CombatTarget producer in barracks)
            {
                if (gap <= 0 || economy.IsExitBlocked(producer) ||
                    economy.PendingAt(producer) >= Profile.MilitaryQueueLimit)
                {
                    continue;
                }
                if (economy.Used + economy.Reserved >= economy.Maximum)
                {
                    ProductionReason = "Waiting for population capacity";
                    return;
                }

                var preferred = formation.GroupBy(kind => kind)
                    .Where(entry => RaidRoleDeficit(soldiers, entry.Key, entry.Count()) > 0)
                    .Select(entry => entry.Key).ToArray();
                if (preferred.Length == 0)
                {
                    preferred = new[] { formation[nextReserveKind % formation.Length] };
                }

                bool accepted = preferred.Any(kind => OrderProduction(producer, kind));
                if (!accepted && rosterGap >= 2)
                {
                    accepted = OrderProduction(producer, UnitKind.Infantry);
                }
                if (!accepted)
                {
                    ProductionReason = "Saving gold for a needed unit";
                    continue;
                }

                nextReserveKind++;
                gap--;
                rosterGap--;
                ProductionReason = "Military order accepted";
            }
        }

        private bool OrderProduction(CombatTarget producer, UnitKind kind)
        {
            PrototypeEconomy.Product product = economy.Products.FirstOrDefault(item => item.Kind == kind);
            if (product != null && economy.Gold >= product.Cost && economy.Used + economy.Reserved < economy.Maximum)
            {
                return economy.Enqueue(producer, product);
            }
            return false;
        }

        private string EnsureConstruction(BuildingKind kind, CombatTarget home, CombatTarget[] owned,
            PrototypeUnit[] workers)
        {
            CombatTarget unfinished = owned.FirstOrDefault(actor => actor.Building == kind && !actor.IsOperational);
            if (unfinished != null && unfinished.Site.Builder != null)
            {
                return "under construction";
            }

            PrototypeUnit worker = workers.FirstOrDefault(unit => orders.StateOf(unit) == UnitState.Idle) ??
                workers.FirstOrDefault(unit => orders.StateOf(unit) == UnitState.Gather && unit.CarriedGold == 0);
            if (worker == null)
            {
                return "no available builder";
            }

            if (unfinished != null)
            {
                orders.Interact(new[] { worker }, unfinished);
                return "builder reassigned";
            }

            PrototypeConstruction.Blueprint blueprint = construction.Blueprints.First(item => item.Kind == kind);
            if (economy.Gold < blueprint.Cost)
            {
                return "saving gold";
            }

            siteAttempts.TryGetValue(kind, out int next);
            const int rings = 4;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int slot = (next + attempt) % (BuildDirections.Length * rings);
                Vector2Int direction = BuildDirections[slot % BuildDirections.Length];
                float distance = 5f + 2f * (slot / BuildDirections.Length);
                Vector3 site = home.transform.position + new Vector3(direction.x, 0, direction.y) * distance;
                // Keep space for traffic between producers and nearby structures.
                float margin = Mathf.Max(blueprint.Size.x, blueprint.Size.z) * 0.5f + 1.25f;
                if (owned.Any(asset => asset.Unit == null &&
                    asset.DistanceFrom(site, 0f) < margin))
                {
                    continue;
                }
                if (construction.TryBuild(blueprint, worker, site) != null)
                {
                    siteAttempts[kind] = (slot + 1) % (BuildDirections.Length * rings);
                    return "foundation paid";
                }
            }

            siteAttempts[kind] = (next + 4) % (BuildDirections.Length * rings);
            return "searching for a reachable site with clearance";
        }

        private bool CanSee(CombatTarget actor) => visibility == null || visibility.IsVisible(Side.Opponent, actor);

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var home = FindObjectsByType<CombatTarget>(FindObjectsSortMode.None).FirstOrDefault(actor =>
                actor.Side == Side.Opponent && actor.Building == BuildingKind.TownHall && actor.IsOperational);
            if (home == null)
            {
                return;
            }

            var original = UnityEditor.Handles.color;
            Ring(home.transform.position, baseDefenseRadius, new Color(0.25f, 0.75f, 1f), "Зона бази");
            Ring(home.transform.position, pursuitRadius, new Color(0.7f, 0.45f, 1f), "Межа переслідування");
            Ring(AssaultAnchor(home), rallyRadius, new Color(1f, 0.6f, 0.23f), "Збір загону");
            Ring(ReserveAnchor(home), rallyRadius, new Color(0.25f, 0.9f, 0.5f), "Резерв");
            foreach (PrototypeUnit unit in FindObjectsByType<PrototypeUnit>(FindObjectsSortMode.None))
            {
                if (unit.Team != Side.Opponent ||
                    DistanceSquared(unit.transform.position, home.transform.position) >
                    baseDefenseRadius * baseDefenseRadius)
                {
                    continue;
                }
                Ring(unit.transform.position, detectionRadius, new Color(1f, 0.85f, 0.25f, 0.45f));
                if (unit.CanAttack)
                {
                    Ring(unit.transform.position, unit.AttackRange, new Color(1f, 0.27f, 0.27f));
                }
            }

            foreach (Station station in stations.Values)
            {
                if (station.Position is Vector3 position)
                {
                    Ring(position, 0.4f, station.Role == Role.Guard ? Color.cyan :
                        station.Role == Role.Reserve ? Color.green : new Color(1f, 0.6f, 0.23f));
                }
            }
            UnityEditor.Handles.color = original;
        }

        private static void Ring(Vector3 center, float radius, Color color, string label = null)
        {
            center.y = 0.06f;
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, radius);
            if (label != null)
            {
                UnityEditor.Handles.Label(center + Vector3.forward * radius, label);
            }
        }
#endif

        private bool IsThreat(CombatTarget actor, CombatTarget home, CombatTarget[] owned)
        {
            if (!actor.IsAlive || actor.Side != Side.Player || actor.Unit == null || !CanSee(actor))
            {
                return false;
            }
            Vector3 point = actor.transform.position;
            return DistanceSquared(point, home.transform.position) <= baseDefenseRadius * baseDefenseRadius &&
                owned.Any(observer =>
                    DistanceSquared(observer.transform.position, home.transform.position) <=
                        baseDefenseRadius * baseDefenseRadius &&
                    DistanceSquared(point, observer.transform.position) <= detectionRadius * detectionRadius);
        }

        private void Attack(IEnumerable<PrototypeUnit> soldiers, CombatTarget target)
        {
            var available = soldiers.Where(unit => !orders.IsResponding(unit) &&
                !orders.IsAttacking(unit, target)).ToArray();
            if (available.Length > 0)
            {
                orders.Interact(available, target);
            }
        }
    }
}
