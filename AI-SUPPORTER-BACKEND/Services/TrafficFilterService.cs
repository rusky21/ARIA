using System.Text.RegularExpressions;

namespace AI_Supporter;

/// <summary>
/// Серверный фильтр пакетов. Парсит выражения: "proto == artnet", "port == 6454", "src == 192.168.1.50".
/// Логика перенесена с фронта (Network.tsx matchPart/matchesFilter).
/// </summary>
public static partial class TrafficFilterService
{
    [GeneratedRegex(@"^(\w+)\s*(==|=|!=|>=|<=|>|<)\s*(\S+)$")]
    private static partial Regex FieldExpr();

    public static IReadOnlyList<TrafficRecord> Filter(IReadOnlyList<TrafficRecord> records, string filter)
    {
        var q = filter.Trim();
        if (string.IsNullOrEmpty(q)) return records;

        return records.Where(r => MatchesFilter(r, q)).ToList();
    }

    private static bool MatchesFilter(TrafficRecord pk, string filter)
    {
        var groups = filter.Split("||", StringSplitOptions.RemoveEmptyEntries);
        foreach (var group in groups)
        {
            var parts = group.Split("&&", StringSplitOptions.RemoveEmptyEntries);
            if (parts.All(p => MatchPart(pk, p.Trim())))
                return true;
        }
        return false;
    }

    private static bool MatchPart(TrafficRecord pk, string raw)
    {
        var p = raw.Trim();
        if (string.IsNullOrEmpty(p)) return true;
        if (p.StartsWith('!'))
            return !MatchPart(pk, p[1..]);

        var m = FieldExpr().Match(p);
        if (m.Success)
        {
            var field = m.Groups[1].Value.ToLowerInvariant();
            var op = m.Groups[2].Value;
            var val = m.Groups[3].Value.Trim('"').ToLowerInvariant();
            var num = int.TryParse(val, out var numVal) ? numVal : 0;

            return field switch
            {
                "proto" => CmpStr(pk.Protocol, op, val),
                "src" => CmpStr(pk.SrcIp, op, val),
                "dst" => CmpStr(pk.DstIp, op, val),
                "ip" => CmpStr(pk.SrcIp, op, val) || CmpStr(pk.DstIp, op, val),
                "sport" => CmpNum(pk.SrcPort, op, num),
                "dport" => CmpNum(pk.DstPort, op, num),
                "port" => op == "!="
                    ? pk.SrcPort != num && pk.DstPort != num
                    : CmpNum(pk.SrcPort, op, num) || CmpNum(pk.DstPort, op, num),
                "len" => CmpNum(pk.Length, op, num),
                "ttl" => CmpNum(pk.Ttl, op, num),
                "info" => CmpStr(pk.Summary ?? "", op, val),
                _ => false,
            };
        }

        // Fallback: substring search across all fields
        var s = p.ToLowerInvariant();
        var haystack = $"{pk.Protocol} {pk.SrcIp} {pk.DstIp} {pk.Summary} {pk.SrcPort} {pk.DstPort}".ToLowerInvariant();
        return haystack.Contains(s);
    }

    private static bool CmpStr(string a, string op, string b)
    {
        var x = a.ToLowerInvariant();
        return op switch
        {
            "==" or "=" => x == b,
            "!=" => x != b,
            ">" => string.Compare(x, b, StringComparison.Ordinal) > 0,
            "<" => string.Compare(x, b, StringComparison.Ordinal) < 0,
            ">=" => string.Compare(x, b, StringComparison.Ordinal) >= 0,
            "<=" => string.Compare(x, b, StringComparison.Ordinal) <= 0,
            _ => false,
        };
    }

    private static bool CmpNum(int a, string op, int b)
    {
        return op switch
        {
            "==" or "=" => a == b,
            "!=" => a != b,
            ">" => a > b,
            "<" => a < b,
            ">=" => a >= b,
            "<=" => a <= b,
            _ => false,
        };
    }
}
