using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    public sealed class RosterEntry
    {
        public string Id { get; }
        public string Name { get; set; }
        public long JoinOrder { get; }
        public RosterEntry(string id, string name, long order) { Id = id; Name = name; JoinOrder = order; }
    }

    /// <summary>Convoy/session player list. Leader = longest-standing member, mirroring the database rule in leave_convoy().</summary>
    public sealed class Roster
    {
        readonly Dictionary<string, RosterEntry> _byId = new Dictionary<string, RosterEntry>();
        long _seq;
        public event Action Changed;
        public int Capacity { get; }
        public Roster(int capacity = 8) { Capacity = capacity; }

        public IReadOnlyList<RosterEntry> Members => _byId.Values.OrderBy(e => e.JoinOrder).ToList();
        public int Count => _byId.Count;
        public RosterEntry Leader => _byId.Values.OrderBy(e => e.JoinOrder).FirstOrDefault();
        public bool Contains(string id) => _byId.ContainsKey(id);

        /// <summary>Returns false if full or already present (a re-join does not change join order).</summary>
        public bool Join(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (_byId.TryGetValue(id, out var e)) { if (e.Name != name) { e.Name = name; Changed?.Invoke(); } return false; }
            if (_byId.Count >= Capacity) return false;
            _byId[id] = new RosterEntry(id, string.IsNullOrWhiteSpace(name) ? "Driver" : name.Trim(), ++_seq);
            Changed?.Invoke(); return true;
        }

        public bool Leave(string id)
        {
            if (!_byId.Remove(id)) return false;
            Changed?.Invoke(); return true;
        }

        public void Clear() { if (_byId.Count == 0) return; _byId.Clear(); Changed?.Invoke(); }
    }
}
