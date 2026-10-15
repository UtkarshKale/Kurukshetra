using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Net
{
    // Checks for the LAN layer that need no second PC: snapshot packing, roster readiness, the start packet and messages.
    // CLI: Chaukanna.Net.NetSelfTest.Run()
    public static class NetSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            // Snapshot: 48 contacts round trip within a few centimetres.
            var origin = new Vector3(1200f, 300f, -800f);
            var list = new List<SnapContact>();
            var rng = new System.Random(3);
            for (int i = 0; i < 48; i++)
                list.Add(new SnapContact { id = 100 + i, position = origin + new Vector3((float)(rng.NextDouble() - 0.5) * 6000f, 0f, (float)(rng.NextDouble() - 0.5) * 6000f) + Vector3.up * (float)(rng.NextDouble() * 900f - 290f), yaw = (float)rng.NextDouble() * 360f });
            var bytes = Snapshot.Pack(123.4f, origin, list, 0, list.Count);
            var back = new List<SnapContact>();
            float t = Snapshot.Unpack(bytes, origin, back);
            float worst = 0f, worstYaw = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                worst = Mathf.Max(worst, Vector3.Distance(list[i].position, back[i].position));
                worstYaw = Mathf.Max(worstYaw, Mathf.Abs(Mathf.DeltaAngle(list[i].yaw, back[i].yaw)));
            }
            Check($"snapshot of 48 contacts: {bytes.Length} bytes, worst position error {worst * 100f:F1} cm, heading {worstYaw:F1} deg, time kept",
                back.Count == 48 && back[47].id == 147 && worst < 0.1f && worstYaw < 1f && Mathf.Approximately(t, 123.4f) && bytes.Length == 6 + 48 * Snapshot.BytesPerContact);
            Check($"{Snapshot.MaxPerMessage} contacts fit one message of about 1 KB ({6 + Snapshot.MaxPerMessage * Snapshot.BytesPerContact} bytes)", 6 + Snapshot.MaxPerMessage * Snapshot.BytesPerContact <= 1100);
            var empty = Snapshot.Pack(5f, origin, list, 0, 0);
            Check("an empty sky still sends the clock", Mathf.Approximately(Snapshot.Unpack(empty, origin, back), 5f) && back.Count == 0);

            // Roster.
            var roster = new NetRoster();
            roster.players.Add(new NetPlayer { clientId = 0, name = "Trainer", host = true });
            roster.players.Add(new NetPlayer { clientId = 1, name = "S-014" });
            roster.players.Add(new NetPlayer { clientId = 2, name = "S-020", ready = true });
            bool notYet = !roster.AllReady();
            roster.Find(1).ready = true;
            Check("START waits until every trainee is READY (the host does not need to be)", notYet && roster.AllReady());
            var ro2 = JsonUtility.FromJson<NetRoster>(JsonUtility.ToJson(roster));
            Check("roster survives the wire (names, ids, mode)", ro2.players.Count == 3 && ro2.Find(2).name == "S-020" && ro2.players[0].host);

            // Start packet: the scenario arrives intact, so a Solo trainee builds the same run from the same seed.
            var cfg = new ScenarioConfig { seed = 4242, hostileCount = 5, roeAtStart = RoeCard.CardB, roeFlip = true, roeFlipTo = RoeCard.CardA, prevalence = Prevalence.Low, director = true };
            var packet = new StartPacket { mode = NetMode.Team, scenarioJson = JsonUtility.ToJson(cfg), difficulty = 2, players = roster.players, spawnPoints = new List<Vector2> { new Vector2(10f, 20f) } };
            var p2 = JsonUtility.FromJson<StartPacket>(JsonUtility.ToJson(packet));
            var cfg2 = JsonUtility.FromJson<ScenarioConfig>(p2.scenarioJson);
            Check("start packet: mode, scenario, difficulty, seats and spawn points arrive intact",
                p2.mode == NetMode.Team && cfg2.seed == 4242 && cfg2.hostileCount == 5 && cfg2.roeFlipTo == RoeCard.CardA && cfg2.prevalence == Prevalence.Low && p2.difficulty == 2 && p2.players.Count == 3 && p2.spawnPoints[0] == new Vector2(10f, 20f));
            Check("same config on two PCs gives the same counts after prevalence (Solo runs match)",
                JsonUtility.ToJson(cfg.Clone().Validate().ApplyPrevalence()) == JsonUtility.ToJson(cfg2.Clone().Validate().ApplyPrevalence()));

            var spawn = JsonUtility.FromJson<SpawnMsg>(JsonUtility.ToJson(new SpawnMsg { id = 7, truth = (int)ContactClass.HostileQuad, look = (int)ContactClass.FriendlyISR, seed = 99, deceptive = true, position = new Vector3(1, 2, 3) }));
            Check("spawn message keeps truth and look apart (a blend-in hostile looks like an own drone)", spawn.truth == (int)ContactClass.HostileQuad && spawn.look == (int)ContactClass.FriendlyISR && spawn.deceptive && spawn.seed == 99);
            var eff = JsonUtility.FromJson<EffectMsg>(JsonUtility.ToJson(new EffectMsg { id = 7, action = (int)PlayerAction.Engage, hit = true, result = "hit" }));
            Check("effect message: the host's hit comes back to the trainee", eff.hit && eff.action == (int)PlayerAction.Engage && eff.result == "hit");

            string summary = $"[NetSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
