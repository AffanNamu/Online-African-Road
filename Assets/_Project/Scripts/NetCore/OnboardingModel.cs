using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ARO.NetCore
{
    // ===================================================================================================== First-run onboarding + the player's own profile: pure rules (no Unity types, unit tested)
    public enum PlayStyle { Jobs, Bus, Convoy, Explore }
    public enum OnboardingStep { Name, Where, Style }

    public sealed class CountryOption { public string Code = "", Name = ""; }
    public sealed class LanguageOption { public string Code = "", Name = "", NativeName = ""; public bool UiReady; }

    public static class OnboardingRules
    {
        public const int Steps = 3;
        public static readonly PlayStyle[] AllStyles = { PlayStyle.Jobs, PlayStyle.Bus, PlayStyle.Convoy, PlayStyle.Explore };

        // ---------------------------------------------------------------- steps
        public static int Number(OnboardingStep s) => (int)s + 1;
        public static OnboardingStep Next(OnboardingStep s) => s == OnboardingStep.Style ? s : (OnboardingStep)((int)s + 1);
        public static OnboardingStep Back(OnboardingStep s) => s == OnboardingStep.Name ? s : (OnboardingStep)((int)s - 1);
        public static bool IsLast(OnboardingStep s) => s == OnboardingStep.Style;
        public static string Title(OnboardingStep s) => s == OnboardingStep.Name ? "What should we call you, driver?" : s == OnboardingStep.Where ? "Where are you driving from?" : "How do you want to drive?";
        public static string Subtitle(OnboardingStep s) =>
            s == OnboardingStep.Name ? "This is the name other drivers see on the map, in convoys and on the leaderboards." :
            s == OnboardingStep.Where ? "Your country appears on your profile and the leaderboards. Pick the language you would like the game in." :
            "We will open the right screen first. You can change this any time on your profile.";

        /// <summary>Whether the player may leave the step: the name is valid, a country and a language are chosen, a play style is chosen.</summary>
        public static bool CanContinue(OnboardingStep s, string name, string country, string language, PlayStyle? style)
        {
            switch (s)
            {
                case OnboardingStep.Name: return NameProblem(name) == null;
                case OnboardingStep.Where: return !string.IsNullOrEmpty(country) && !string.IsNullOrEmpty(language);
                default: return style.HasValue;
            }
        }

        /// <summary>Why the Continue button is disabled (shown under the step), or null when it is not.</summary>
        public static string BlockedReason(OnboardingStep s, string name, string country, string language, PlayStyle? style)
        {
            if (CanContinue(s, name, country, language, style)) return null;
            switch (s)
            {
                case OnboardingStep.Name: return NameProblem(name);
                case OnboardingStep.Where: return string.IsNullOrEmpty(country) ? "Choose your country to continue." : "Choose a language to continue.";
                default: return "Choose how you want to start.";
            }
        }

        // ---------------------------------------------------------------- driver name (the same rule the server enforces: 3-24 letters, numbers, space, - and _)
        static readonly Regex NameShape = new Regex("^[A-Za-z0-9_ -]{3,24}$", RegexOptions.Compiled);
        public static string CleanName(string s) => Regex.Replace((s ?? "").Trim(), @"\s{2,}", " ");
        public static string NameProblem(string raw)
        {
            string s = CleanName(raw);
            if (s.Length < 3) return "Your driver name needs at least 3 characters.";
            if (s.Length > 24) return "Your driver name can have at most 24 characters.";
            if (!NameShape.IsMatch(s)) return "Use letters, numbers, spaces, - and _ only.";
            return null;
        }
        static readonly Regex UsernameShape = new Regex("^[a-z0-9_]{3,24}$", RegexOptions.Compiled);
        public static string UsernameProblem(string raw)
        {
            string s = (raw ?? "").Trim().TrimStart('@').ToLowerInvariant();
            if (s.Length < 3) return "Your username needs at least 3 characters.";
            if (s.Length > 24) return "Your username can have at most 24 characters.";
            if (!UsernameShape.IsMatch(s)) return "Use lower-case letters, numbers and _ only.";
            return null;
        }
        public static string CleanUsername(string raw) => (raw ?? "").Trim().TrimStart('@').ToLowerInvariant();

        // ---------------------------------------------------------------- lists from the server
        /// <summary>Countries narrowed by the search text (name or code contains it, any case, accents ignored), alphabetical. A blank query lists all.</summary>
        public static List<CountryOption> FilterCountries(IEnumerable<CountryOption> all, string query)
        {
            string q = Fold(query); var src = (all ?? new CountryOption[0]).Where(c => c != null && !string.IsNullOrEmpty(c.Code));
            if (q.Length > 0) src = src.Where(c => Fold(c.Name).Contains(q) || Fold(c.Code) == q);
            return src.OrderBy(c => Fold(c.Name), StringComparer.Ordinal).ToList();
        }

        /// <summary>Lower case without accents, so "cote" finds "Cote d'Ivoire" and "Cabo" finds "Cabo Verde".</summary>
        public static string Fold(string s)
        {
            var d = (s ?? "").Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD); var b = new System.Text.StringBuilder();
            foreach (char ch in d) if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark) b.Append(ch);
            return b.ToString();
        }

        /// <summary>English first (the only translated language), then the rest alphabetically.</summary>
        public static List<LanguageOption> OrderLanguages(IEnumerable<LanguageOption> all) =>
            (all ?? new LanguageOption[0]).Where(l => l != null && !string.IsNullOrEmpty(l.Code)).OrderByDescending(l => l.UiReady).ThenBy(l => Fold(l.Name), StringComparer.Ordinal).ToList();

        public static string LanguageLabel(LanguageOption l) => string.IsNullOrEmpty(l.NativeName) || l.NativeName == l.Name ? l.Name : l.Name + "  (" + l.NativeName + ")";
        public static string LanguageTag(LanguageOption l) => l.UiReady ? "Game text available" : "Saved to your profile";
        /// <summary>What the language choice really does, said plainly under the list.</summary>
        public static string LanguageNote(LanguageOption l) => l == null ? "" : l.UiReady ? "The game will be shown in " + l.Name + "." : "The game is in English for now. We will remember " + l.Name + " for when it is translated.";

        // ---------------------------------------------------------------- suggesting a country and a language from the browser's locale ("en-NG", "fr-SN", "pt")
        /// <summary>Splits a locale tag into (language, country). Either part is null when absent or malformed.</summary>
        public static void ParseLocale(string tag, out string language, out string country)
        {
            language = country = null; var s = (tag ?? "").Trim().Replace('_', '-'); if (s.Length == 0) return;
            var parts = s.Split('-'); if (parts[0].Length >= 2 && parts[0].Length <= 3 && parts[0].All(char.IsLetter)) language = parts[0].ToLowerInvariant();
            foreach (var p in parts.Skip(1)) if (p.Length == 2 && p.All(char.IsLetter)) { country = p.ToUpperInvariant(); break; }
        }

        /// <summary>Only what the server actually offers is ever suggested; a suggestion is a starting point the player can change.</summary>
        public static void Suggest(string localeTag, IEnumerable<CountryOption> countries, IEnumerable<LanguageOption> languages, out string countryCode, out string languageCode)
        {
            ParseLocale(localeTag, out var lang, out var cc); countryCode = languageCode = null;
            if (cc != null && (countries ?? new CountryOption[0]).Any(c => c != null && c.Code == cc)) countryCode = cc;
            if (lang != null && (languages ?? new LanguageOption[0]).Any(l => l != null && l.Code == lang)) languageCode = lang;
        }

        // ---------------------------------------------------------------- how the player wants to start
        public static string StyleKey(PlayStyle s) => s == PlayStyle.Bus ? "bus" : s == PlayStyle.Convoy ? "convoy" : s == PlayStyle.Explore ? "explore" : "jobs";
        public static PlayStyle? StyleFromKey(string key) { switch (key) { case "jobs": return PlayStyle.Jobs; case "bus": return PlayStyle.Bus; case "convoy": return PlayStyle.Convoy; case "explore": return PlayStyle.Explore; default: return null; } }
        public static string StyleTitle(PlayStyle s) => s == PlayStyle.Bus ? "Run bus routes" : s == PlayStyle.Convoy ? "Drive with friends" : s == PlayStyle.Explore ? "Explore Africa" : "Deliver freight";
        public static string StyleText(PlayStyle s) =>
            s == PlayStyle.Bus ? "Carry passengers between stops on scheduled routes and earn fares." :
            s == PlayStyle.Convoy ? "Create or join a convoy, drive a route together and chat on the way." :
            s == PlayStyle.Explore ? "Look around the map, check the open routes and drive wherever you like." :
            "Take cargo jobs for good pay and build your reputation and your fleet.";
        public static string StyleIcon(PlayStyle s) => s == PlayStyle.Bus ? "ic_bus" : s == PlayStyle.Convoy ? "ic_people" : s == PlayStyle.Explore ? "ic_map" : "ic_jobs";
        /// <summary>The first screen after onboarding: "jobs", "vehicles", "multiplayer" or "home".</summary>
        public static string LandingPage(PlayStyle? s) => s == PlayStyle.Jobs ? "jobs" : s == PlayStyle.Bus ? "vehicles" : s == PlayStyle.Convoy ? "multiplayer" : "home";

        // ---------------------------------------------------------------- what the server said
        public static string ErrorText(string code, string fallback = null)
        {
            switch (code)
            {
                case "display_name_taken": return "That driver name is already taken. Try another one.";
                case "username_taken": return "That username is already taken. Try another one.";
                case "invalid_display_name": return "Use 3 to 24 letters, numbers, spaces, - or _ for your driver name.";
                case "invalid_username": return "Use 3 to 24 lower-case letters, numbers or _ for your username.";
                case "unknown_country": return "That country is not available. Choose one from the list.";
                case "unknown_language": return "That language is not available. Choose one from the list.";
                case "invalid_play_style": return "Choose how you want to start.";
                case "rate_limited": return "Too many tries. Wait a moment and try again.";
                case "not_authenticated": return "Your session ended. Please sign in again.";
                default: return fallback ?? "Something went wrong. Please try again.";
            }
        }
    }

    // ===================================================================================================== the player's own profile page
    public static class ProfileRules
    {
        /// <summary>How far through the current level the player is, 0..1, from the server's XP window.</summary>
        public static float LevelProgress(long xp, long levelStart, long nextLevel)
        {
            long span = nextLevel - levelStart; if (span <= 0) return 0f;
            return (float)Math.Max(0.0, Math.Min(1.0, (double)(xp - levelStart) / span));
        }
        public static long XpIntoLevel(long xp, long levelStart) => Math.Max(0, xp - levelStart);
        public static long XpToNext(long xp, long nextLevel) => Math.Max(0, nextLevel - xp);

        public static string MemberSince(string iso) => TimeText.TryParse(iso, out var t) ? "Member since " + t.UtcDateTime.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : "";
        public static string Distance(double km) => km.ToString(km >= 100 ? "N0" : "N1", System.Globalization.CultureInfo.InvariantCulture) + " km";
        public static string Money(long coins) => "₦ " + coins.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        public static string Count(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        /// <summary>Completion of jobs the player took, as a whole percent; empty when none were taken.</summary>
        public static string JobsRate(int completed, int total) => total <= 0 ? "" : Math.Min(100, (int)Math.Round(100.0 * completed / total)) + "% completed";
        public static string FriendsText(int n) => n == 0 ? "No friends yet" : n == 1 ? "1 friend" : n + " friends";
        public static string RankText(int rank) => rank <= 0 ? "Unranked" : "#" + rank.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        public static string Initials(string name) => FriendsRules.Initials(name);
    }
}
