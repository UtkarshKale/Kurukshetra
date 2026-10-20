using System.Collections.Generic;
using Kurukshetra.Net;
using Kurukshetra.Scenario;
using Kurukshetra.Scoring;

namespace Kurukshetra.Trainer
{
    // The score dashboard's player list in a LAN session (on the host): this PC's own seat first, then every trainee with
    // the latest result their PC sent (live every 5 s, final at the end of the run).
    public class NetPlayerSource : IPlayerSource
    {
        readonly LocalPlayerSource local;
        readonly List<PlayerEntry> entries = new List<PlayerEntry>();

        public NetPlayerSource(ScoreKeeper keeper, ScenarioSpawner spawner)
        {
            local = new LocalPlayerSource(keeper, spawner);
        }

        public int Count => Refresh();

        public PlayerEntry Get(int index)
        {
            Refresh();
            return index >= 0 && index < entries.Count ? entries[index] : null;
        }

        int Refresh()
        {
            entries.Clear();
            var me = local.Get(0);
            me.name = ScoreKeeper.PlayerName + " (host)";
            entries.Add(me);
            var lan = LanSession.Instance;
            if (lan == null) return entries.Count;
            bool team = lan.roster.mode == NetMode.Team;
            var host = lan.roster.players.Find(p => p.host);
            if (team && host != null) me.role = NetPlayer.RoleName(host.Role);
            foreach (var p in lan.roster.players)
            {
                if (p.host) continue;
                lan.Results.TryGetValue(p.clientId, out var s);
                entries.Add(new PlayerEntry { name = p.name, state = string.IsNullOrEmpty(p.state) ? "waiting" : p.state, current = s, role = team ? NetPlayer.RoleName(p.Role) : "", seatId = p.clientId.ToString(), unit = string.IsNullOrEmpty(p.unit) ? "Unit 1" : p.unit });
            }
            return entries.Count;
        }
    }
}
