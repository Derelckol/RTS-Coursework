using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    [Serializable] public sealed class MatchSave
    {
        public int version = 1;
        public string map = "RtsMatchPrototype-v1";
        public string name, utc;
        public DifficultyLevel difficulty;
        public float time;
        public CameraViewState camera;
        public ActorSave[] actors;
        public MineSave[] mines;
        public EconomySave[] economies;
        public KnowledgeSave[] knowledge;
        public DirectorSave director;
        public float orderTime;
    }
    [Serializable] public sealed class ActorSave
    {
        public string id;
        public Side side;
        public BuildingKind building;
        public UnitKind kind;
        public Vector3 position, scale;
        public Quaternion rotation;
        public float health, maxHealth;
        public int cargo;
        public bool upgraded;
        public float upgradeRemaining, nextBuildingAttack;
        public float constructionDuration, constructionElapsed, workRange;
        public string builder;
        public OrderSave order;
        public Vector3[] route;
        public float blocked;
        public int replans;
    }
    [Serializable] public sealed class OrderSave
    {
        public UnitState state;
        public string target, source, blockedThreat;
        public float nextAction, nextPlan, nextScan, harvestAt, lastProgress, retryThreatAt;
        public int failedPlans;
        public Vector3 lastTargetPosition, defensePost, breachGoal, progressPosition;
        public bool selfDefense;
    }
    [Serializable] public sealed class MineSave { public string id; public Vector3 position; public int stock; }
    [Serializable] public sealed class JobSave { public string building; public UnitKind kind; public float remaining, retry; }
    [Serializable] public sealed class EconomySave { public Side side; public int gold, used, reserved, maximum; public JobSave[] jobs; }
    [Serializable] public sealed class MineKnowledge { public string id; public int stock; }
    [Serializable] public sealed class KnowledgeSave { public int[] explored; public BuildingMemory[] buildings; public MineKnowledge[] mines; }
    [Serializable] public sealed class StationSave { public string unit; public int role, slot; public bool positioned; public Vector3 position; public float retry; }
    [Serializable] public sealed class ContactSave { public string target; public float expires; }
    [Serializable] public sealed class SiteAttemptSave { public BuildingKind kind; public int attempts; }
    [Serializable] public sealed class DirectorSave
    {
        public float elapsed, nextDecision, nextRaid, scoutRetry, lastRaidAt;
        public int explorationOffset, nextReserveKind, raidsLaunched, lastRaidSize;
        public OpponentMode mode;
        public string scout, defenseTarget;
        public Vector3 scoutGoal;
        public string[] raiders, garrison, assembly, threats;
        public StationSave[] stations;
        public ContactSave[] contacts;
        public SiteAttemptSave[] attempts;
    }

    public sealed class SaveIdentity : MonoBehaviour
    {
        public string Id { get; private set; }
        public static string Of(Component component)
        {
            if (component == null) return null;
            var identity = component.GetComponent<SaveIdentity>() ?? component.gameObject.AddComponent<SaveIdentity>();
            return identity.Id ??= Guid.NewGuid().ToString("N");
        }
        public static void Assign(Component component, string id)
        {
            var identity = component.GetComponent<SaveIdentity>() ?? component.gameObject.AddComponent<SaveIdentity>();
            identity.Id = id;
        }
    }
}
