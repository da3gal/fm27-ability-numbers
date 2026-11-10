// Pure logic with no game/Unity dependencies, so it can be unit tested outside FM27.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace FM27AbilityNumbers
{
    public enum AbilityKind { None, Current, Potential }

    public static class Logic
    {
        // FM27 binds every player CA/PA star widget to a StarRange whose binding path names the ability, e.g.
        // "Person.CurrentAbilityStarRange", "report.ScoutedPotentialAbilityInfo.StarRange",
        // "binding.coachpotentialabilitystarrange". Role ratings use "role.CurrentAbilityStars" (the player's
        // ability in that role, not his CA), so only "...AbilityStarRange" and "...AbilityInfo" count.
        static readonly Regex s_abilityNode = new Regex(@"(current|potential)ability(starrange|info)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static AbilityKind Classify(IEnumerable<string> nodeNamesLeafFirst)
        {
            foreach (var name in nodeNamesLeafFirst)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var m = s_abilityNode.Match(name);
                if (m.Success)
                    return m.Groups[1].Value.Equals("current", StringComparison.OrdinalIgnoreCase) ? AbilityKind.Current : AbilityKind.Potential;
            }
            return AbilityKind.None;
        }

        // A player's ability -> label text, or null to hide the label. FM abilities are 1..200; a negative PA is a
        // database range code and is shown as-is.
        public static string FormatAbility(int v) =>
            v is >= -200 and <= 200 && v != 0 ? v.ToString(CultureInfo.InvariantCulture) : null;

        // The dedicated header/popup text, e.g. "CA 158 · PA 170"; null when there is nothing to show.
        public static string FormatPair(int ca, int pa, bool showCa, bool showPa)
        {
            string c = showCa ? FormatAbility(ca) : null, p = showPa ? FormatAbility(pa) : null;
            if (c != null && p != null) return $"CA {c} · PA {p}";
            if (c != null) return $"CA {c}";
            return p != null ? $"PA {p}" : null;
        }

        static readonly Regex s_sortPath = new Regex(
            @"^(?<prefix>(?:[^.]+\.)*)(?:coach|scout)?(?<kind>current|potential)abilitystarrange\.(?:max|min)value$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Table columns sort CA/PA stars by "<prefix>CurrentAbilityStarRange.MaxValue" (half-star buckets).
        // Rewrites that to the exact property so clicking the column header sorts by the real number.
        public static bool TryRewriteSortPath(string path, string caProperty, string paProperty, out string rewritten)
        {
            rewritten = null;
            if (string.IsNullOrEmpty(path)) return false;
            var m = s_sortPath.Match(path);
            if (!m.Success) return false;
            var prop = m.Groups["kind"].Value.Equals("current", StringComparison.OrdinalIgnoreCase) ? caProperty : paProperty;
            if (string.IsNullOrWhiteSpace(prop)) return false;
            rewritten = m.Groups["prefix"].Value + prop.Trim();
            return true;
        }

        // The value to answer an exact-sort channel with, or null to send nothing. FM sorts a table only once every
        // row has answered, so a player we cannot read gets 0 once the index exists; one we read before keeps that.
        public static int? ChannelAnswer(bool read, int value, bool indexBuilt, int? last)
        {
            if (!read)
            {
                if (last != null || !indexBuilt) return null;
                value = 0;
            }
            return value == last ? null : value;
        }

        public static string[] SplitClasses(string classes) =>
            (classes ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
