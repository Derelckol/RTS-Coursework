using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace Rts
{
    public static class SaveStore
    {
        public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "Saves");
        public static string[] Files() => Directory.Exists(DirectoryPath)
            ? Directory.GetFiles(DirectoryPath, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToArray() : Array.Empty<string>();
        private static string SafePath(string path)
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) != Path.GetFullPath(DirectoryPath) || Path.GetExtension(path) != ".json")
                throw new InvalidDataException("Некоректний шлях збереження.");
            return path;
        }
        public static MatchSave Read(string path)
        {
            SafePath(path);
            var info = new FileInfo(path);
            if (info.Length > 32 * 1024 * 1024) throw new InvalidDataException("Файл збереження завеликий.");
            var save = JsonUtility.FromJson<MatchSave>(File.ReadAllText(path));
            Validate(save);
            Need(!string.IsNullOrWhiteSpace(save.name) && save.name.Length <= 60 && !save.name.Any(char.IsControl) &&
                DateTime.TryParse(save.utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out _), "назва або дата.");
            return save;
        }
        public static string Write(MatchSave save, string displayName, string overwrite = null)
        {
            displayName = displayName?.Trim();
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 60 || displayName.Any(char.IsControl))
                throw new InvalidDataException("Назва має містити від 1 до 60 символів без керівних знаків.");
            save.name = displayName;
            save.utc = DateTime.UtcNow.ToString("O");
            Validate(save);
            Directory.CreateDirectory(DirectoryPath);
            string path = overwrite == null ? Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".json") : SafePath(overwrite);
            string temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(JsonUtility.ToJson(save, true));
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                return path;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public static void Delete(string path)
        {
            File.Delete(SafePath(path));
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
        private static void Need(bool condition, string reason)
        { if (!condition) throw new InvalidDataException("Некоректне збереження: " + reason); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Vector(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z);
        private static bool Point(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) && p.x >= -40 && p.x <= 40 && p.z >= -30 && p.z <= 30;
        public static void Validate(MatchSave s)
        {
            Need(s != null && s.version == 1, "непідтримувана версія формату.");
            Need(s.map == "RtsMatchPrototype-v1", "інша карта.");
            Need(Enum.IsDefined(typeof(DifficultyLevel), s.difficulty) && Finite(s.time) && s.time >= 0, "складність або час.");
            Need(s.actors != null && s.actors.Length <= 10000 && s.mines != null && s.mines.Length == 4 &&
                s.economies?.Length == 2 && s.knowledge?.Length == 2 && s.director != null, "відсутні розділи.");
            var ids = new HashSet<string>();
            foreach (var a in s.actors)
                Need(a != null && !string.IsNullOrEmpty(a.id) && ids.Add(a.id) && Point(a.position) &&
                    Enum.IsDefined(typeof(Side), a.side) && Enum.IsDefined(typeof(BuildingKind), a.building) &&
                    Enum.IsDefined(typeof(UnitKind), a.kind) && Finite(a.health) && Finite(a.maxHealth) && a.health > 0 && a.health <= a.maxHealth &&
                    Finite(a.upgradeRemaining) && a.upgradeRemaining >= 0 && Finite(a.nextBuildingAttack) && a.nextBuildingAttack >= 0 &&
                    (!a.upgraded || a.upgradeRemaining == 0) &&
                    ((!a.upgraded && a.upgradeRemaining == 0) || a.building != BuildingKind.None && a.constructionDuration == 0) &&
                    a.cargo >= 0 && Finite(a.constructionElapsed) && Finite(a.constructionDuration) && a.constructionElapsed >= 0 && a.constructionDuration >= a.constructionElapsed &&
                    Vector(a.scale) && a.scale.x > 0 && a.scale.y > 0 && a.scale.z > 0 && a.scale.x <= 20 && a.scale.y <= 20 && a.scale.z <= 20 &&
                    Finite(a.rotation.x) && Finite(a.rotation.y) && Finite(a.rotation.z) && Finite(a.rotation.w) &&
                    Finite(a.workRange) && a.workRange >= 0 && a.blocked >= 0 && a.replans >= 0 &&
                    (a.building != BuildingKind.None || a.order != null && a.route != null), "об’єкт або повторний ID.");
            foreach (var m in s.mines)
                Need(m != null && !string.IsNullOrEmpty(m.id) && ids.Add(m.id) && Point(m.position) && m.stock >= 0, "родовище або повторний ID.");
            Need(s.mines.Select(m => m.position).Distinct().Count() == s.mines.Length, "повторні родовища.");
            var actors = s.actors.ToDictionary(a => a.id);
            bool Actor(string id) => string.IsNullOrEmpty(id) || actors.ContainsKey(id);
            bool Unit(string id) => string.IsNullOrEmpty(id) || actors.TryGetValue(id, out var a) && a.building == BuildingKind.None;
            var mineIds = new HashSet<string>(s.mines.Select(m => m.id));
            foreach (var a in s.actors)
            {
                Need(Unit(a.builder) && (string.IsNullOrEmpty(a.builder) || a.constructionDuration > 0 &&
                    actors[a.builder].side == a.side && actors[a.builder].kind == UnitKind.Worker &&
                    actors[a.builder].order?.state == UnitState.Build && actors[a.builder].order.target == a.id), "ID будівельника.");
                Need(a.building != BuildingKind.None || a.constructionDuration == 0 && string.IsNullOrEmpty(a.builder), "фундамент замість юніта.");
                Need(a.constructionDuration == 0 || a.workRange > 0 && a.building != BuildingKind.None, "характеристики будівництва.");
                if (a.order == null) continue;
                var o = a.order;
                Need(Enum.IsDefined(typeof(UnitState), o.state) && Actor(o.target) && Actor(o.blockedThreat) &&
                    (string.IsNullOrEmpty(o.source) || mineIds.Contains(o.source)) &&
                    new[] { o.nextAction, o.nextPlan, o.nextScan, o.harvestAt, o.lastProgress, o.retryThreatAt, a.blocked }.All(Finite) &&
                    Vector(o.lastTargetPosition) && Vector(o.defensePost) && Vector(o.breachGoal) && Vector(o.progressPosition) && o.failedPlans >= 0 &&
                    a.route.Length <= 10000 && a.route.All(Point), "наказ або посилання.");
                if (o.state == UnitState.Build) Need(!string.IsNullOrEmpty(o.target) && actors[o.target].constructionDuration > 0, "ціль будівництва.");
            }
            for (int side = 0; side < 2; side++)
            {
                var e = s.economies[side]; var k = s.knowledge[side];
                Need(e != null && (int)e.side == side && e.gold >= 0 && e.jobs != null && e.used == s.actors.Count(a => (int)a.side == side && a.building == BuildingKind.None) &&
                    e.reserved == e.jobs.Length && e.maximum >= 0, "економіка або населення.");
                foreach (var j in e.jobs)
                    Need(j != null && actors.TryGetValue(j.building ?? "", out var b) && b.side == e.side && b.constructionDuration == 0 &&
                        (b.building == BuildingKind.TownHall && j.kind == UnitKind.Worker || b.building == BuildingKind.Barracks && j.kind != UnitKind.Worker) &&
                        Enum.IsDefined(typeof(UnitKind), j.kind) && Finite(j.remaining) && j.remaining >= 0 && Finite(j.retry), "черга виробництва.");
                Need(k != null && k.explored?.Length == 4800 && k.buildings != null && k.mines != null && k.explored.All(v => v >= 0 && v <= 2), "знання сторони.");
                foreach (var m in k.mines) Need(m != null && mineIds.Contains(m.id ?? "") && m.stock >= 0, "відоме родовище.");
                foreach (var b in k.buildings) Need(Point(b.position) && Vector(b.size) && b.size.x > 0 && b.size.z > 0 &&
                    b.type != BuildingKind.None && Enum.IsDefined(typeof(BuildingKind), b.type), "пам’ять споруди.");
            }
            var d = s.director;
            Need(d.raiders != null && d.garrison != null && d.assembly != null && d.threats != null && d.stations != null && d.contacts != null && d.attempts != null &&
                Unit(d.scout) && Actor(d.defenseTarget) && d.raiders.Concat(d.garrison).Concat(d.assembly).All(id => !string.IsNullOrEmpty(id) && Unit(id)) &&
                d.threats.All(id => !string.IsNullOrEmpty(id) && Actor(id)) &&
                new[] { d.elapsed, d.nextDecision, d.nextRaid, d.scoutRetry, d.lastRaidAt, s.orderTime, s.camera.size }.All(Finite) &&
                Point(s.camera.center) && s.camera.size >= 6 && s.camera.size <= 16, "стан ШІ або камери.");
            Need(d.stations.Select(p => p?.unit).Distinct().Count() == d.stations.Length &&
                d.contacts.Select(c => c?.target).Distinct().Count() == d.contacts.Length &&
                d.attempts.All(a => a != null && Enum.IsDefined(typeof(BuildingKind), a.kind) && a.attempts >= 0) &&
                d.attempts.Select(a => a.kind).Distinct().Count() == d.attempts.Length &&
                Enum.IsDefined(typeof(OpponentMode), d.mode) && Vector(d.scoutGoal) && d.explorationOffset >= 0 && d.raidsLaunched >= 0, "структура стану ШІ.");
            foreach (var p in d.stations) Need(p != null && !string.IsNullOrEmpty(p.unit) && Unit(p.unit) &&
                actors[p.unit].side == Side.Opponent && p.role >= 0 && p.role <= 2 && Finite(p.retry) && Vector(p.position), "позиція ШІ.");
            foreach (var c in d.contacts) Need(c != null && !string.IsNullOrEmpty(c.target) && Actor(c.target) && Finite(c.expires), "контакт ШІ.");
        }
    }
}
