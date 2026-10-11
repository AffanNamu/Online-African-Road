using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

namespace NetCore.Tests
{
    public class OnboardingModelTests
    {
        static List<CountryOption> Countries() => new List<CountryOption>
        {
            new CountryOption { Code = "NG", Name = "Nigeria" }, new CountryOption { Code = "GH", Name = "Ghana" }, new CountryOption { Code = "CI", Name = "Côte d'Ivoire" },
            new CountryOption { Code = "CV", Name = "Cabo Verde" }, new CountryOption { Code = "ZA", Name = "South Africa" },
        };
        static List<LanguageOption> Languages() => new List<LanguageOption>
        {
            new LanguageOption { Code = "yo", Name = "Yoruba", NativeName = "Yorùbá" }, new LanguageOption { Code = "en", Name = "English", NativeName = "English", UiReady = true },
            new LanguageOption { Code = "fr", Name = "French", NativeName = "Français" }, new LanguageOption { Code = "ar", Name = "Arabic", NativeName = "Arabic" },
        };

        // ---------------------------------------------------------------- steps
        [Fact] public void ThereAreThreeStepsInOrder()
        {
            Assert.Equal(3, OnboardingRules.Steps);
            Assert.Equal(new[] { 1, 2, 3 }, new[] { OnboardingStep.Name, OnboardingStep.Where, OnboardingStep.Style }.Select(OnboardingRules.Number));
            Assert.Equal(OnboardingStep.Where, OnboardingRules.Next(OnboardingStep.Name)); Assert.Equal(OnboardingStep.Style, OnboardingRules.Next(OnboardingStep.Where));
            Assert.Equal(OnboardingStep.Style, OnboardingRules.Next(OnboardingStep.Style));
            Assert.Equal(OnboardingStep.Name, OnboardingRules.Back(OnboardingStep.Name)); Assert.Equal(OnboardingStep.Where, OnboardingRules.Back(OnboardingStep.Style));
            Assert.True(OnboardingRules.IsLast(OnboardingStep.Style)); Assert.False(OnboardingRules.IsLast(OnboardingStep.Where));
        }
        [Fact] public void EveryStepHasATitleAndText()
        {
            foreach (OnboardingStep s in System.Enum.GetValues(typeof(OnboardingStep))) { Assert.False(string.IsNullOrWhiteSpace(OnboardingRules.Title(s))); Assert.False(string.IsNullOrWhiteSpace(OnboardingRules.Subtitle(s))); }
            Assert.Equal(3, new[] { OnboardingStep.Name, OnboardingStep.Where, OnboardingStep.Style }.Select(OnboardingRules.Title).Distinct().Count());
        }
        [Fact] public void ContinueNeedsTheStepsOwnAnswer()
        {
            Assert.False(OnboardingRules.CanContinue(OnboardingStep.Name, "", "NG", "en", PlayStyle.Jobs));
            Assert.True(OnboardingRules.CanContinue(OnboardingStep.Name, "Chidi", null, null, null));
            Assert.False(OnboardingRules.CanContinue(OnboardingStep.Where, "Chidi", null, "en", null)); Assert.False(OnboardingRules.CanContinue(OnboardingStep.Where, "Chidi", "NG", null, null));
            Assert.False(OnboardingRules.CanContinue(OnboardingStep.Where, "Chidi", "", "", null)); Assert.True(OnboardingRules.CanContinue(OnboardingStep.Where, "Chidi", "NG", "en", null));
            Assert.False(OnboardingRules.CanContinue(OnboardingStep.Style, "Chidi", "NG", "en", null)); Assert.True(OnboardingRules.CanContinue(OnboardingStep.Style, "Chidi", "NG", "en", PlayStyle.Explore));
        }
        [Fact] public void ABlockedStepSaysWhy()
        {
            Assert.Contains("3 characters", OnboardingRules.BlockedReason(OnboardingStep.Name, "ab", null, null, null));
            Assert.Contains("country", OnboardingRules.BlockedReason(OnboardingStep.Where, "x", null, "en", null)); Assert.Contains("language", OnboardingRules.BlockedReason(OnboardingStep.Where, "x", "NG", null, null));
            Assert.Contains("how you want", OnboardingRules.BlockedReason(OnboardingStep.Style, "x", "NG", "en", null));
            Assert.Null(OnboardingRules.BlockedReason(OnboardingStep.Style, "x", "NG", "en", PlayStyle.Jobs));
        }

        // ---------------------------------------------------------------- the name (mirrors the server rule)
        [Theory] [InlineData("Chidi", true)] [InlineData("abc", true)] [InlineData("Road_Boss-7", true)] [InlineData("Ama Mensah", true)] [InlineData("  Chidi  ", true)] [InlineData("Ama    Mensah", true)]
        [InlineData("ab", false)] [InlineData("", false)] [InlineData("   ", false)] [InlineData("bad;name", false)] [InlineData("Zoë Ünï", false)] [InlineData("a@b.com", false)] [InlineData("<script>", false)]
        public void DriverNameRule(string name, bool ok) => Assert.Equal(ok, OnboardingRules.NameProblem(name) == null);
        [Fact] public void NameLengthLimitsAreExact()
        {
            Assert.Null(OnboardingRules.NameProblem(new string('a', 24))); Assert.Contains("at most 24", OnboardingRules.NameProblem(new string('a', 25))); Assert.Null(OnboardingRules.NameProblem("abc")); Assert.Contains("at least 3", OnboardingRules.NameProblem("ab"));
        }
        [Fact] public void CleaningCollapsesSpaces() { Assert.Equal("Ama Mensah", OnboardingRules.CleanName("  Ama    Mensah ")); Assert.Equal("", OnboardingRules.CleanName(null)); }
        [Theory] [InlineData("chidi_7", true)] [InlineData("@chidi_7", true)] [InlineData("CHIDI", true)] [InlineData("ab", false)] [InlineData("has space", false)] [InlineData("dot.name", false)] [InlineData("", false)]
        public void UsernameRule(string u, bool ok) => Assert.Equal(ok, OnboardingRules.UsernameProblem(u) == null);
        [Fact] public void UsernameIsNormalised() { Assert.Equal("chidi_7", OnboardingRules.CleanUsername("  @Chidi_7 ")); Assert.Equal("", OnboardingRules.CleanUsername(null)); }

        // ---------------------------------------------------------------- countries and languages
        [Fact] public void CountriesAreAlphabeticalAndAccentsDoNotMatter()
        {
            Assert.Equal(new[] { "Cabo Verde", "Côte d'Ivoire", "Ghana", "Nigeria", "South Africa" }, OnboardingRules.FilterCountries(Countries(), "").Select(c => c.Name));
            Assert.Equal(new[] { "CI" }, OnboardingRules.FilterCountries(Countries(), "cote").Select(c => c.Code)); Assert.Equal(new[] { "CI" }, OnboardingRules.FilterCountries(Countries(), "CÔTE").Select(c => c.Code));
            Assert.Equal(new[] { "ZA" }, OnboardingRules.FilterCountries(Countries(), "africa").Select(c => c.Code)); Assert.Equal(new[] { "NG" }, OnboardingRules.FilterCountries(Countries(), "ng").Select(c => c.Code));
            Assert.Empty(OnboardingRules.FilterCountries(Countries(), "zzz")); Assert.Empty(OnboardingRules.FilterCountries(null, "x"));
        }
        [Fact] public void ACodeMatchesOnlyWhenTypedInFullAndNamesMatchByPart()
        {
            Assert.Equal(new[] { "GH" }, OnboardingRules.FilterCountries(Countries(), "gh").Select(c => c.Code));            // "gh" is Ghana's code; no other name contains it
            Assert.Equal(new[] { "GH", "NG" }, OnboardingRules.FilterCountries(Countries(), "g").Select(c => c.Code));        // "g" is only part of the names Ghana and Nigeria (and no code is exactly "g")
        }
        [Fact] public void EnglishIsFirstThenAlphabetical() => Assert.Equal(new[] { "en", "ar", "fr", "yo" }, OnboardingRules.OrderLanguages(Languages()).Select(l => l.Code));
        [Fact] public void LanguageLabelsAndNotesAreHonest()
        {
            var l = Languages().ToDictionary(x => x.Code);
            Assert.Equal("English", OnboardingRules.LanguageLabel(l["en"])); Assert.Equal("French  (Français)", OnboardingRules.LanguageLabel(l["fr"])); Assert.Equal("Arabic", OnboardingRules.LanguageLabel(l["ar"]));
            Assert.Equal("Game text available", OnboardingRules.LanguageTag(l["en"])); Assert.Equal("Saved to your profile", OnboardingRules.LanguageTag(l["fr"]));
            Assert.Equal("The game will be shown in English.", OnboardingRules.LanguageNote(l["en"])); Assert.Contains("English for now", OnboardingRules.LanguageNote(l["yo"])); Assert.Contains("Yoruba", OnboardingRules.LanguageNote(l["yo"]));
            Assert.Equal("", OnboardingRules.LanguageNote(null));
        }

        // ---------------------------------------------------------------- suggestions from the browser's locale
        [Theory] [InlineData("en-NG", "en", "NG")] [InlineData("fr_SN", "fr", "SN")] [InlineData("pt", "pt", null)] [InlineData("en-US", "en", "US")] [InlineData("zh-Hans-CN", "zh", "CN")] [InlineData("", null, null)] [InlineData(null, null, null)] [InlineData("123", null, null)] [InlineData("e", null, null)]
        public void LocaleTagsAreParsed(string tag, string lang, string cc) { OnboardingRules.ParseLocale(tag, out var l, out var c); Assert.Equal(lang, l); Assert.Equal(cc, c); }
        [Fact] public void OnlyWhatTheServerOffersIsSuggested()
        {
            OnboardingRules.Suggest("en-NG", Countries(), Languages(), out var c, out var l); Assert.Equal("NG", c); Assert.Equal("en", l);
            OnboardingRules.Suggest("en-US", Countries(), Languages(), out c, out l); Assert.Null(c); Assert.Equal("en", l);        // no US in the list: no guess
            OnboardingRules.Suggest("de-DE", Countries(), Languages(), out c, out l); Assert.Null(c); Assert.Null(l);
            OnboardingRules.Suggest("", Countries(), Languages(), out c, out l); Assert.Null(c); Assert.Null(l);
            OnboardingRules.Suggest("fr-CI", null, null, out c, out l); Assert.Null(c); Assert.Null(l);
        }

        // ---------------------------------------------------------------- play styles
        [Fact] public void StylesMapToServerKeysAndBack()
        {
            Assert.Equal(new[] { "jobs", "bus", "convoy", "explore" }, OnboardingRules.AllStyles.Select(OnboardingRules.StyleKey));
            foreach (var s in OnboardingRules.AllStyles) Assert.Equal(s, OnboardingRules.StyleFromKey(OnboardingRules.StyleKey(s)));
            Assert.Null(OnboardingRules.StyleFromKey("racing")); Assert.Null(OnboardingRules.StyleFromKey(null)); Assert.Null(OnboardingRules.StyleFromKey(""));
        }
        [Fact] public void EveryStyleHasWordsAnIconAndAFirstScreen()
        {
            foreach (var s in OnboardingRules.AllStyles) { Assert.False(string.IsNullOrWhiteSpace(OnboardingRules.StyleTitle(s))); Assert.False(string.IsNullOrWhiteSpace(OnboardingRules.StyleText(s))); Assert.StartsWith("ic_", OnboardingRules.StyleIcon(s)); }
            Assert.Equal(4, OnboardingRules.AllStyles.Select(OnboardingRules.StyleTitle).Distinct().Count());
            Assert.Equal(new[] { "jobs", "vehicles", "multiplayer", "home" }, OnboardingRules.AllStyles.Select(s => OnboardingRules.LandingPage(s)));
            Assert.Equal("home", OnboardingRules.LandingPage(null));
        }

        // ---------------------------------------------------------------- server errors become sentences
        [Theory] [InlineData("display_name_taken", "already taken")] [InlineData("username_taken", "already taken")] [InlineData("invalid_display_name", "3 to 24")] [InlineData("invalid_username", "lower-case")]
        [InlineData("unknown_country", "country")] [InlineData("unknown_language", "language")] [InlineData("invalid_play_style", "how you want")] [InlineData("rate_limited", "Too many")] [InlineData("not_authenticated", "sign in")]
        public void ErrorsAreWords(string code, string contains) => Assert.Contains(contains, OnboardingRules.ErrorText(code));
        [Fact] public void AnUnknownErrorUsesTheFallback() { Assert.Equal("boom", OnboardingRules.ErrorText("weird", "boom")); Assert.Contains("try again", OnboardingRules.ErrorText("weird"), System.StringComparison.OrdinalIgnoreCase); }
    }

    public class ProfileRulesTests
    {
        [Fact] public void LevelProgressComesFromTheServersWindow()
        {
            Assert.Equal(0.5f, ProfileRules.LevelProgress(150, 100, 200), 3); Assert.Equal(0f, ProfileRules.LevelProgress(100, 100, 200)); Assert.Equal(1f, ProfileRules.LevelProgress(200, 100, 200));
            Assert.Equal(0f, ProfileRules.LevelProgress(50, 100, 200)); Assert.Equal(1f, ProfileRules.LevelProgress(500, 100, 200)); Assert.Equal(0f, ProfileRules.LevelProgress(5, 100, 100)); Assert.Equal(0f, ProfileRules.LevelProgress(5, 100, 50));
            Assert.Equal(50, ProfileRules.XpIntoLevel(150, 100)); Assert.Equal(0, ProfileRules.XpIntoLevel(50, 100)); Assert.Equal(50, ProfileRules.XpToNext(150, 200)); Assert.Equal(0, ProfileRules.XpToNext(250, 200));
        }
        [Fact] public void TextsAreFormattedTheSameEverywhere()
        {
            Assert.Equal("Member since October 2026", ProfileRules.MemberSince("2026-10-11T14:55:29.963352+00:00")); Assert.Equal("", ProfileRules.MemberSince("")); Assert.Equal("", ProfileRules.MemberSince("nonsense"));
            Assert.Equal("12.5 km", ProfileRules.Distance(12.5)); Assert.Equal("1,234 km", ProfileRules.Distance(1234.4)); Assert.Equal("0.0 km", ProfileRules.Distance(0));
            Assert.Equal("₦ 48,750", ProfileRules.Money(48750)); Assert.Equal("₦ 0", ProfileRules.Money(0)); Assert.Equal("1,200", ProfileRules.Count(1200));
            Assert.Equal("", ProfileRules.JobsRate(0, 0)); Assert.Equal("75% completed", ProfileRules.JobsRate(3, 4)); Assert.Equal("100% completed", ProfileRules.JobsRate(5, 4));
            Assert.Equal("No friends yet", ProfileRules.FriendsText(0)); Assert.Equal("1 friend", ProfileRules.FriendsText(1)); Assert.Equal("7 friends", ProfileRules.FriendsText(7));
            Assert.Equal("Unranked", ProfileRules.RankText(0)); Assert.Equal("#14", ProfileRules.RankText(14)); Assert.Equal("#1,204", ProfileRules.RankText(1204));
            Assert.Equal("CA", ProfileRules.Initials("Chidi Ade"));
        }
    }
}
