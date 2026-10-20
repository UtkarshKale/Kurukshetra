using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scoring
{
    // One player's finished (or running) session, with who and what unit played it.
    public class SessionPart
    {
        public string player = "", seat = "", unit = "Unit 1";
        public SessionResult session;
        public IList<Chaukanna.Trainer.TrendPoint> history;     // that player's saved sessions, oldest first (may be null)
    }

    // The report filter: All players, one unit, or one player. A view is a SessionResult too, so every card, chart and table
    // that draws a SessionResult (the dashboard's cards and charts, the HTML report) works on a combined view unchanged.
    public static class SessionFilter
    {
        public const string AllKey = "all";
        public static string UnitKey(string unit) => "u:" + unit;
        public static string PlayerKey(string player) => "p:" + player;

        public static string Label(string key, int players)
        {
            if (key == AllKey) return $"All players ({players})";
            if (key.StartsWith("u:")) return key.Substring(2) + $" ({players} player{(players == 1 ? "" : "s")})";
            return key.StartsWith("p:") ? key.Substring(2) : key;
        }

        // The parts a key selects. "all" = every part with a session; "u:Unit 1" = that unit's; "p:Name" = that player's.
        public static List<SessionPart> Select(IList<SessionPart> parts, string key)
        {
            var list = new List<SessionPart>();
            if (parts == null) return list;
            foreach (var p in parts)
            {
                if (p == null || p.session == null) continue;
                if (key == null || key == AllKey || (key.StartsWith("u:") && p.unit == key.Substring(2)) || (key.StartsWith("p:") && p.player == key.Substring(2))) list.Add(p);
            }
            return list;
        }

        public static List<string> Units(IList<SessionPart> parts)
        {
            var units = new List<string>();
            if (parts != null) foreach (var p in parts) if (p != null && p.session != null && !units.Contains(p.unit)) units.Add(p.unit);
            return units;
        }

        // The session a key shows: null when nothing matches; the player's own session (same object) when exactly one part matches;
        // otherwise their contacts and counts combined.
        public static SessionResult View(IList<SessionPart> parts, string key)
        {
            var sel = Select(parts, key);
            if (sel.Count == 0) return null;
            Tag(sel);
            if (sel.Count == 1) return sel[0].session;
            return Combine(sel, Label(key, sel.Count));
        }

        // The saved sessions to show beside a view: the one player's own history, nothing for a combined view (trends do not add up).
        public static IList<Chaukanna.Trainer.TrendPoint> HistoryFor(IList<SessionPart> parts, string key)
        {
            var sel = Select(parts, key);
            return sel.Count == 1 ? sel[0].history : null;
        }

        // Every contact carries its player, seat and unit, filled from the part when the session itself did not say.
        public static void Tag(IList<SessionPart> parts)
        {
            foreach (var p in parts)
            {
                if (p == null || p.session == null) continue;
                var s = p.session;
                if (string.IsNullOrEmpty(s.playerId)) s.playerId = p.player;
                if (string.IsNullOrEmpty(s.seatId)) s.seatId = p.seat ?? "";
                if (string.IsNullOrEmpty(s.unitId)) s.unitId = string.IsNullOrEmpty(p.unit) ? "Unit 1" : p.unit;
                foreach (var c in s.contacts)
                {
                    if (string.IsNullOrEmpty(c.playerId)) c.playerId = !string.IsNullOrEmpty(p.player) ? p.player : s.playerId;
                    if (string.IsNullOrEmpty(c.seatId)) c.seatId = p.seat ?? s.seatId;
                    if (string.IsNullOrEmpty(c.unitId)) c.unitId = string.IsNullOrEmpty(p.unit) ? s.unitId : p.unit;
                }
            }
        }

        // One line of the unit comparison: a unit (isUnit) or one player in it. -1 = n/a.
        public class CompareRow
        {
            public string key, label;
            public bool isUnit;
            public int players, contacts, qualityN;
            public float detection = -1f, quality = -1f, goodOutcome = -1f, avgScore = -1f;
        }

        public const string SoloNote = "Unit views need a LAN run: with one player there is only one unit to look at.";

        // Per unit: players, contacts, detection, decision quality, outcome and average score, each followed by its players.
        public static List<CompareRow> Compare(IList<SessionPart> parts)
        {
            var rows = new List<CompareRow>();
            foreach (var u in Units(parts))
            {
                string k = UnitKey(u);
                var sel = Select(parts, k);
                rows.Add(Row(k, u, true, sel.Count, View(parts, k)));
                foreach (var p in sel) rows.Add(Row(PlayerKey(p.player), p.player, false, 1, p.session));
            }
            return rows;
        }

        static CompareRow Row(string key, string label, bool unit, int players, SessionResult s)
        {
            var r = new CompareRow { key = key, label = label, isUnit = unit, players = players };
            if (s == null) return r;
            int grid = s.gridSoundSuccessful + s.gridSoundUnlucky + s.gridLucky + s.gridUnsound;
            r.contacts = s.scoredContacts;
            r.detection = s.scoredContacts > 0 ? 100f * s.detected / s.scoredContacts : -1f;
            r.quality = s.processScore;
            r.qualityN = s.processScore < 0f ? 0 : s.processCount > 0 ? s.processCount : grid;
            r.goodOutcome = grid > 0 ? 100f * (s.gridSoundSuccessful + s.gridLucky) / grid : -1f;
            r.avgScore = s.maxPoints > 0 ? s.ScorePercent : -1f;
            return r;
        }

        public static string Pct(float v) => v < 0f ? "n/a" : v.ToString("F0") + " %";

        static bool IsRight(ContactResult c) => c.classBranch == "Right" || c.classBranch == "Caught deception" || c.classBranch == "Second guess";

        // Counts add up; the means are recomputed from the contacts. Swarm, group-call and card-change results add their counts.
        public static SessionResult Combine(IList<SessionPart> parts, string label)
        {
            var first = parts[0].session;
            var m = JsonUtility.FromJson<SessionResult>(JsonUtility.ToJson(first));     // conditions, labels and tables of the first, then overwritten
            m.contacts = new List<ContactResult>();
            m.playerId = label;
            m.seatId = "";
            m.unitId = "";
            m.seatRole = "";
            m.blackBox = ""; m.voiceFolder = "";
            m.totalPoints = m.maxPoints = 0;
            m.scoredContacts = m.notScored = m.notDetectable = m.detected = m.missed = m.unresolved = m.falseEngagements = m.missedHostiles = 0;
            m.decisionChanges = m.groupDecisions = m.deceptiveContacts = m.deceptionsCaught = 0;
            m.aiHintsShown = m.wrongAiHints = m.followedWrongHint = m.overrodeWrongHintCorrectly = 0;
            m.gridSoundSuccessful = m.gridSoundUnlucky = m.gridLucky = m.gridUnsound = 0;
            m.clockMisses = m.correctRejections = m.unverifiedEngagements = m.searchFailures = m.perceptionFailures = 0;
            m.handledByTeam = m.sentryReports = m.commanderDelayCount = m.upstreamWaivers = m.reportAloneEngagements = m.handoffFratricides = 0;
            m.crowdTracks = m.crowdPeak = m.crowdChecked = m.iffChallenges = m.crowdFriendliesHit = 0;
            m.sentryReportLatencyMean = m.commanderDelayMean = -1f;
            m.confusion = new int[first.confusion != null ? first.confusion.Length : 0];
            m.duration = 0f;

            int rHits = 0, rSig = 0, rFa = 0, rNoise = 0, eHits = 0, eSig = 0, eFa = 0, eNoise = 0, minTrials = 5;
            var sw = new SwarmTriageResult();
            var gc = new GroupCallResult();
            var cc = new CardChangeResult();
            foreach (var p in parts)
            {
                var s = p.session;
                m.duration = Mathf.Max(m.duration, s.duration);
                m.totalPoints += s.totalPoints; m.maxPoints += s.maxPoints;
                m.scoredContacts += s.scoredContacts; m.notScored += s.notScored; m.notDetectable += s.notDetectable;
                m.detected += s.detected; m.missed += s.missed; m.unresolved += s.unresolved;
                m.falseEngagements += s.falseEngagements; m.missedHostiles += s.missedHostiles;
                m.decisionChanges += s.decisionChanges; m.groupDecisions += s.groupDecisions;
                m.deceptiveContacts += s.deceptiveContacts; m.deceptionsCaught += s.deceptionsCaught;
                m.aiHintsShown += s.aiHintsShown; m.wrongAiHints += s.wrongAiHints; m.followedWrongHint += s.followedWrongHint; m.overrodeWrongHintCorrectly += s.overrodeWrongHintCorrectly;
                m.gridSoundSuccessful += s.gridSoundSuccessful; m.gridSoundUnlucky += s.gridSoundUnlucky; m.gridLucky += s.gridLucky; m.gridUnsound += s.gridUnsound;
                m.clockMisses += s.clockMisses; m.correctRejections += s.correctRejections; m.unverifiedEngagements += s.unverifiedEngagements;
                m.searchFailures += s.searchFailures; m.perceptionFailures += s.perceptionFailures;
                m.handledByTeam += s.handledByTeam; m.sentryReports += s.sentryReports; m.commanderDelayCount += s.commanderDelayCount;
                m.upstreamWaivers += s.upstreamWaivers; m.reportAloneEngagements += s.reportAloneEngagements; m.handoffFratricides += s.handoffFratricides;
                m.crowdTracks += s.crowdTracks; m.crowdPeak = Mathf.Max(m.crowdPeak, s.crowdPeak); m.crowdChecked += s.crowdChecked;
                m.iffChallenges += s.iffChallenges; m.crowdFriendliesHit += s.crowdFriendliesHit;
                if (s.confusion != null && s.confusion.Length == m.confusion.Length) for (int i = 0; i < m.confusion.Length; i++) m.confusion[i] += s.confusion[i];
                if (s.reportSdt != null) { rHits += s.reportSdt.hits; rSig += s.reportSdt.signals; rFa += s.reportSdt.falseAlarms; rNoise += s.reportSdt.noise; }
                if (s.engageSdt != null) { eHits += s.engageSdt.hits; eSig += s.engageSdt.signals; eFa += s.engageSdt.falseAlarms; eNoise += s.engageSdt.noise; }

                if (s.swarm != null && s.swarm.happened)
                {
                    sw.happened = true; sw.size += s.swarm.size; sw.seen += s.swarm.seen; sw.actedOn += s.swarm.actedOn;
                    sw.nearestFirst += s.swarm.nearestFirst; sw.leakers += s.swarm.leakers; sw.points += s.swarm.points; sw.maxPoints += s.swarm.maxPoints;
                }
                if (s.groupCall != null && s.groupCall.happened)
                {
                    gc.happened = true; gc.calls += s.groupCall.calls; gc.right += s.groupCall.right; gc.wrong += s.groupCall.wrong;
                    gc.points += s.groupCall.points; gc.maxPoints += s.groupCall.maxPoints;
                }
                if (s.cardChange != null && s.cardChange.happened)
                {
                    if (!cc.happened) { cc.from = s.cardChange.from; cc.to = s.cardChange.to; cc.time = s.cardChange.time; cc.adaptationSeconds = s.cardChange.adaptationSeconds; }
                    cc.happened = true; cc.staleErrors += s.cardChange.staleErrors; cc.decisionsAfter += s.cardChange.decisionsAfter;
                    cc.points += s.cardChange.points; cc.maxPoints += s.cardChange.maxPoints;
                }

                foreach (var c in s.contacts) m.contacts.Add(c);
            }

            // Means from the contacts themselves.
            float detSum = 0f; int detN = 0, right = 0, actioned = 0, correct = 0, procN = 0, procSum = 0;
            foreach (var c in m.contacts)
            {
                if (!c.scored) continue;
                if (c.processScore >= 0) { procSum += c.processScore; procN++; }
                if (!c.detected) continue;
                detSum += c.detectionSeconds; detN++;
                if (IsRight(c)) right++;
                if (c.action != "none" && !string.IsNullOrEmpty(c.action))
                {
                    actioned++;
                    if (c.nodes != null && c.nodes.Count > 2 && c.nodes[2].passed) correct++;
                }
            }
            m.averageDetectionSeconds = detN > 0 ? detSum / detN : -1f;
            m.classificationAccuracy = detN > 0 ? 100f * right / detN : 0f;
            m.engagementDecisionScore = actioned > 0 ? 100f * correct / actioned : 0f;
            m.processScore = procN > 0 ? (float)procSum / procN : -1f;
            m.processCount = procN;
            m.reportSdt = SdtResult.From(rHits, rSig, rFa, rNoise, minTrials);
            m.engageSdt = SdtResult.From(eHits, eSig, eFa, eNoise, minTrials);

            if (sw.happened)
            {
                sw.nearestFirstRate = sw.actedOn > 0 ? (float)sw.nearestFirst / sw.actedOn : -1f;
                sw.summary = $"{parts.Count} players combined: swarm members {sw.size}, {sw.seen} seen, {sw.actedOn} acted on, {sw.leakers} reached the post ({sw.points} of {sw.maxPoints})";
            }
            m.swarm = sw;
            if (gc.happened) gc.summary = $"{parts.Count} players combined: {gc.calls} group calls, {gc.right} right, {gc.wrong} wrong ({gc.points} of {gc.maxPoints})";
            m.groupCall = gc;
            if (cc.happened) cc.summary = $"{parts.Count} players combined: {cc.staleErrors} stale-card errors, {cc.decisionsAfter} decisions after the change ({cc.points} of {cc.maxPoints})";
            m.cardChange = cc;
            return m;
        }
    }
}
