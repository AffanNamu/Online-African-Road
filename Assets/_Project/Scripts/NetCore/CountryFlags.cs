using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>What can be drawn for a stored country value.</summary>
    public enum FlagState
    {
        /// <summary>No country stored (an account from before onboarding, or one that never chose): a neutral placeholder, never a guess.</summary>
        NotSet,
        /// <summary>A value that is not an ISO-3166-1 alpha-2 code (two letters A-Z): drawn as the neutral placeholder.</summary>
        Invalid,
        /// <summary>A well-formed code the game ships no flag for: the neutral placeholder, with the code as text.</summary>
        NoArtwork,
        /// <summary>A flag sprite exists at Resources/Flags/{AssetName}.</summary>
        Known
    }

    /// <summary>
    /// The one place that turns the stored country (profiles.country_code / cities.country_code, ISO-3166-1 alpha-2, the key of the server's `countries` table)
    /// into a flag asset name. Every screen draws flags through this (Ui.Flag), so no screen has its own mapping.
    /// The flags are Resources/Flags/{code in lower case}.png (flag-icons, MIT; see Resources/Flags/SOURCE.md). `Codes` is the list that ships:
    /// every African state plus Western Sahara. tools/gen_flags.py --check and the tests keep this list and the files identical.
    /// Nothing here reads a player's name, address or location: only the stored country code.
    /// </summary>
    public static class CountryFlags
    {
        public const string Codes = "AO BF BI BJ BW CD CF CG CI CM CV DJ DZ EG EH ER ET GA GH GM GN GQ GW KE KM LR LS LY MA MG ML MR MU MW MZ NA NE NG RW SC SD SL SN SO SS ST SZ TD TG TN TZ UG ZA ZM ZW";

        static readonly HashSet<string> Shipped = new HashSet<string>(Codes.Split(' '), StringComparer.Ordinal);

        public static IReadOnlyCollection<string> All => Shipped;

        /// <summary>The upper-case ISO-2 code, or null when the value is empty or not exactly two letters A-Z. Lower case and surrounding spaces are accepted ("ng " -> "NG").</summary>
        public static string Normalize(string code)
        {
            if (code == null) return null;
            code = code.Trim(); if (code.Length != 2) return null;
            char a = char.ToUpperInvariant(code[0]), b = char.ToUpperInvariant(code[1]);
            if (a < 'A' || a > 'Z' || b < 'A' || b > 'Z') return null;
            return new string(new[] { a, b });
        }

        public static FlagState StateOf(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return FlagState.NotSet;
            var n = Normalize(code); if (n == null) return FlagState.Invalid;
            return Shipped.Contains(n) ? FlagState.Known : FlagState.NoArtwork;
        }

        public static bool HasFlag(string code) => StateOf(code) == FlagState.Known;

        /// <summary>The file name (no extension) of the flag, e.g. "ng"; null when there is none.</summary>
        public static string AssetName(string code) => HasFlag(code) ? Normalize(code).ToLowerInvariant() : null;

        /// <summary>The Resources path of the flag, e.g. "Flags/ng"; null when there is none.</summary>
        public static string ResourcePath(string code) { var a = AssetName(code); return a == null ? null : "Flags/" + a; }

        /// <summary>The text shown beside (or instead of) a flag: the clean upper-case code, or "" when none is stored. It never invents a country.</summary>
        public static string CodeText(string code) => Normalize(code) ?? "";

        /// <summary>How many of `codes` draw a real flag, how many have a well-formed code but no artwork, and how many have no usable country; plus the flag files drawn, in order.</summary>
        public sealed class Tally { public int Known, NoArtwork, NotSet; public string Drawn = ""; }

        public static Tally Count(IEnumerable<string> codes)
        {
            var t = new Tally(); var drawn = new List<string>();
            foreach (var c in codes ?? new string[0])
                switch (StateOf(c))
                {
                    case FlagState.Known: t.Known++; drawn.Add(AssetName(c)); break;
                    case FlagState.NoArtwork: t.NoArtwork++; break;
                    default: t.NotSet++; break;
                }
            t.Drawn = string.Join(",", drawn); return t;
        }

        /// <summary>The country as one phrase: the server's name when known, else the code, else "Country not set".</summary>
        public static string Describe(string code, string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
            var n = Normalize(code); return n ?? "Country not set";
        }
    }
}
