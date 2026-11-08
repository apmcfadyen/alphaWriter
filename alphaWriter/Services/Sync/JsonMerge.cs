using System.Text.Json.Nodes;

namespace alphaWriter.Services.Sync
{
    /// <summary>
    /// Three-way merge of two edited copies of the same JSON document against the version
    /// they both started from (book.json edited on two devices):
    /// <list type="bullet">
    /// <item>a value changed on one side only takes that side's change;</item>
    /// <item>objects merge key by key;</item>
    /// <item>lists of objects with an "Id" (chapters, scenes, characters…) merge by id: items
    /// added on either side are kept, deletions apply unless the other side edited the item,
    /// and the order comes from whichever side reordered;</item>
    /// <item>lists of plain values (tag ids, dismissed notes) merge as sets;</item>
    /// <item>a value changed differently on both sides keeps this device's value and is reported.</item>
    /// </list>
    /// With no common version (two devices syncing for the first time) everything is treated
    /// as added on both sides.
    /// </summary>
    public static class JsonMerge
    {
        public static JsonNode? Merge(JsonNode? @base, JsonNode? local, JsonNode? remote, List<string> conflicts) =>
            MergeNode(@base, local, remote, "$", conflicts);

        private static bool Same(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

        private static JsonNode? Clone(JsonNode? n) => n?.DeepClone();

        private static JsonNode? MergeNode(JsonNode? b, JsonNode? l, JsonNode? r, string path, List<string> conflicts)
        {
            if (Same(l, r)) return Clone(l);
            if (b is not null && Same(l, b)) return Clone(r);
            if (b is not null && Same(r, b)) return Clone(l);

            if (l is JsonObject lo && r is JsonObject ro)
                return MergeObjects(b as JsonObject, lo, ro, path, conflicts);

            if (l is JsonArray la && r is JsonArray ra)
            {
                var ba = b as JsonArray;
                var all = la.Concat(ra).Concat(ba ?? []).ToList();
                if (all.Count > 0 && all.All(HasId))
                    return MergeById(ba, la, ra, path, conflicts);
                if (all.All(n => n is JsonValue))
                    return MergeAsSet(ba, la, ra);
            }

            conflicts.Add(path);
            return Clone(l);
        }

        private static JsonObject MergeObjects(JsonObject? b, JsonObject l, JsonObject r, string path, List<string> conflicts)
        {
            var result = new JsonObject();
            var keys = l.Select(p => p.Key).Concat(r.Select(p => p.Key).Where(k => !l.ContainsKey(k))).ToList();
            foreach (var key in keys)
            {
                JsonNode? bv = null;
                bool inBase = b is not null && b.TryGetPropertyValue(key, out bv);
                bool inLocal = l.TryGetPropertyValue(key, out var lv);
                bool inRemote = r.TryGetPropertyValue(key, out var rv);

                // A key one side doesn't have yet (e.g. a newer app version added it): take the other's
                if (!inLocal) { result[key] = Clone(rv); continue; }
                if (!inRemote) { result[key] = Clone(lv); continue; }
                result[key] = MergeNode(inBase ? bv : null, lv, rv, $"{path}.{key}", conflicts);
            }
            return result;
        }

        private static bool HasId(JsonNode? n) =>
            n is JsonObject o && o.TryGetPropertyValue("Id", out var id) && id is JsonValue v && v.TryGetValue<string>(out _);

        private static string IdOf(JsonNode n) => n["Id"]!.GetValue<string>();

        private static JsonArray MergeById(JsonArray? b, JsonArray l, JsonArray r, string path, List<string> conflicts)
        {
            var bMap = (b ?? []).ToDictionary(n => IdOf(n!), n => n);
            var lMap = l.ToDictionary(n => IdOf(n!), n => n);
            var rMap = r.ToDictionary(n => IdOf(n!), n => n);
            var lOrder = l.Select(n => IdOf(n!)).ToList();
            var rOrder = r.Select(n => IdOf(n!)).ToList();
            var bOrder = (b ?? []).Select(n => IdOf(n!)).ToList();

            bool Keep(string id)
            {
                bool inB = bMap.ContainsKey(id), inL = lMap.ContainsKey(id), inR = rMap.ContainsKey(id);
                if (inL && inR) return true;
                if (!inB) return true;                                // added on one side
                // Deleted on one side: honour it unless the other side changed the item
                return inL ? !Same(lMap[id], bMap[id]) : !Same(rMap[id], bMap[id]);
            }

            // Order from whichever side reordered (local first), then slot in the other side's additions
            var primary = !lOrder.Where(bMap.ContainsKey).SequenceEqual(bOrder.Where(lMap.ContainsKey)) ? lOrder : rOrder;
            var secondary = ReferenceEquals(primary, lOrder) ? rOrder : lOrder;
            var order = primary.Where(Keep).ToList();
            for (int i = 0; i < secondary.Count; i++)
            {
                var id = secondary[i];
                if (order.Contains(id) || !Keep(id)) continue;
                // Before the item that follows it on its own side, or at the end
                var after = secondary.Skip(i + 1).FirstOrDefault(order.Contains);
                order.Insert(after is null ? order.Count : order.IndexOf(after), id);
            }

            var result = new JsonArray();
            foreach (var id in order)
            {
                bMap.TryGetValue(id, out var bn);
                lMap.TryGetValue(id, out var ln);
                rMap.TryGetValue(id, out var rn);
                result.Add(ln is not null && rn is not null
                    ? MergeNode(bn, ln, rn, $"{path}[{id}]", conflicts)
                    : Clone(ln ?? rn));
            }
            return result;
        }

        private static JsonArray MergeAsSet(JsonArray? b, JsonArray l, JsonArray r)
        {
            static string Key(JsonNode? n) => n?.ToJsonString() ?? "null";
            var bSet = (b ?? []).Select(Key).ToHashSet();
            var lSet = l.Select(Key).ToHashSet();
            var rSet = r.Select(Key).ToHashSet();
            var result = new JsonArray();
            var seen = new HashSet<string>();
            foreach (var n in l.Concat(r))
            {
                var k = Key(n);
                bool keep = (lSet.Contains(k) && rSet.Contains(k)) || !bSet.Contains(k);
                if (keep && seen.Add(k)) result.Add(Clone(n));
            }
            return result;
        }
    }
}
