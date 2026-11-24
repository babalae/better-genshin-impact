using BetterGenshinImpact.GameTask.I18n;
using Newtonsoft.Json;
using System.Reflection;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.I18n
{
    public class GameTextServiceTests
    {
        private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "GameTask", "I18n");

        /// <summary>
        /// 已知缺项基线：改造前 resx 中就缺失的条目。补上译文后请同步从这里删除；新增缺项会导致测试失败。
        /// </summary>
        private static readonly Dictionary<string, string[]> KnownMissingKeys = new()
        {
            ["zh-Hant"] =
            [
                "Domain.MatchingChallenge",
                "Domain.QuickSetup",
                "Domain.LimitedTimeFullyOpen",
                "Domain.LimitedTimeOpen",
                "SereniteaPot.Ayuan",
                "SereniteaPot.Tubby",
                "SereniteaPot.TubbyTitle",
                "SereniteaPot.Trust",
                "SereniteaPot.RealmDepot",
            ],
            ["en"] =
            [
                "Domain.MatchingChallenge",
                "Domain.QuickSetup",
                "Domain.LimitedTimeFullyOpen",
                "Domain.LimitedTimeOpen",
                "SereniteaPot.Ayuan",
                "SereniteaPot.Tubby",
                "SereniteaPot.TubbyTitle",
                "SereniteaPot.Trust",
                "SereniteaPot.RealmDepot",
            ],
            ["fr"] =
            [
                "Domain.MatchingChallenge",
                "Domain.QuickSetup",
                "Domain.LimitedTimeFullyOpen",
                "Domain.LimitedTimeOpen",
                "SereniteaPot.Ayuan",
                "SereniteaPot.Tubby",
                "SereniteaPot.TubbyTitle",
                "SereniteaPot.Trust",
                "SereniteaPot.RealmDepot",
            ],
            ["it"] =
            [
                "Common.Use",
                "Common.Cancel",
                "Resin.Original",
                "Resin.Condensed",
                "Resin.Transient",
                "Resin.Fragile",
                "Resin.ReplenishOriginal",
                "Resin.Insufficient",
                "Domain.MatchingChallenge",
                "Domain.SinglePlayerChallenge",
                "Domain.StartChallenge",
                "Domain.RetryPrompt",
                "Domain.PetrifiedTree",
                "Map.Area.Snezhnaya",
                "Rewards.DailyCommissionRewards",
                "SereniteaPot.Ayuan",
                "SereniteaPot.Tubby",
                "SereniteaPot.TubbyTitle",
                "SereniteaPot.Trust",
                "SereniteaPot.RealmDepot",
            ],
        };

        private static GameTextService CreateSut() => new(Directory, new FakeLogger());

        private static IReadOnlyList<string> AllKeys()
        {
            var keys = new List<string>();
            Collect(typeof(GameTextKeys), keys);
            return keys;

            static void Collect(Type type, List<string> keys)
            {
                keys.AddRange(type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                    .Select(f => (string)f.GetRawConstantValue()!));
                foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
                {
                    Collect(nested, keys);
                }
            }
        }

        [Fact]
        public void GameTextKeys_ShouldBeUnique()
        {
            var keys = AllKeys();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        [Fact]
        public void EveryKey_ShouldExistInBaseLanguage()
        {
            var sut = CreateSut();
            var missing = AllKeys().Where(k => !sut.TryGet(k, GameTextService.BaseLanguage, out _)).ToList();
            Assert.Empty(missing);
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void JsonFile_ShouldOnlyContainRegisteredNonEmptyKeys(string language)
        {
            var json = File.ReadAllText(Path.Combine(Directory, $"{language}.json"));
            var table = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            Assert.NotNull(table);
            Assert.Empty(table.Keys.Except(AllKeys()));
            Assert.DoesNotContain(table.Values, string.IsNullOrEmpty);
        }

        [Theory]
        [MemberData(nameof(Languages))]
        public void MissingKeys_ShouldMatchKnownBaseline(string language)
        {
            var sut = CreateSut();
            var missing = AllKeys().Where(k => !sut.TryGet(k, language, out _)).Order().ToArray();
            var expected = KnownMissingKeys.GetValueOrDefault(language, []).Order().ToArray();
            Assert.Equal(expected, missing);
        }

        public static IEnumerable<object[]> Languages() => GameTextService.SupportedLanguages.Select(l => new object[] { l });

        [Theory]
        [InlineData("zh-Hans", "(挑战|达成)")]
        [InlineData("zh-Hant", "(挑戰|達成)")]
        [InlineData("en", "(Challenge|Completed)")]
        public void Get_ShouldReturnLanguageValue(string language, string expected)
        {
            Assert.Equal(expected, CreateSut().Get(GameTextKeys.Domain.ChallengeCompleted, language));
        }

        [Fact]
        public void Get_MissingInLanguage_ShouldFallbackToBaseLanguage()
        {
            Assert.Equal("快速编队", CreateSut().Get(GameTextKeys.Domain.QuickSetup, "en"));
        }

        [Theory]
        [InlineData("ja")]
        [InlineData("ru")]
        [InlineData(null)]
        public void Get_UnsupportedLanguage_ShouldUseBaseLanguage(string? language)
        {
            Assert.Equal("复苏", CreateSut().Get(GameTextKeys.Common.Revive, language));
        }

        [Fact]
        public void Get_UnregisteredKey_ShouldReturnKey()
        {
            Assert.Equal("Not.Registered", CreateSut().Get("Not.Registered", "en"));
        }

        [Fact]
        public void Get_MissingDirectory_ShouldNotThrow()
        {
            var sut = new GameTextService(Path.Combine(Directory, "not-exists"), new FakeLogger());
            Assert.Equal(GameTextKeys.Common.Revive, sut.Get(GameTextKeys.Common.Revive, "en"));
        }
    }
}
