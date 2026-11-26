using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Rts
{
    [System.Serializable]
    public struct CameraViewState
    {
        public Vector3 center;
        public float size;
    }

    public sealed partial class PrototypeControls : MonoBehaviour
    {
        private const float DragThreshold = 8f;
        private const float EdgeSize = 24f;

        [SerializeField] private Camera viewCamera;
        [SerializeField, Min(1f)] private float minimumZoom = 6f;
        [SerializeField, Min(1f)] private float maximumZoom = 16f;
        [SerializeField, Min(0.01f)] private float zoomSensitivity = 2f;
        [SerializeField, Min(0.01f)] private float zoomSmoothTime = 0.15f;
        [SerializeField] private RectInt mapBounds = new RectInt(-24, -18, 48, 36);
        [SerializeField] private Collider[] obstacles;
        [SerializeField] private PrototypeUnit[] units;
        [SerializeField] private CombatTarget[] combatTargets = System.Array.Empty<CombatTarget>();

        private readonly List<PrototypeUnit> selected = new List<PrototypeUnit>();
        private readonly List<PrototypeUnit> activeUnits = new List<PrototypeUnit>();
        private readonly List<CombatTarget> activeTargets = new List<CombatTarget>();
        private readonly List<Collider> activeObstacles = new List<Collider>();
        private UnitMovement movement;
        private UnitOrders orders;
        private PrototypeEconomy economy;
        private PrototypeEconomy opponentEconomy;
        private PrototypeConstruction construction;
        private PrototypeInterface interfaceHud;
        private PrototypeMatch match;
        private OpponentDirector director;
        private MatchVisibility fog;
        private MatchLandscape landscape;
        private EconomyHud economyHud;
        private CombatTarget selectedBuilding;
        private GoldDeposit selectedSource;
        private GUIStyle healthLabel;
        private Vector3 cameraTarget;
        private float zoomTarget;
        private float zoomVelocity;
        private bool selecting;
        private Vector2 dragStart;
        private Vector2 pointer;

        private void Awake()
        {
            zoomTarget = viewCamera.orthographicSize;
            foreach (PrototypeEconomy resources in GetComponents<PrototypeEconomy>())
            {
                if (resources.Owner == Side.Player)
                {
                    economy = resources;
                }
                else
                {
                    opponentEconomy = resources;
                }
            }
            construction = GetComponent<PrototypeConstruction>();
            interfaceHud = GetComponent<PrototypeInterface>();
            match = GetComponent<PrototypeMatch>();
            if (Application.isPlaying && match != null && SaveLoader.Pending == null)
                EnsureAudioListener();
            director = GetComponent<OpponentDirector>();
            interfaceHud?.Initialize(mapBounds);
            activeObstacles.AddRange(obstacles);
            movement = new UnitMovement(mapBounds, CollectFootprints(), units,
                economy != null ? economy.ProductionClearance() : 0f);
            orders = new UnitOrders(movement, combatTargets, economy, opponentEconomy, match);
            activeUnits.AddRange(units);
            activeTargets.AddRange(combatTargets);
            if (Application.isPlaying && match != null)
            {
                foreach (var obstacle in activeObstacles) MatchActorVisual.AttachLandmark(obstacle);
                foreach (var actor in activeTargets) MatchActorVisual.Attach(actor, this);
            }
            fog = GetComponent<MatchVisibility>();
            fog?.Initialize(mapBounds, activeTargets, activeObstacles);
            if (Application.isPlaying && match != null && fog != null)
                landscape = MatchLandscape.Create(transform, mapBounds, fog, activeObstacles);
            orders.SetVisibility(fog);
            orders.Hit += OnCombatHit;
            interfaceHud?.SetVisibility(fog);
            foreach (CombatTarget target in combatTargets)
            {
                target.Defeated += OnDefeated;
            }
            if (economy != null)
            {
                economy.Initialize(movement, combatTargets, RegisterSpawn);
                opponentEconomy?.Initialize(movement, combatTargets, RegisterSpawn);
                economyHud = new EconomyHud(economy, construction);
                construction?.Initialize(mapBounds, economy, movement, orders, CollectFootprints, RegisterBuilding,
                    opponentEconomy);
            }
            director?.Initialize(opponentEconomy, construction, orders, activeTargets, activeObstacles,
                match.Difficulty);
            if (match != null)
            {
                foreach (CombatTarget target in combatTargets)
                {
                    if (target.Side == Side.Player && target.Building == BuildingKind.TownHall)
                    {
                        cameraTarget = target.transform.position;
                        cameraTarget.y = 0f;
                        break;
                    }
                }
            }
            UpdateCamera(Vector2.zero, false);
            if (match != null && SaveLoader.Pending != null)
            {
                try { RestoreSave(SaveLoader.Pending); }
                catch (System.Exception error) { SaveLoader.Failure = error.Message; Debug.LogWarning("Відновлення збереження відхилено: " + error.Message); }
            }
        }

        private void RegisterBuilding(CombatTarget target)
        {
            if (Application.isPlaying && match != null) MatchActorVisual.Attach(target, this);
            activeTargets.Add(target);
            activeObstacles.Add(target.Body);
            landscape?.AddApproach(target.Body);
            orders.Register(target);
            target.Defeated += OnDefeated;
            movement.UpdateObstacles(CollectFootprints(), reroute: true);
            fog?.Refresh();
        }

        private void RegisterSpawn(CombatTarget target)
        {
            if (Application.isPlaying && match != null) MatchActorVisual.Attach(target, this);
            activeUnits.Add(target.Unit);
            activeTargets.Add(target);
            movement.Register(target.Unit);
            orders.Register(target);
            target.Defeated += OnDefeated;
            fog?.Refresh();
        }

        private List<Bounds> CollectFootprints()
        {
            var footprints = new List<Bounds>();
            foreach (Collider obstacle in activeObstacles)
            {
                if (obstacle != null && obstacle.gameObject.activeInHierarchy)
                {
                    footprints.Add(obstacle.bounds);
                }
            }
            return footprints;
        }

        private void OnDefeated(CombatTarget target)
        {
            director?.ContactDefeated(target);
            orders.Forget(target);
            activeTargets.Remove(target);
            economy?.OnDefeated(target);
            opponentEconomy?.OnDefeated(target);
            if (target.Unit != null)
            {
                selected.Remove(target.Unit);
                activeUnits.Remove(target.Unit);
                movement.Remove(target.Unit);
                construction?.OnWorkerDefeated(target.Unit);
            }
            else
            {
                activeObstacles.Remove(target.Body);
                if (selectedBuilding == target)
                {
                    selectedBuilding = null;
                }
                movement.UpdateObstacles(CollectFootprints());
            }
        }

        private void Update()
        {
            if (DisplaySettings.Instance?.BlocksWorldInput == true) return;
            if (SaveLoader.Busy || saveWindow?.IsOpen == true)
            {
                if (!SaveLoader.Busy && Keyboard.current?.escapeKey.wasPressedThisFrame == true) saveWindow.Close();
                return;
            }
            if (match?.BlocksWorldInput == true)
            {
                return;
            }

            if (!GameViewFocused())
            {
                selecting = false;
                construction?.HidePreview();
                return;
            }

            if (interfaceHud != null && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                if (match?.CancelMenuConfirmation() != true)
                {
                    interfaceHud.HandleEscape(construction);
                }
                selecting = false;
                construction?.HidePreview();
                return;
            }

            Mouse mouse = Mouse.current;
            if (interfaceHud?.IsPaused == true || mouse == null)
            {
                return;
            }

            Vector2 mousePosition = mouse.position.ReadValue();
            Rect viewport = viewCamera.pixelRect;
            Vector2 max = viewport.max - Vector2.one;
            bool inside = viewport.Contains(mousePosition);
            pointer = new Vector2(
                Mathf.Clamp(mousePosition.x, viewport.xMin, max.x),
                Mathf.Clamp(mousePosition.y, viewport.yMin, max.y));

            var layout = CurrentLayout();
            Vector2 logicalPoint = layout.FromScreen(pointer);
            bool overHud = IsUiCovered(layout, logicalPoint);
            if (inside && mouse.leftButton.wasPressedThisFrame && TryFocusMinimap(layout, logicalPoint))
            {
                selecting = false;
                construction?.HidePreview();
                return;
            }

            AdvanceZoom(mouse.scroll.ReadValue().y, Time.unscaledDeltaTime, inside && !overHud);
            UpdateCamera(pointer, inside && !overHud);
            if (construction != null && construction.IsPlacing)
            {
                UpdatePlacement(mouse, inside && !overHud);
                return;
            }
            if (match != null && Keyboard.current?.sKey.wasPressedThisFrame == true)
            {
                foreach (PrototypeUnit unit in selected)
                {
                    orders.Halt(unit);
                }
                return;
            }
            if (inside && !overHud && mouse.leftButton.wasPressedThisFrame)
            {
                selecting = true;
                dragStart = pointer;
            }

            if (selecting && mouse.leftButton.wasReleasedThisFrame)
            {
                if (!overHud)
                {
                    Select(pointer);
                }
                selecting = false;
            }

            if (inside && !overHud && mouse.rightButton.wasPressedThisFrame)
            {
                MoveSelected(pointer);
            }
        }

        private HudLayout CurrentLayout() => new HudLayout(Screen.width, Screen.height, interfaceHud != null);

        private bool IsUiCovered(HudLayout layout, Vector2 logicalPoint)
        {
            return match?.BlocksWorldInput == true || (economyHud != null && economyHud.Covers(layout, logicalPoint)) ||
                (interfaceHud != null && interfaceHud.Covers(layout, logicalPoint)) ||
                (match != null && match.CoversGuidance(layout, logicalPoint));
        }

        private bool TryFocusMinimap(HudLayout layout, Vector2 logicalPoint)
        {
            if (match?.BlocksWorldInput == true || interfaceHud == null ||
                !interfaceHud.TryMinimapFocus(layout, logicalPoint, out Vector3 focus))
            {
                return false;
            }

            cameraTarget = focus;
            UpdateCamera(Vector2.zero, false);
            return true;
        }

        private void SetPaused(bool paused)
        {
            interfaceHud.SetPaused(paused);
            selecting = false;
            construction?.HidePreview();
        }

        private void UpdatePlacement(Mouse mouse, bool overMap)
        {
            selecting = false;
            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true || mouse.rightButton.wasPressedThisFrame)
            {
                construction.Cancel();
                return;
            }

            Ray ray = viewCamera.ScreenPointToRay(pointer);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!overMap || !ground.Raycast(ray, out float distance))
            {
                construction.HidePreview();
                return;
            }

            Vector3 groundPoint = ray.GetPoint(distance);
            if (mouse.leftButton.wasPressedThisFrame)
            {
                construction.Confirm(groundPoint);
            }
            construction.ShowPreview(groundPoint);
        }

        private static bool GameViewFocused()
        {
#if UNITY_EDITOR
            var window = UnityEditor.EditorWindow.focusedWindow;
            return Application.isFocused && window != null && window.GetType().Name == "GameView";
#else
            return Application.isFocused;
#endif
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                selecting = false;
            }
        }

        private void FixedUpdate()
        {
            AdvanceSimulation(Time.fixedDeltaTime);
        }

        [SerializeField, Min(0)] private float hallAttackRange = 5f;
        [SerializeField, Min(0)] private float hallAttackDamage = 6f;
        [SerializeField, Min(.1f)] private float hallAttackInterval = 1.5f;
        private AudioSource eventAudio;
        private AudioListener eventListener;
        private AudioClip selectionSound, hitSound;
        private float nextSelectionSound, nextHitSound;

        private void OnCombatHit(CombatTarget attacker, CombatTarget victim)
        {
            if (fog == null || fog.IsVisible(Side.Player, attacker) && fog.IsVisible(Side.Player, victim))
                PlayEvent(true);
        }

        private void PlayEvent(bool hit)
        {
            if (!Application.isPlaying || match == null) return;
            float now = Time.unscaledTime;
            if (now < (hit ? nextHitSound : nextSelectionSound)) return;
            EnsureAudioListener();
            if (eventAudio == null)
            {
                eventAudio = gameObject.AddComponent<AudioSource>();
                eventAudio.playOnAwake = false;
                eventAudio.spatialBlend = 0f;
                eventAudio.volume = 1f;
                eventAudio.mute = false;
                selectionSound = CreateTone("RTS selection", 620, .07f);
                hitSound = CreateTone("RTS impact", 140, .10f);
            }
            AudioClip clip = hit ? hitSound : selectionSound;
            if (clip == null) return;
            if (hit) nextHitSound = now + .18f;
            else nextSelectionSound = now + .12f;
            eventAudio.PlayOneShot(clip, .75f);
        }

        private void EnsureAudioListener()
        {
            if (eventListener != null) return;
            eventListener = viewCamera.GetComponent<AudioListener>();
            if (eventListener == null) eventListener = viewCamera.gameObject.AddComponent<AudioListener>();
            eventListener.enabled = true;
        }

        private static AudioClip CreateTone(string label, float frequency, float duration)
        {
            const int rate = 22050;
            var samples = new float[Mathf.RoundToInt(rate * duration)];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = .24f * Mathf.Sin(2 * Mathf.PI * frequency * i / rate) *
                    Mathf.Min(1, i / 60f) * (1 - (float)i / samples.Length);
            var clip = AudioClip.Create(label, samples.Length, 1, rate, false);
            if (clip.SetData(samples, 0)) return clip;
            Debug.LogError($"Could not write samples for {label}.");
            Destroy(clip);
            return null;
        }

        private void OnDestroy()
        {
            if (selectionSound != null) Destroy(selectionSound);
            if (hitSound != null) Destroy(hitSound);
        }

        private void TickBuildingCombat()
        {
            if (hallAttackDamage <= 0 || hallAttackRange <= 0) return;
            // Damage may remove a target from the live roster.
            foreach (var hall in activeTargets.ToArray())
            {
                if (!hall.IsAlive || hall.Building != BuildingKind.TownHall || !hall.IsOperational ||
                    hall.NextBuildingAttack > match.ElapsedSeconds) continue;
                CombatTarget best = null;
                float nearest = hallAttackRange;
                foreach (var target in activeTargets)
                {
                    if (target.Side == hall.Side || !target.IsAlive ||
                        fog != null && !fog.IsVisible(hall.Side, target)) continue;
                    float distance = hall.DistanceFrom(target.Body.bounds.center, 0);
                    if (distance > nearest || !movement.HasShot(hall.Body.bounds.center, target, hall)) continue;
                    nearest = distance;
                    best = target;
                }
                hall.NextBuildingAttack = match.ElapsedSeconds + (best == null ? .25f : hallAttackInterval);
                if (best == null) continue;
                if (hall.Side == Side.Opponent) match.RegisterBaseHit(best, activeTargets);
                orders.ApplyDamage(hall, best, hallAttackDamage);
            }
        }

        internal float AnimationTime => match.ElapsedSeconds;
        internal bool AnimationFrozen => match.BlocksSimulation || interfaceHud.IsPaused;
        internal UnitState AnimationOrder(PrototypeUnit unit) => orders.StateOf(unit);
        internal Vector3? AnimationFocus(PrototypeUnit unit, out bool working) => orders.VisualFocus(unit, out working);

        private void AdvanceSimulation(float deltaTime)
        {
            if (interfaceHud?.IsPaused == true || match?.BlocksSimulation == true)
            {
                return;
            }

            match?.Tick(deltaTime);
            fog?.Tick(deltaTime);
            director?.Tick(deltaTime);
            orders.Tick(deltaTime);
            if (match != null) TickBuildingCombat();
            movement.Tick(deltaTime);
            economy?.Tick(deltaTime);
            opponentEconomy?.Tick(deltaTime);
            if (selectedBuilding != null && fog != null && !fog.IsVisible(Side.Player, selectedBuilding))
                selectedBuilding = null;
            if (selectedSource != null && fog != null && !fog.IsVisible(Side.Player, selectedSource))
                selectedSource = null;
            if (match != null)
            {
                match.Evaluate(activeTargets);
                if (match.HasEnded)
                {
                    selecting = false;
                    construction?.Cancel();
                }
            }
        }

        public CameraViewState CaptureCamera() => new CameraViewState
            { center = cameraTarget, size = viewCamera.orthographicSize };

        public void RestoreCamera(CameraViewState state)
        {
            cameraTarget = state.center;
            cameraTarget.y = 0f;
            zoomTarget = Mathf.Clamp(state.size, minimumZoom, Mathf.Max(minimumZoom, maximumZoom));
            viewCamera.orthographicSize = zoomTarget;
            zoomVelocity = 0f;
            UpdateCamera(Vector2.zero, false);
        }

        private void AdvanceZoom(float wheel, float deltaTime, bool overMap)
        {
            if (match == null || match.BlocksWorldInput || interfaceHud?.IsPaused == true) return;
            float wheelUnits = InputSystem.settings.scrollDeltaBehavior == InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms
                ? wheel : wheel / 120f;
            if (overMap)
                zoomTarget = Mathf.Clamp(zoomTarget - wheelUnits * zoomSensitivity,
                    minimumZoom, Mathf.Max(minimumZoom, maximumZoom));
            viewCamera.orthographicSize = Mathf.SmoothDamp(viewCamera.orthographicSize, zoomTarget,
                ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, deltaTime);
        }

        private void UpdateCamera(Vector2 position, bool inside)
        {
            Rect viewport = viewCamera.pixelRect;
            if (inside)
            {
                var direction = new Vector3(
                    EdgeDirection(position.x - viewport.xMin, viewport.width), 0f,
                    EdgeDirection(position.y - viewport.yMin, viewport.height));
                cameraTarget += direction.normalized * (14f * Time.unscaledDeltaTime);
            }

            float halfWidth = viewCamera.orthographicSize * viewCamera.aspect;
            float halfDepth = viewCamera.orthographicSize / Mathf.Abs(viewCamera.transform.forward.y);
            cameraTarget.x = ClampView(cameraTarget.x, mapBounds.xMin, mapBounds.xMax, halfWidth);
            cameraTarget.z = ClampView(cameraTarget.z, mapBounds.yMin, mapBounds.yMax, halfDepth);
            viewCamera.transform.position = cameraTarget - viewCamera.transform.forward * 22f;
        }

        private static float EdgeDirection(float position, float size)
        {
            float edge = Mathf.Min(EdgeSize, size * 0.25f);
            if (position < edge)
            {
                return -1f;
            }

            return position >= size - edge ? 1f : 0f;
        }

        private static float ClampView(float position, float min, float max, float halfView)
        {
            return halfView * 2f >= max - min
                ? (min + max) * 0.5f
                : Mathf.Clamp(position, min + halfView, max - halfView);
        }

        private void Select(Vector2 end)
        {
            foreach (PrototypeUnit unit in selected)
            {
                unit.SetSelected(false);
            }

            selected.Clear();
            selectedBuilding = null;
            selectedSource = null;
            if ((end - dragStart).sqrMagnitude < DragThreshold * DragThreshold)
            {
                if (TryVisibleHit(end, out RaycastHit hit))
                {
                    AddSelection(hit.collider.GetComponentInParent<PrototypeUnit>());
                    if (economy != null)
                    {
                        CombatTarget target = hit.collider.GetComponentInParent<CombatTarget>();
                        if (target != null && target.Building != BuildingKind.None)
                        {
                            selectedBuilding = target;
                        }
                        selectedSource = hit.collider.GetComponentInParent<GoldDeposit>();
                    }
                }
                if (selected.Count > 0 || selectedBuilding != null) PlayEvent(false);
                return;
            }

            Rect region = ScreenRect(dragStart, end);
            foreach (PrototypeUnit unit in activeUnits)
            {
                if (unit == null || !unit.IsAvailable)
                {
                    continue;
                }
                Vector3 projected = viewCamera.WorldToScreenPoint(unit.transform.position);
                if (projected.z > 0f && region.Contains(projected, true))
                {
                    AddSelection(unit);
                }
            }
            if (selected.Count == 0) SelectBuildingIn(region);
            if (selected.Count > 0 || selectedBuilding != null) PlayEvent(false);
        }

        private void SelectBuildingIn(Rect region)
        {
            float nearest = float.PositiveInfinity;
            foreach (var actor in activeTargets)
            {
                if (!actor.IsAlive || actor.Side != Side.Player || actor.Unit != null ||
                    fog != null && !fog.IsVisible(Side.Player, actor)) continue;
                Vector3 point = viewCamera.WorldToScreenPoint(actor.Body.bounds.center);
                float distance = ((Vector2)point - region.center).sqrMagnitude;
                if (point.z <= 0 || !region.Contains(point) || distance >= nearest) continue;
                selectedBuilding = actor;
                nearest = distance;
            }
        }

        private void AddSelection(PrototypeUnit unit)
        {
            if (unit != null && unit.PlayerOwned)
            {
                selected.Add(unit);
                unit.SetSelected(true);
            }
        }

        private void MoveSelected(Vector2 position)
        {
            if (selected.Count == 0 ||
                !TryVisibleHit(position, out RaycastHit hit))
            {
                return;
            }

            if (hit.collider.gameObject == gameObject)
            {
                orders.Move(selected, hit.point);
                return;
            }

            GoldDeposit source = hit.collider.GetComponentInParent<GoldDeposit>();
            if (source != null)
            {
                orders.Gather(selected, source);
                return;
            }

            CombatTarget target = hit.collider.GetComponentInParent<CombatTarget>();
            if (target != null)
            {
                if (target.Side == Side.Player && target.Unit != null)
                {
                    orders.Move(selected, target.transform.position);
                }
                else
                {
                    orders.Interact(selected, target);
                }
                return;
            }

            PrototypeUnit occupant = hit.collider.GetComponentInParent<PrototypeUnit>();
            if (occupant != null)
            {
                orders.Move(selected, occupant.transform.position);
            }
        }

        private bool TryVisibleHit(Vector2 screen, out RaycastHit hit)
        {
            RaycastHit[] hits = Physics.RaycastAll(viewCamera.ScreenPointToRay(screen));
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit candidate in hits)
            {
                CombatTarget actor = candidate.collider.GetComponentInParent<CombatTarget>();
                if (fog != null && (actor != null ? !fog.IsVisible(Side.Player, actor) :
                    candidate.collider.gameObject != gameObject &&
                    fog.State(Side.Player, candidate.collider.transform.position) != VisibilityState.Visible))
                    continue;
                hit = candidate;
                return true;
            }
            hit = default;
            return false;
        }

        private static Rect ScreenRect(Vector2 first, Vector2 second)
        {
            return Rect.MinMaxRect(Mathf.Min(first.x, second.x), Mathf.Min(first.y, second.y),
                Mathf.Max(first.x, second.x), Mathf.Max(first.y, second.y));
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.Repaint)
            {
                DrawHealth();
            }

            DrawHud();
            if (Event.current.type != EventType.Repaint || !selecting ||
                (pointer - dragStart).sqrMagnitude < DragThreshold * DragThreshold)
            {
                return;
            }

            Rect box = ScreenRect(dragStart, pointer);
            box.y = Screen.height - box.yMax;
            Color previous = GUI.color;
            GUI.color = new Color(0.25f, 0.9f, 1f, 0.18f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.25f, 0.9f, 1f);
            GUI.DrawTexture(new Rect(box.xMin, box.yMin, box.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.xMin, box.yMax - 2, box.width, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.xMin, box.yMin, 2, box.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.xMax - 2, box.yMin, 2, box.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void DrawHud()
        {
            var layout = CurrentLayout();
            Matrix4x4 previousMatrix = GUI.matrix;
            bool previousEnabled = GUI.enabled;
            GUI.matrix = previousMatrix * Matrix4x4.Scale(new Vector3(layout.Scale, layout.Scale, 1f));
            try
            {
                bool blocked = match?.BlocksSimulation == true;
                GUI.enabled = previousEnabled && interfaceHud?.IsPaused != true && !blocked;
                economyHud?.Draw(layout, selectedBuilding, selectedSource, selected, orders);
                GUI.enabled = previousEnabled && saveWindow?.IsOpen != true && !SaveLoader.Busy && DisplaySettings.Instance?.BlocksWorldInput != true;
                match?.DrawClock(layout);
                interfaceHud?.Draw(layout, viewCamera, activeObstacles, activeTargets, SetPaused, !blocked, match);
                if (match?.HasEnded == true)
                {
                    match.DrawResult(layout);
                }
                else if (match != null && !match.HasStarted)
                {
                    match.DrawIntro(layout);
                }
                else if (interfaceHud?.IsPaused != true)
                {
                    match?.DrawGuidance(layout);
                }
                GUI.enabled = previousEnabled;
                saveWindow?.Draw(layout);
                DisplaySettings.Instance?.Draw(layout);
            }
            finally
            {
                GUI.enabled = previousEnabled;
                GUI.matrix = previousMatrix;
            }
        }

        private void DrawHealth()
        {
            if (activeTargets.Count == 0)
            {
                return;
            }

            healthLabel ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };
            Color previous = GUI.color;
            var layout = CurrentLayout();
            CombatTarget detail = selectedBuilding;
            if (detail == null && selected.Count == 1) detail = selected[0].GetComponent<CombatTarget>();
            Vector2 cursor = Event.current.mousePosition;
            if (!IsUiCovered(layout, cursor / layout.Scale) &&
                TryVisibleHit(new Vector2(cursor.x, Screen.height - cursor.y), out var hit))
            {
                var hovered = hit.collider.GetComponentInParent<CombatTarget>();
                if (hovered != null) detail = hovered;
            }
            float width = 30 * layout.Scale;
            float height = 4 * layout.Scale;
            Rect detailRect = default;
            string detailText = null;
            foreach (CombatTarget target in activeTargets)
            {
                if (target == null || !target.IsAlive || fog != null && !fog.IsVisible(Side.Player, target))
                {
                    continue;
                }

                Vector3 anchor = target.transform.position;
                anchor.y = target.Body.bounds.max.y + 0.25f;
                if (Application.isPlaying && match != null && target.IsOperational)
                {
                    if (target.Building == BuildingKind.TownHall) anchor.y += 1.0f;
                    else if (target.Unit != null && target.Unit.Kind == UnitKind.Cavalry) anchor.y += .52f;
                }
                Vector3 screen = viewCamera.WorldToScreenPoint(anchor);
                if (screen.z <= 0f || !viewCamera.pixelRect.Contains(screen))
                {
                    continue;
                }

                float y = Screen.height - screen.y;
                var logical = new Vector2(screen.x, y) / layout.Scale;
                if (economyHud?.Covers(layout, logical) == true ||
                    interfaceHud != null && layout.MinimapPanel(mapBounds).Contains(logical)) continue;
                var bar = new Rect(screen.x - width / 2, y - height / 2, width, height);
                if (target == detail)
                {
                    string status = target.IsOperational
                        ? $"{target.Health:0.#}/{target.MaxHealth:0.#}"
                        : $"{target.Health:0.#}/{target.MaxHealth:0.#} · {target.Site.Progress:P0}";
                    detailText = $"{HudPresentation.Name(target)} · {status}";
                    detailRect = new Rect(Mathf.Clamp(screen.x - 105, 0, Screen.width - 210), y - 25, 210, 19);
                }
                GUI.color = new Color(.08f,.10f,.12f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = target.Side == Side.Player ? new Color(.25f,.85f,1) : new Color(1,.32f,.2f);
                GUI.DrawTexture(new Rect(bar.x+1,bar.y+1,(bar.width-2)*target.Health/target.MaxHealth,bar.height-2),
                    Texture2D.whiteTexture);
            }
            if (detailText != null)
            {
                GUI.color = Color.white;
                HudPresentation.Panel(detailRect);
                GUI.Label(detailRect, detailText, healthLabel);
            }
            GUI.color = previous;
        }
    }
}
