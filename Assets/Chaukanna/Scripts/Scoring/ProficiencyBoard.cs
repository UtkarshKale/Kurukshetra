using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Chaukanna.Scoring
{
    // A soldier on this PC: pseudonymous ID and a salted PIN hash. The roster that maps IDs to names stays with the
    // instructor, offline; no name is ever stored here (doc §1.11).
    [Serializable]
    public class RosterEntry
    {
        public string id;
        public string salt;
        public string pinHash;
    }

    [Serializable]
    public class Roster
    {
        public List<RosterEntry> soldiers = new List<RosterEntry>();
    }

    public static class Login
    {
        public static string Folder => Application.persistentDataPath;
        static string File(string folder) => Path.Combine(folder ?? Folder, "roster.json");

        public static Roster Load(string folder = null)
        {
            try { if (System.IO.File.Exists(File(folder))) return JsonUtility.FromJson<Roster>(System.IO.File.ReadAllText(File(folder))) ?? new Roster(); }
            catch (Exception e) { Debug.LogWarning("[Login] Could not read the roster: " + e.Message); }
            return new Roster();
        }

        static void Save(Roster r, string folder)
        {
            Directory.CreateDirectory(folder ?? Folder);
            System.IO.File.WriteAllText(File(folder), JsonUtility.ToJson(r, true));
        }

        public static string Hash(string salt, string pin)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(salt + ":" + pin));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool ValidId(string id) => !string.IsNullOrEmpty(id) && id.Length <= 16 && id.Trim() == id;
        public static bool ValidPin(string pin) => !string.IsNullOrEmpty(pin) && pin.Length >= 4 && pin.Length <= 8;

        // Signs in an existing soldier, or adds a new one. Returns an empty string on success, otherwise why not.
        public static string SignIn(string id, string pin, bool register, string folder = null)
        {
            if (!ValidId(id)) return "Soldier ID: 1-16 characters, no spaces at the ends (a pseudonym, never a name).";
            if (!ValidPin(pin)) return "PIN: 4-8 characters.";
            var roster = Load(folder);
            var entry = roster.soldiers.Find(s => s.id == id);
            if (entry == null)
            {
                if (!register) return "No soldier with that ID on this PC. Use Register.";
                entry = new RosterEntry { id = id, salt = Guid.NewGuid().ToString("N") };
                entry.pinHash = Hash(entry.salt, pin);
                roster.soldiers.Add(entry);
                Save(roster, folder);
                return "";
            }
            if (register) return "That ID is already registered on this PC.";
            return Hash(entry.salt, pin) == entry.pinHash ? "" : "Wrong PIN.";
        }
    }

    // One cell of the board: one soldier on one reference package.
    public class BoardCell
    {
        public string player, package;
        public int n;
        public float latest, mean, low, high, process = -1f;   // percent; low/high = 95 % interval when n >= 2, else -1
    }

    public class BoardFlag
    {
        public string a, b, when;
        public float p;
    }

    // The sim proficiency board (doc §1.11, module 12): scores only locked reference packages with the Director off.
    // Every figure shows n and an interval; there is a "not yet trained" list; and a duplicate-timing check flags back-to-back
    // sessions under different IDs whose decision timing is indistinguishable (two-sample KS test, p > 0.5, placeholder).
    public static class ProficiencyBoard
    {
        public const string Disclaimer = HtmlReport.Disclaimer;

        public static List<SessionResult> LoadSessions(string folder = null)
        {
            var list = new List<SessionResult>();
            string dir = folder ?? ScoreKeeper.SessionsFolder;
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "session_*.json"))
            {
                try { var s = JsonUtility.FromJson<SessionResult>(System.IO.File.ReadAllText(f)); if (s != null) list.Add(s); } catch { }
            }
            list.Sort((x, y) => string.CompareOrdinal(x.timestamp, y.timestamp));
            return list;
        }

        public static bool Counts(SessionResult s) => s.referencePackage && !s.directorOn && !string.IsNullOrEmpty(s.packageId);

        public static List<BoardCell> Cells(IList<SessionResult> sessions)
        {
            var map = new Dictionary<string, List<SessionResult>>();
            foreach (var s in sessions)
            {
                if (!Counts(s)) continue;
                string key = (s.playerId ?? "") + "\u0001" + s.packageId;
                if (!map.TryGetValue(key, out var l)) map[key] = l = new List<SessionResult>();
                l.Add(s);
            }
            var cells = new List<BoardCell>();
            foreach (var kv in map)
            {
                var l = kv.Value;
                var c = new BoardCell { player = l[0].playerId, package = l[0].packageId, n = l.Count, latest = l[l.Count - 1].ScorePercent, low = -1f, high = -1f };
                float sum = 0f, procSum = 0f; int procN = 0;
                foreach (var s in l) { sum += s.ScorePercent; if (s.processScore >= 0f) { procSum += s.processScore; procN++; } }
                c.mean = sum / l.Count;
                if (procN > 0) c.process = procSum / procN;
                if (l.Count >= 2)
                {
                    float ss = 0f;
                    foreach (var s in l) ss += (s.ScorePercent - c.mean) * (s.ScorePercent - c.mean);
                    float sd = Mathf.Sqrt(ss / (l.Count - 1));
                    float half = 1.96f * sd / Mathf.Sqrt(l.Count);
                    c.low = Mathf.Max(0f, c.mean - half);
                    c.high = Mathf.Min(100f, c.mean + half);
                }
                cells.Add(c);
            }
            cells.Sort((a, b) => a.player != b.player ? string.CompareOrdinal(a.player, b.player) : string.CompareOrdinal(a.package, b.package));
            return cells;
        }

        // Soldier x package pairs with no counted session.
        public static List<string> NotYetTrained(IList<string> soldiers, IList<string> packages, IList<BoardCell> cells)
        {
            var list = new List<string>();
            foreach (var s in soldiers)
                foreach (var p in packages)
                    { bool found = false; foreach (var c in cells) if (c.player == s && c.package == p) found = true; if (!found) list.Add($"{s}: {p}"); }
            return list;
        }

        // Decision times (call to action) of one session.
        public static List<float> Timing(SessionResult s)
        {
            var t = new List<float>();
            foreach (var c in s.contacts) if (c.decisionLatency >= 0f) t.Add(c.decisionLatency);
            return t;
        }

        // Two-sample Kolmogorov-Smirnov: the asymptotic p that both samples come from the same distribution.
        public static float KsP(List<float> a, List<float> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0f;
            var x = new List<float>(a); var y = new List<float>(b);
            x.Sort(); y.Sort();
            int i = 0, j = 0;
            float d = 0f;
            while (i < x.Count && j < y.Count)
            {
                float v = Mathf.Min(x[i], y[j]);
                while (i < x.Count && x[i] <= v) i++;
                while (j < y.Count && y[j] <= v) j++;
                d = Mathf.Max(d, Mathf.Abs((float)i / x.Count - (float)j / y.Count));
            }
            double ne = (double)x.Count * y.Count / (x.Count + y.Count);
            double lambda = (Math.Sqrt(ne) + 0.12 + 0.11 / Math.Sqrt(ne)) * d;
            if (lambda < 0.2) return 1f;
            double p = 0;
            for (int k = 1; k <= 100; k++) p += 2 * ((k & 1) == 1 ? 1 : -1) * Math.Exp(-2 * k * k * lambda * lambda);
            return (float)Math.Max(0, Math.Min(1, p));
        }

        // Back-to-back sessions on this PC (within 2 hours) under different IDs, each with at least 5 decisions, whose timing
        // cannot be told apart: possibly one person playing as several.
        public static List<BoardFlag> DuplicateTiming(IList<SessionResult> sessions, float pThreshold = 0.5f)
        {
            var flags = new List<BoardFlag>();
            for (int k = 1; k < sessions.Count; k++)
            {
                var a = sessions[k - 1]; var b = sessions[k];
                if (string.IsNullOrEmpty(a.playerId) || a.playerId == b.playerId) continue;
                if (!DateTime.TryParse(a.timestamp, out var ta) || !DateTime.TryParse(b.timestamp, out var tb) || (tb - ta).TotalHours > 2) continue;
                var x = Timing(a); var y = Timing(b);
                if (x.Count < 5 || y.Count < 5) continue;
                float p = KsP(x, y);
                if (p > pThreshold) flags.Add(new BoardFlag { a = a.playerId, b = b.playerId, when = b.timestamp, p = p });
            }
            return flags;
        }

        public static string Html(IList<BoardCell> cells, IList<string> notYet, IList<BoardFlag> flags)
        {
            var h = new StringBuilder();
            h.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Kurukshetra proficiency board</title><style>")
             .Append("body{font:14px system-ui,Segoe UI,Arial;margin:16px;color:#14181f}table{border-collapse:collapse}td,th{border-bottom:1px solid #ccc;padding:4px 8px;text-align:left}.d{background:#7a1f1f;color:#fff;font-weight:700;padding:4px 10px;display:inline-block}@media print{@page{size:A4;margin:10mm}}")
             .Append("</style></head><body><div class=\"d\">").Append(Disclaimer).Append("</div><h1>Sim proficiency board</h1>")
             .Append("<p>Locked reference packages only, Director off. Scores are the sim's, not a measure of real-sky performance.</p>")
             .Append("<table><tr><th>Soldier ID</th><th>Package</th><th>n</th><th>Latest</th><th>Mean (95 % interval)</th><th>Decision quality</th></tr>");
            foreach (var c in cells)
                h.Append("<tr><td>").Append(HtmlReport.Esc(c.player)).Append("</td><td>").Append(HtmlReport.Esc(c.package)).Append("</td><td>").Append(c.n)
                 .Append("</td><td>").Append(c.latest.ToString("F0")).Append(" %</td><td>").Append(c.mean.ToString("F0")).Append(" %")
                 .Append(c.low >= 0f ? $" ({c.low:F0}-{c.high:F0})" : " (n too small)").Append("</td><td>").Append(c.process < 0f ? "-" : c.process.ToString("F0")).Append("</td></tr>");
            h.Append("</table><h2>Not yet trained</h2><ul>");
            foreach (var s in notYet) h.Append("<li>").Append(HtmlReport.Esc(s)).Append("</li>");
            h.Append("</ul><h2>Duplicate-timing check</h2>");
            if (flags.Count == 0) h.Append("<p>No flags.</p>");
            else { h.Append("<ul>"); foreach (var f in flags) h.Append($"<li>{HtmlReport.Esc(f.a)} then {HtmlReport.Esc(f.b)} at {HtmlReport.Esc(f.when)}: timing indistinguishable (KS p = {f.p:F2}). Check who played.</li>"); h.Append("</ul>"); }
            h.Append("<p><i>").Append(Disclaimer).Append("</i></p></body></html>");
            return h.ToString();
        }

        public static string SaveHtml(string html)
        {
            try
            {
                Directory.CreateDirectory(HtmlReport.ReportsFolder);
                string path = Path.Combine(HtmlReport.ReportsFolder, $"board_{DateTime.Now:yyyyMMdd_HHmmss}.html");
                System.IO.File.WriteAllText(path, html, new UTF8Encoding(false));
                return path;
            }
            catch (Exception e) { Debug.LogWarning("[Board] Could not save: " + e.Message); return null; }
        }
    }
}
