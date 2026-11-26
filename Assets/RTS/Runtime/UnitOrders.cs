using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public enum UnitState { Idle, Move, Attack, Repair, Gather, Deliver, Build, Defend, ReturnToPost, Respond, Breach, ResumeBreach }

    public sealed partial class UnitOrders
    {
        private sealed class Order
        {
            public UnitState State;
            public CombatTarget Target;
            public float NextAction;
            public float NextPlan;
            public float NextScan;
            public int FailedPlans;
            public Vector3 LastTargetPosition;
            public GoldDeposit Source;
            public float HarvestAt;
            public Vector3 DefensePost;
            public Vector3 BreachGoal;
            public Vector3 ProgressPosition;
            public float LastProgress;
            public CombatTarget BlockedThreat;
            public float RetryThreatAt;
            public bool SelfDefense;
            public float NextRefusalTrace;
        }

        private readonly UnitMovement movement;
        private readonly List<CombatTarget> targets = new List<CombatTarget>();
        private readonly PrototypeEconomy economy;
        private readonly PrototypeEconomy opponentEconomy;
        private readonly PrototypeMatch training;
        private readonly Dictionary<PrototypeUnit, Order> orders = new Dictionary<PrototypeUnit, Order>();
        private float time;
        private MatchVisibility fog;
        public void SetVisibility(MatchVisibility visibility) => fog = visibility;
        private bool CanSee(Side side, CombatTarget target) => fog == null || fog.IsVisible(side, target);
        private float opponentPursuit = 12f;
        private System.Predicate<CombatTarget> opponentVisibility;
        private System.Predicate<CombatTarget> opponentLocalThreat;
        private System.Action<PrototypeUnit, CombatTarget, string> defenseTrace;
        public string LastTacticalIssue { get; private set; }

        public UnitOrders(UnitMovement movement, CombatTarget[] targets, PrototypeEconomy economy = null,
            PrototypeEconomy opponentEconomy = null, PrototypeMatch training = null)
        {
            this.movement = movement;
            this.economy = economy;
            this.opponentEconomy = opponentEconomy;
            this.training = training;
            foreach (CombatTarget target in targets)
            {
                Register(target);
            }
        }

        private PrototypeEconomy EconomyFor(PrototypeUnit unit) => unit.PlayerOwned ? economy : opponentEconomy;

        public void ConfigureOpponentDefense(float pursuit, System.Predicate<CombatTarget> visible,
            System.Predicate<CombatTarget> localThreat, System.Action<PrototypeUnit, CombatTarget, string> trace)
        {
            opponentPursuit = pursuit;
            opponentVisibility = visible;
            opponentLocalThreat = localThreat;
            defenseTrace = trace;
        }

        public void ClearDefenseContact(CombatTarget threat)
        {
            foreach (Order order in orders.Values)
            {
                if (order.BlockedThreat == threat)
                {
                    order.BlockedThreat = null;
                    order.RetryThreatAt = 0f;
                }
            }
        }

        public CombatTarget ActiveResponse(PrototypeUnit unit)
        {
            if (!orders.TryGetValue(unit, out Order order) || order.State != UnitState.Respond)
            {
                return null;
            }

            CombatTarget target = order.Target;
            string reason = ResponseEndReason(order);
            if (reason == null)
            {
                return target;
            }

            defenseTrace?.Invoke(unit, target, reason);
            Idle(unit, order);
            return null;
        }

        private string ResponseEndReason(Order order)
        {
            CombatTarget target = order.Target;
            if (target == null || !target.IsAlive)
            {
                return "response ended: target gone";
            }
            if (opponentVisibility != null && !opponentVisibility(target))
            {
                return "response ended: target no longer visible";
            }
            if (!order.SelfDefense && opponentLocalThreat != null && !opponentLocalThreat(target))
            {
                return "response ended: contact left defense zone";
            }
            return DistanceSquared(target.transform.position, order.DefensePost) > opponentPursuit * opponentPursuit
                ? "response ended: pursuit limit" : null;
        }

        public void Register(CombatTarget target)
        {
            targets.Add(target);
            if (target.Unit != null)
            {
                orders.Add(target.Unit, new Order());
            }
        }

        public UnitState StateOf(PrototypeUnit unit) => orders.TryGetValue(unit, out Order order)
            ? order.State : UnitState.Idle;

        public bool IsAttacking(PrototypeUnit unit, CombatTarget target) =>
            orders.TryGetValue(unit, out Order order) &&
            (order.State == UnitState.Attack || order.State == UnitState.Defend ||
             order.State == UnitState.Respond || order.State == UnitState.Breach) && order.Target == target;

        internal Vector3? VisualFocus(PrototypeUnit unit, out bool working)
        {
            working = false;
            if (!orders.TryGetValue(unit, out var order)) return null;
            if (order.State == UnitState.Gather && order.Source != null)
            {
                working = order.HarvestAt > 0 && !unit.IsMoving;
                return order.Source.transform.position;
            }
            var target = order.Target;
            if (target == null || !target.IsAlive || !CanSee(unit.Team, target)) return null;
            working = order.State == UnitState.Build && target.Site != null &&
                target.Site.Builder == unit && target.DistanceFrom(unit.transform.position, unit.Radius) <= target.Site.WorkRange;
            return target.transform.position;
        }

        public bool IsResponding(PrototypeUnit unit) => StateOf(unit) == UnitState.Respond;

        public bool IsThreatening(PrototypeUnit attacker, Side side)
        {
            return orders.TryGetValue(attacker, out Order order) && order.Target != null &&
                order.Target.IsAlive && order.Target.Side == side && IsAttacking(attacker, order.Target) &&
                order.Target.DistanceFrom(attacker.transform.position, attacker.Radius) <= attacker.AttackRange + 2f;
        }

        public CombatTarget ThreatenedAsset(PrototypeUnit attacker, Side side) =>
            IsThreatening(attacker, side) ? orders[attacker].Target : null;

        public string DefenseProgress(PrototypeUnit unit)
        {
            if (!orders.TryGetValue(unit, out Order order))
            {
                return "unit not registered";
            }
            return $"state={order.State} pos={unit.transform.position:F2} destination={unit.Destination:F2} " +
                $"waypoints={unit.RemainingWaypoints} blocked={unit.BlockedTime:F2}s " +
                $"replans={unit.ReplanAttempts} target=" +
                (order.Target == null ? "-" : $"{order.Target.DisplayName}#{order.Target.GetInstanceID()}");
        }

        public bool TryRespond(PrototypeUnit unit, CombatTarget threat, Vector3 anchor, bool selfDefense = false)
        {
            if (!unit.CanAttack || !threat.IsAlive || threat.Side == unit.Team ||
                (opponentVisibility != null && !opponentVisibility(threat)) ||
                !orders.TryGetValue(unit, out Order order))
            {
                return false;
            }
            if (order.State == UnitState.Respond && order.Target == threat)
            {
                order.SelfDefense |= selfDefense;
                return true;
            }
            if (order.State == UnitState.Respond && (!selfDefense || order.SelfDefense))
            {
                return true;
            }
            if (order.BlockedThreat == threat && time < order.RetryThreatAt)
            {
                TraceRefusal(unit, threat, order, "retry cooldown after unreachable position");
                return false;
            }
            if (DistanceSquared(anchor, threat.transform.position) > opponentPursuit * opponentPursuit)
            {
                TraceRefusal(unit, threat, order, "target beyond pursuit boundary");
                return false;
            }

            bool inRange = movement.CanStrike(unit, threat, unit.transform.position);
            if (!inRange && !movement.TryReachDefensePosition(unit, threat,
                    point => DistanceSquared(point, anchor) <= opponentPursuit * opponentPursuit, continuing: false))
            {
                BlockThreat(unit, order, threat);
                return false;
            }

            order.DefensePost = anchor;
            order.SelfDefense = selfDefense;
            UnitState previous = order.State;
            Begin(unit, order, UnitState.Respond, threat, keepRoute: !inRange);
            defenseTrace?.Invoke(unit, threat, $"{previous} -> Respond; " +
                (inRange ? "in weapon range" : $"position reserved {unit.Destination:F2}"));
            return true;
        }

        private void TraceRefusal(PrototypeUnit unit, CombatTarget target, Order order, string reason)
        {
            if (time < order.NextRefusalTrace)
            {
                return;
            }
            order.NextRefusalTrace = time + 2f;
            defenseTrace?.Invoke(unit, target, $"refused: {reason}");
        }

        public void Halt(PrototypeUnit unit)
        {
            if (orders.TryGetValue(unit, out Order order))
            {
                Idle(unit, order);
            }
        }

        public void Move(IReadOnlyList<PrototypeUnit> group, Vector3 point)
        {
            if (training == null)
            {
                movement.IssueOrder(group, point, BeginMove);
                return;
            }

            var ordinary = new List<PrototypeUnit>();
            foreach (PrototypeUnit unit in group)
            {
                if (fog != null && fog.State(unit.Team, point) != VisibilityState.Visible &&
                    !movement.IsDestinationTraversable(point))
                {
                    MoveNear(unit, point, 4f);
                    continue;
                }
                if (movement.HasRoute(unit, point))
                {
                    ordinary.Add(unit);
                }
                else if (orders.TryGetValue(unit, out Order order))
                {
                    Idle(unit, order);
                    order.BreachGoal = point;
                    PlanBreach(unit, order);
                }
            }
            movement.IssueOrder(ordinary, point, BeginMove);
        }

        private void PlanBreach(PrototypeUnit unit, Order order)
        {
            if (movement.HasRoute(unit, order.BreachGoal))
            {
                movement.IssueOrder(new[] { unit }, order.BreachGoal, BeginMove);
                if (order.State == UnitState.ResumeBreach)
                {
                    ReportBlockedMove(unit, order);
                }
                return;
            }

            var buildings = new List<CombatTarget>();
            if (unit.CanAttack)
            {
                foreach (CombatTarget target in targets)
                {
                    if (target != null && target.IsAlive && target.Side != unit.Team &&
                        target.Building != BuildingKind.None && CanSee(unit.Team, target))
                    {
                        buildings.Add(target);
                    }
                }
            }

            CombatTarget obstacle = movement.TryPlanBreach(unit, order.BreachGoal, buildings);
            if (obstacle == null)
            {
                ReportBlockedMove(unit, order);
                return;
            }

            Begin(unit, order, UnitState.Breach, obstacle, keepRoute: true);
        }

        private void ReportBlockedMove(PrototypeUnit unit, Order order)
        {
            Idle(unit, order);
            LastTacticalIssue = $"{unit.Kind}: no available route or reachable breach";
            if (unit.PlayerOwned)
            {
                economy?.Notify("Немає доступного маршруту або проходу через ворожу споруду.");
            }
        }

        public bool MoveNear(PrototypeUnit unit, Vector3 point, float radius,
            System.Predicate<Vector3> available = null)
        {
            if (movement.TryMoveNear(unit, point,
                    candidate => GridNavigation.ToPlane(candidate - point).sqrMagnitude <= radius * radius &&
                        (available == null || available(candidate)), radius))
            {
                BeginMove(unit);
                return true;
            }

            return false;
        }

        private void BeginMove(PrototypeUnit unit)
        {
            if (orders.TryGetValue(unit, out Order order))
            {
                ReleaseSite(unit, order);
                order.Target = null;
                order.Source = null;
                order.State = UnitState.Move;
                order.BreachGoal = unit.Destination;
            }
        }

        public void StartBuild(PrototypeUnit worker, ConstructionSite site, IReadOnlyList<Vector3> route = null)
        {
            if (!worker.IsAvailable || worker.Kind != UnitKind.Worker || worker.Team != site.Target.Side ||
                !site.Target.IsAlive || site.Completed || (site.Builder != null && site.Builder != worker) ||
                !orders.TryGetValue(worker, out Order order))
            {
                return;
            }

            Begin(worker, order, UnitState.Build, site.Target);
            site.Builder = worker;
            if (route != null)
            {
                worker.ReplaceRoute(route);
            }
        }

        public void Gather(IReadOnlyList<PrototypeUnit> group, GoldDeposit source)
        {
            foreach (PrototypeUnit unit in group)
            {
                if (source == null || fog != null && !fog.IsKnown(unit.Team, source)) continue;
                if (EconomyFor(unit) == null || unit.Kind != UnitKind.Worker ||
                    !orders.TryGetValue(unit, out Order order))
                {
                    continue;
                }

                Begin(unit, order, unit.CarriedGold > 0 ? UnitState.Deliver : UnitState.Gather, null);
                order.Source = source;
            }
        }

        public void Interact(IReadOnlyList<PrototypeUnit> group, CombatTarget target)
        {
            if (target == null || !target.IsAlive)
            {
                return;
            }

            foreach (PrototypeUnit unit in group)
            {
                Interact(unit, target);
            }
        }

        private void Interact(PrototypeUnit unit, CombatTarget target)
        {
            if (!CanSee(unit.Team, target)) return;
            if (!orders.TryGetValue(unit, out Order order))
            {
                return;
            }

            if (unit.Team != target.Side && unit.CanAttack)
            {
                Begin(unit, order, UnitState.Attack, target);
            }
            else if (unit.Team == target.Side && unit.Kind == UnitKind.Worker && !target.IsOperational)
            {
                StartBuild(unit, target.Site);
            }
            else if (EconomyFor(unit) != null && unit.Team == target.Side && unit.Kind == UnitKind.Worker &&
                target.Building == BuildingKind.TownHall && unit.CarriedGold > 0)
            {
                Begin(unit, order, UnitState.Deliver, target);
            }
            else if (unit.Team == target.Side && unit.Kind == UnitKind.Worker &&
                target.Building != BuildingKind.None && target.Health < target.MaxHealth)
            {
                Begin(unit, order, UnitState.Repair, target);
            }
        }

        private void Begin(PrototypeUnit unit, Order order, UnitState state, CombatTarget target, bool keepRoute = false)
        {
            ReleaseSite(unit, order);
            if (!keepRoute)
            {
                unit.Stop();
            }
            order.State = state;
            order.Target = target;
            order.Source = null;
            order.HarvestAt = 0f;
            order.NextPlan = time;
            order.FailedPlans = 0;
            order.ProgressPosition = unit.transform.position;
            order.LastProgress = time;
            if (target != null) order.LastTargetPosition = target.transform.position;
            // An interrupted attack still consumes the weapon's cooldown.
        }

        private static void Idle(PrototypeUnit unit, Order order)
        {
            ReleaseSite(unit, order);
            unit.Stop();
            order.State = UnitState.Idle;
            order.Target = null;
            order.Source = null;
        }

        private static void ReleaseSite(PrototypeUnit worker, Order order)
        {
            if (order.State == UnitState.Build && order.Target != null && order.Target.Site.Builder == worker)
            {
                order.Target.Site.Builder = null;
            }
        }

        public void Forget(CombatTarget target)
        {
            if (target.Unit != null)
            {
                target.Unit.Stop();
                if (orders.Remove(target.Unit, out Order order))
                {
                    ReleaseSite(target.Unit, order);
                }
            }

            foreach (var entry in orders)
            {
                if (entry.Value.BlockedThreat == target)
                {
                    entry.Value.BlockedThreat = null;
                    entry.Value.RetryThreatAt = 0f;
                }
                if (entry.Value.Target == target)
                {
                    if (entry.Value.State == UnitState.Deliver)
                    {
                        entry.Key.Stop();
                        entry.Value.Target = null;
                        entry.Value.NextPlan = time;
                    }
                    else if (entry.Value.State == UnitState.Defend)
                    {
                        Return(entry.Key, entry.Value);
                    }
                    else if (entry.Value.State == UnitState.Breach)
                    {
                        Begin(entry.Key, entry.Value, UnitState.ResumeBreach, null);
                    }
                    else
                    {
                        if (entry.Value.State == UnitState.Respond)
                        {
                            defenseTrace?.Invoke(entry.Key, target, "response ended: target defeated; route released");
                        }
                        Idle(entry.Key, entry.Value);
                    }
                }
            }
        }

        public void Tick(float deltaTime)
        {
            time += deltaTime;
            targets.RemoveAll(actor => actor == null || !actor.IsAlive);
            // Damage can remove entries, so iterate the stable scene roster.
            foreach (CombatTarget actor in targets)
            {
                if (actor == null || !actor.IsAlive || actor.Unit == null)
                {
                    continue;
                }

                PrototypeUnit unit = actor.Unit;
                Order order = orders[unit];
                switch (order.State)
                {
                    case UnitState.Move:
                        if (!unit.IsMoving)
                        {
                            if (training != null && DistanceSquared(unit.transform.position, order.BreachGoal) > 0.04f &&
                                !movement.HasRoute(unit, order.BreachGoal))
                            {
                                PlanBreach(unit, order);
                                break;
                            }
                            Idle(unit, order);
                            if (training != null)
                            {
                                order.NextScan = time + training.DefenseScanInterval;
                            }
                        }
                        break;
                    case UnitState.Attack:
                    case UnitState.Repair:
                    case UnitState.Defend:
                    case UnitState.Respond:
                    case UnitState.Breach:
                        Pursue(unit, order);
                        break;
                    case UnitState.ResumeBreach:
                        PlanBreach(unit, order);
                        break;
                    case UnitState.ReturnToPost:
                        ReturnToPost(unit, order);
                        break;
                    case UnitState.Idle:
                        if (training != null && unit.PlayerOwned)
                        {
                            ScanDefense(actor, order);
                        }
                        else
                        {
                            Scan(unit, order);
                        }
                        break;
                    case UnitState.Gather:
                        Harvest(unit, order);
                        break;
                    case UnitState.Deliver:
                        Deliver(unit, order);
                        break;
                    case UnitState.Build:
                        Build(unit, order, deltaTime);
                        break;
                }
            }
        }

        private void Build(PrototypeUnit worker, Order order, float deltaTime)
        {
            CombatTarget target = order.Target;
            if (target == null || !target.IsAlive || target.IsOperational || target.Site.Builder != worker)
            {
                Idle(worker, order);
                return;
            }

            ConstructionSite site = target.Site;
            bool InRange(Vector3 point) => target.DistanceFrom(point, worker.Radius) <= site.WorkRange;
            if (!InRange(worker.transform.position))
            {
                Approach(worker, order, target.transform.position, InRange,
                    target.ApproachRadius(worker.Radius, site.WorkRange));
                return;
            }

            worker.Stop();
            site.Advance(deltaTime);
            if (site.Completed)
            {
                Idle(worker, order);
            }
        }

        private void Harvest(PrototypeUnit worker, Order order)
        {
            PrototypeEconomy resources = EconomyFor(worker);
            GoldDeposit source = order.Source;
            if (source == null || (fog == null ? source.Stock : fog.KnownStock(worker.Team, source)) <= 0)
            {
                Idle(worker, order);
                return;
            }

            Bounds body = source.Body.bounds;
            bool InRange(Vector3 point)
            {
                Vector2 origin = GridNavigation.ToPlane(point);
                Vector2 nearest = GridNavigation.ToPlane(body.ClosestPoint(point));
                return Vector2.Distance(origin, nearest) <= resources.WorkRange + worker.Radius;
            }

            if (!InRange(worker.transform.position))
            {
                Approach(worker, order, source.transform.position, InRange,
                    GridNavigation.ToPlane(body.extents).magnitude + resources.WorkRange + worker.Radius);
                return;
            }

            worker.Stop();
            if (order.HarvestAt == 0f)
            {
                order.HarvestAt = time + resources.GatherSeconds;
                return;
            }

            if (time >= order.HarvestAt)
            {
                worker.CarriedGold = source.Take(resources.LoadSize);
                order.State = UnitState.Deliver;
                order.NextPlan = time;
            }
        }

        private void Deliver(PrototypeUnit worker, Order order)
        {
            PrototypeEconomy resources = EconomyFor(worker);
            if (order.Target == null || !order.Target.IsAlive)
            {
                order.Target = resources.FindTownHall(worker);
            }

            CombatTarget hall = order.Target;
            if (hall == null)
            {
                worker.Stop();
                return;
            }

            bool InRange(Vector3 point) => hall.DistanceFrom(point, worker.Radius) <= resources.WorkRange;
            if (!InRange(worker.transform.position))
            {
                Approach(worker, order, hall.transform.position, InRange,
                    hall.ApproachRadius(worker.Radius, resources.WorkRange));
                return;
            }

            resources.Deposit(worker);
            GoldDeposit source = order.Source;
            if (source == null || (fog == null ? source.Stock : fog.KnownStock(worker.Team, source)) <= 0)
            {
                Idle(worker, order);
                return;
            }

            Begin(worker, order, UnitState.Gather, null);
            order.Source = source;
        }

        private void Approach(PrototypeUnit worker, Order order, Vector3 destination,
            System.Predicate<Vector3> inRange, float searchRadius)
        {
            if (!worker.IsMoving && time >= order.NextPlan)
            {
                order.NextPlan = time + 0.6f;
                movement.TryMoveNear(worker, destination, inRange, searchRadius);
            }
        }

        internal event System.Action<CombatTarget, CombatTarget> Hit;

        internal void ApplyDamage(CombatTarget attacker, CombatTarget target, float damage)
        {
            Hit?.Invoke(attacker, target);
            target.Damage(damage);
            if (training != null && target.IsAlive && target.Side == Side.Opponent && target.Unit != null)
                TryRespond(target.Unit, attacker, target.transform.position, selfDefense: true);
        }

        private void Pursue(PrototypeUnit unit, Order order)
        {
            CombatTarget target = order.Target;
            if (target != null && target.Side != unit.Team && !CanSee(unit.Team, target))
            {
                Vector3 lastSeen = order.LastTargetPosition;
                Idle(unit, order);
                Move(new[] { unit }, lastSeen);
                return;
            }
            bool repair = order.State == UnitState.Repair;
            bool defend = order.State == UnitState.Defend;
            if (target == null || !target.IsAlive || (repair && target.Health >= target.MaxHealth))
            {
                FinishPursuit(unit, order);
                return;
            }

            if (order.State == UnitState.Respond && ActiveResponse(unit) == null)
            {
                return;
            }

            if (defend && (DistanceSquared(target.transform.position, order.DefensePost) >
                    training.DefenseLeash * training.DefenseLeash ||
                !InsideDefenseZone(unit.transform.position, order)))
            {
                Return(unit, order);
                return;
            }

            float range = repair ? unit.RepairRange : unit.AttackRange;
            bool InRange(Vector3 point) => repair ? target.DistanceFrom(point, unit.Radius) <= range :
                movement.CanStrike(unit, target, point);
            if (InRange(unit.transform.position))
            {
                unit.Stop();
                order.FailedPlans = 0;
                order.LastProgress = time;
                if (time < order.NextAction)
                {
                    return;
                }

                order.NextAction = time + (repair ? unit.RepairInterval : unit.AttackInterval);
                unit.SignalCombatMotion();
                if (repair)
                {
                    target.Repair(unit.RepairAmount);
                    if (target.Health >= target.MaxHealth)
                    {
                        Idle(unit, order);
                    }
                }
                else
                {
                    if (unit.Team == Side.Opponent && target.Side == Side.Player)
                    {
                        training?.RegisterBaseHit(target, targets);
                        defenseTrace?.Invoke(unit, target, $"HIT {unit.Damage:F1} ({order.State})");
                    }
                    ApplyDamage(unit.GetComponent<CombatTarget>(), target, unit.Damage);
                    if (!target.IsAlive && order.Target == target)
                    {
                        FinishPursuit(unit, order);
                    }
                }
                return;
            }

            Vector3 targetPosition = target.transform.position;
            if (order.State == UnitState.Respond || order.State == UnitState.Breach)
            {
                if (DistanceSquared(unit.transform.position, order.ProgressPosition) > 0.0625f)
                {
                    order.ProgressPosition = unit.transform.position;
                    order.LastProgress = time;
                }

                if (time - order.LastProgress >= 10f)
                {
                    if (order.State == UnitState.Breach)
                    {
                        ReportBlockedMove(unit, order);
                    }
                    else
                    {
                        BlockThreat(unit, order, target);
                        Idle(unit, order);
                    }
                    return;
                }
            }

            bool targetMoved = (GridNavigation.ToPlane(targetPosition - order.LastTargetPosition)).sqrMagnitude >= 0.36f;
            bool reacting = order.State == UnitState.Respond;
            if (time < order.NextPlan || (unit.IsMoving && !targetMoved && InRange(unit.Destination) && (!reacting || unit.BlockedTime < 0.30f)))
            {
                return;
            }

            order.NextPlan = time + 0.6f;
            order.LastTargetPosition = targetPosition;
            System.Predicate<Vector3> zone = defend ? point => InsideDefenseZone(point, order) : null;
            if (order.State == UnitState.Respond)
            {
                zone = point => DistanceSquared(point, order.DefensePost) <= opponentPursuit * opponentPursuit;
            }
            bool planned = reacting ? movement.TryReachDefensePosition(unit, target, zone, continuing: true) :
                movement.TryMoveNear(unit, targetPosition, InRange,
                    target.ApproachRadius(unit.Radius, range), zone);
            if (planned)
            {
                order.FailedPlans = 0;
            }
            else if (!unit.IsMoving && ++order.FailedPlans >= 6)
            {
                if (order.State == UnitState.Breach)
                {
                    ReportBlockedMove(unit, order);
                }
                else
                {
                    if (order.State == UnitState.Respond)
                    {
                        BlockThreat(unit, order, target);
                    }
                    FinishPursuit(unit, order);
                }
                order.NextScan = time + 2f;
            }
        }

        private void BlockThreat(PrototypeUnit unit, Order order, CombatTarget target)
        {
            order.BlockedThreat = target;
            order.RetryThreatAt = time + 4f;
            LastTacticalIssue = $"{unit.Kind}: no reachable position against {target.DisplayName}";
            defenseTrace?.Invoke(unit, target, $"blocked: no usable attack route or progress; retry at {order.RetryThreatAt:F2}");
        }

        private void ScanDefense(CombatTarget defender, Order order)
        {
            PrototypeUnit unit = defender.Unit;
            if (!unit.CanAttack || time < order.NextScan)
            {
                return;
            }

            order.NextScan = time + training.DefenseScanInterval;
            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MaxValue;
            CombatTarget threat = null;
            foreach (CombatTarget candidate in targets)
            {
                if (candidate == null || !candidate.IsAlive || !CanSee(defender.Side, candidate) ||
                    candidate.Side == defender.Side ||
                    candidate.Unit == null)
                {
                    continue;
                }

                float distance = DistanceSquared(candidate.transform.position, unit.transform.position);
                int priority = 2;
                if (orders.TryGetValue(candidate.Unit, out Order attack) && attack.State == UnitState.Attack &&
                    attack.Target != null && attack.Target.IsAlive && attack.Target.Side == defender.Side)
                {
                    if (attack.Target == defender)
                    {
                        priority = 0;
                    }
                    else if (attack.Target.DistanceFrom(unit.transform.position, unit.Radius) <=
                        training.DefenseDetectionRange)
                    {
                        priority = 1;
                    }
                }

                float range = priority < 2 ? training.DefenseLeash : training.DefenseDetectionRange;
                if (distance <= range * range &&
                    (priority < bestPriority || (priority == bestPriority && distance < bestDistance)))
                {
                    bestPriority = priority;
                    bestDistance = distance;
                    threat = candidate;
                }
            }

            if (threat != null)
            {
                order.DefensePost = unit.transform.position;
                Begin(unit, order, UnitState.Defend, threat);
            }
        }

        private bool InsideDefenseZone(Vector3 point, Order order) =>
            DistanceSquared(point, order.DefensePost) <= training.DefenseLeash * training.DefenseLeash;

        private static float DistanceSquared(Vector3 a, Vector3 b) => GridNavigation.ToPlane(a - b).sqrMagnitude;

        private void FinishPursuit(PrototypeUnit unit, Order order)
        {
            if (order.State == UnitState.Defend)
            {
                Return(unit, order);
            }
            else if (order.State == UnitState.Breach)
            {
                Begin(unit, order, UnitState.ResumeBreach, null);
            }
            else
            {
                Idle(unit, order);
            }
        }

        private void Return(PrototypeUnit unit, Order order) => Begin(unit, order, UnitState.ReturnToPost, null);

        private void ReturnToPost(PrototypeUnit unit, Order order)
        {
            if (unit.IsMoving || time < order.NextPlan)
            {
                return;
            }

            Vector3 post = order.DefensePost;
            if (DistanceSquared(unit.transform.position, post) <= 0.12f || order.FailedPlans >= 4)
            {
                Idle(unit, order);
                order.NextScan = time + Mathf.Max(0.8f, training.DefenseScanInterval);
                return;
            }

            order.NextPlan = time + 0.8f;
            order.FailedPlans++;
            movement.TryMoveNear(unit, post, point => DistanceSquared(post, point) <= 2.25f, 1.5f);
        }

        private void Scan(PrototypeUnit unit, Order order)
        {
            if (opponentEconomy != null || unit.Team != Side.Opponent || !unit.CanAttack || time < order.NextScan)
            {
                return;
            }

            order.NextScan = time + 0.4f;
            CombatTarget nearest = null;
            float distance = unit.DetectionRange;
            foreach (CombatTarget candidate in targets)
            {
                if (candidate == null || !candidate.IsAlive || !CanSee(unit.Team, candidate) || candidate.Side == unit.Team)
                {
                    continue;
                }

                float candidateDistance = candidate.DistanceFrom(unit.transform.position, unit.Radius);
                if (candidateDistance <= distance)
                {
                    distance = candidateDistance;
                    nearest = candidate;
                }
            }

            if (nearest != null)
            {
                Interact(unit, nearest);
            }
        }
    }
}
