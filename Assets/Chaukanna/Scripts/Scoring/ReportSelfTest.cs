using System.Collections.Generic;
using System.IO;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Scoring
{
    // Checks for module 8: "your call" vs "the sound call" and the static HTML report.
    // CLI: Chaukanna.Scoring.ReportSelfTest.Run()
    public static class ReportSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var rules = RuleSet.Defaults();

            // The sound call is the least escalatory action the card rates Sound.
            Check("sound call for a strike quad: Jam under Card A and B, Report under Card C",
                ScoreCalculator.SoundAction(rules, RoeCard.CardA, ContactClass.HostileQuad) == PlayerAction.Jam
                && ScoreCalculator.SoundAction(rules, RoeCard.CardB, ContactClass.HostileQuad) == PlayerAction.Jam
                && ScoreCalculator.SoundAction(rules, RoeCard.CardC, ContactClass.HostileQuad) == PlayerAction.Report);
            Check("sound call for an own drone: Report under every card", ScoreCalculator.SoundAction(rules, RoeCard.CardA, ContactClass.FriendlyISR) == PlayerAction.Report
                && ScoreCalculator.SoundAction(rules, RoeCard.CardB, ContactClass.FriendlyISR) == PlayerAction.Report && ScoreCalculator.SoundAction(rules, RoeCard.WeaponsHold, ContactClass.FriendlyISR) == PlayerAction.Report);
            Check("sound call for a hostile under Under Attack: Jam", ScoreCalculator.SoundAction(rules, RoeCard.UnderAttack, ContactClass.HostileFixedWing) == PlayerAction.Jam);

            // A strike quad held until it reached the post under Card A.
            var held = Approach(Rec(ContactClass.HostileQuad, PlayerCall.HostileQuad, PlayerAction.Hold, RoeCard.CardA, 14f));
            held.despawnTime = 41f; held.despawnReason = "ReachedDefendedPoint";
            var h = One(held, rules, RoeCard.CardA);
            Check("held strike quad: sound call is Jam by its release point, consequence says it reached the post",
                !h.matchedSound && h.soundAction == "Jam" && h.soundCall.Contains("by 30.0 s") && h.consequence.Contains("reached the post") && h.yourCall.Contains("Hold"));

            // The right call and a sound action in time: matched.
            var jam = Approach(Rec(ContactClass.HostileQuad, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.CardA, 20f));
            var j = One(jam, rules, RoeCard.CardA);
            Check("jammed in time with the right call: 'your call was the sound call'", j.matchedSound && j.consequence == "your call was the sound call");

            // Own drone shot down: the sound call reports it and it would still be flying.
            var ff = Rec(ContactClass.FriendlyISR, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.CardB, 15f);
            ff.shots = 1; ff.engageHit = true;
            var f = One(ff, rules, RoeCard.CardB);
            Check("own drone engaged: sound call 'Own ISR drone, Report', the own drone would still be flying", !f.matchedSound && f.soundCall.StartsWith("Own ISR drone, Report") && f.consequence.Contains("would still be flying"));

            // Own drone left alone: a correct rejection is the sound call too.
            var alone = One(Rec(ContactClass.FriendlyISR, PlayerCall.OwnIsrDrone, PlayerAction.None, RoeCard.CardB, -1f), rules, RoeCard.CardB);
            Check("own drone called right and left alone: matches the sound call", alone.matchedSound);

            // Never brought into view: the sound call is to search.
            var unseen = new ContactRecord { id = 9, truthClass = ContactClass.HostileQuad, spawnTime = 5f, firstDetectableTime = 8f };
            var u = One(unseen, rules, RoeCard.CardA);
            Check("never seen: sound call is to search the sector from when it could be seen", u.soundCall.Contains("search the sector") && u.soundCall.Contains("8.0 s") && u.yourCall == "never brought into view");

            // The HTML report.
            held.track.Add(new TrackPoint { t = 12f, x = 0f, y = 50f, z = 400f, inView = true });
            held.track.Add(new TrackPoint { t = 12.5f, x = 0f, y = 50f, z = 390f, inView = false });
            ff.id = 2;
            var tricky = Rec(ContactClass.Bird, PlayerCall.Bird, PlayerAction.Report, RoeCard.CardB, 14f);
            tricky.id = 3;
            var session = ScoreCalculator.Calculate(new[] { held, ff, tricky, unseen }, rules, 120f, 42, null, RoeCard.CardB);
            session.timestamp = "2026-10-15 10:00:00";
            // The replay is drawn relative to the logged post; use a known one and put the live one back after.
            Vector3 savedPost = RunLog.post;
            float savedYaw = RunLog.sectorYaw;
            RunLog.post = Vector3.zero;
            RunLog.sectorYaw = 0f;
            string html = HtmlReport.Build(session, new[] { held, ff, tricky, unseen }, "Sepoy <Test> & Co");
            RunLog.post = savedPost;
            RunLog.sectorYaw = savedYaw;
            Check("report carries the 'Training use only' line at the top and in the footer", html.IndexOf(HtmlReport.Disclaimer) > 0 && html.IndexOf(HtmlReport.Disclaimer) != html.LastIndexOf(HtmlReport.Disclaimer));
            Check("report footer points to CREDITS.txt and the report is titled Kurukshetra", html.Contains("CREDITS.txt") && html.Contains("<h1>Kurukshetra after-action review</h1>") && !html.Contains("Chaukanna"));
            Check("report has every section: debrief, decision quality, timeline, replay, every contact, confusion",
                html.Contains("<h2>Debrief</h2>") && html.Contains("<h2>Decision quality</h2>") && html.Contains("Timeline: in view") && html.Contains("<h2>Replay</h2>") && html.Contains("<h2>Every contact</h2>") && html.Contains("Confusion"));
            Check("names are escaped (no raw <Test>)", !html.Contains("<Test>") && html.Contains("Sepoy &lt;Test&gt; &amp; Co"));
            Check("the four TC 25-20 questions are there", html.Contains("What was planned?") && html.Contains("What happened?") && html.Contains("Why?") && html.Contains("How to improve?"));
            Check("debrief names the weak areas it found (search failure, clock miss)", html.Contains("Sector search") && html.Contains("Decide before the release point"));
            Check("the replay data is embedded and the in-view sample is in it", html.Contains("id=\"replay-data\"") && html.Contains("[12.0,0,400,1]"));
            Check("your call / sound call / consequence appear in the contact table", html.Contains(session.contacts[0].soundCall) && html.Contains("would still be flying"));
            Check("no external files or network: no http(s) script or stylesheet links", !html.Contains("<script src") && !html.Contains("<link "));
            Check("d'/c show 'insufficient data' for a short run", html.Contains("insufficient data"));

            // Written to disk and read back.
            string path = Path.Combine(Application.temporaryCachePath, "chaukanna_report_selftest.html");
            File.WriteAllText(path, html);
            Check("written to a file and read back unchanged", File.Exists(path) && File.ReadAllText(path) == html);
            File.Delete(path);

            // Track sampling.
            var rec = new ContactRecord();
            rec.SampleTrack(1f, new Vector3(1f, 2f, 3f), true);
            Check("a track sample keeps time, position and whether it was on screen", rec.track.Count == 1 && rec.track[0].inView && Mathf.Approximately(rec.track[0].z, 3f));

            string summary = $"[ReportSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static ContactRecord Approach(ContactRecord r)
        {
            r.rangeTrack.Add(new Vector2(10f, 900f));
            r.rangeTrack.Add(new Vector2(20f, 400f));
            r.rangeTrack.Add(new Vector2(30f, 140f));
            r.rangeTrack.Add(new Vector2(40f, 50f));
            return r;
        }

        static ContactRecord Rec(ContactClass truth, PlayerCall call, PlayerAction action, RoeCard card, float actionTime)
        {
            return new ContactRecord
            {
                id = 1,
                truthClass = truth,
                spawnTime = 10f,
                firstDetectableTime = 10f,
                firstInViewTime = 12f,
                firstLockTime = 12f,
                call = call,
                callTime = call == PlayerCall.None ? -1f : 13f,
                callRange = 400f,
                action = action,
                actionTime = action == PlayerAction.None ? -1f : actionTime,
                roeAtAction = card,
            };
        }

        static ContactResult One(ContactRecord r, RuleSet rules, RoeCard start)
        {
            return ScoreCalculator.Calculate(new[] { r }, rules, 300f, 0, null, start).contacts[0];
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
