using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public enum DifficultyLevel { Easy, Medium, Hard }

    public sealed class DifficultyProfile : ScriptableObject
    {
        [SerializeField] private string title;
        [SerializeField] private string description;
        [SerializeField, Min(0f)] private float firstRaidDelay;
        [SerializeField, Min(0.1f)] private float raidInterval = 150f;
        [SerializeField, Min(1)] private int firstSquadSize = 2;
        [SerializeField, Min(0)] private int squadGrowth = 1;
        [SerializeField, Min(1)] private int maxSquadSize = 5;
        [SerializeField, Min(1)] private int desiredWorkers = 3;
        [SerializeField, Min(0.1f)] private float decisionInterval = 1.2f;
        [SerializeField, Min(0)] private int capacityBuffer = 1;
        [SerializeField, Min(1)] private int garrisonSize = 1;
        [SerializeField, Min(0)] private int reserveSoldiers = 1;
        [SerializeField, Min(1)] private int barracksLimit = 1;
        [SerializeField, Min(1)] private int militaryQueueLimit = 2;
        [SerializeField, Min(0f)] private float raidWait = 12f;
        [SerializeField] private UnitKind[] squadComposition =
            { UnitKind.Infantry, UnitKind.Archer, UnitKind.Infantry, UnitKind.Cavalry };

        public string Title => title;
        public string Description => description;
        public float FirstRaidDelay => firstRaidDelay;
        public float RaidInterval => raidInterval;
        public int FirstSquadSize => firstSquadSize;
        public int SquadGrowth => squadGrowth;
        public int MaxSquadSize => maxSquadSize;
        public int DesiredWorkers => desiredWorkers;
        public float DecisionInterval => decisionInterval;
        public int CapacityBuffer => capacityBuffer;
        public int GarrisonSize => garrisonSize;
        public int ReserveSoldiers => reserveSoldiers;
        public int BarracksLimit => barracksLimit;
        public int MilitaryQueueLimit => militaryQueueLimit;
        public float RaidWait => raidWait;
        public IReadOnlyList<UnitKind> SquadComposition => squadComposition;
    }
}
