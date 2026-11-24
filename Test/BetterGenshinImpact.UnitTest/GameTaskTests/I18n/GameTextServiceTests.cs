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
        /// 只包含内置语言；意大利语已迁移到 bettergi-plugins 仓库的 lang_it 插件。
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

        public static IEnumerable<object[]> Languages() => GameTextService.BuiltinLanguages.Select(l => new object[] { l.Code });

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

        [Theory]
        [InlineData("en-US", "(Challenge|Completed)")]
        [InlineData("zh-CN", "(挑战|达成)")]
        [InlineData("zh-TW", "(挑戰|達成)")]
        public void Get_RegionalLanguage_ShouldUseParentLanguage(string language, string expected)
        {
            Assert.Equal(expected, CreateSut().Get(GameTextKeys.Domain.ChallengeCompleted, language));
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

        [Fact]
        public void BuiltinLanguages_ShouldNotContainPluginLanguage()
        {
            // 意大利语由 lang_it 插件提供，未启用插件时不可选，按基准语言取值
            var sut = CreateSut();
            Assert.False(sut.IsAvailable("it"));
            Assert.Equal("复苏", sut.Get(GameTextKeys.Common.Revive, "it"));
        }

        [Fact]
        public void AddPluginLanguages_NewLanguage_ShouldBeAvailableAndFallbackToBaseLanguage()
        {
            var sut = CreateSut();
            var changed = 0;
            sut.LanguagesChanged += (_, _) => changed++;

            sut.AddPluginLanguages("lang_it", [Pack("it", "Italiano", (GameTextKeys.Common.Revive, "Rianima"))]);

            Assert.Equal(1, changed);
            Assert.True(sut.IsAvailable("it"));
            Assert.Contains(sut.Languages, l => l is { Code: "it", DisplayName: "Italiano" });
            Assert.Equal("Rianima", sut.Get(GameTextKeys.Common.Revive, "it"));
            // 地区变体沿父区域找到插件语言
            Assert.Equal("Rianima", sut.Get(GameTextKeys.Common.Revive, "it-IT"));
            // 插件缺少的条目回退到基准语言
            Assert.Equal("快速编队", sut.Get(GameTextKeys.Domain.QuickSetup, "it"));
        }

        [Fact]
        public void AddPluginLanguages_BuiltinLanguage_ShouldOverrideThenFallbackToBuiltin()
        {
            var sut = CreateSut();
            sut.AddPluginLanguages("lang_en", [Pack("en", "English (plugin)", (GameTextKeys.Common.Revive, "Revive!"))]);

            Assert.Equal("Revive!", sut.Get(GameTextKeys.Common.Revive, "en"));
            // 插件缺少的条目取内置文本
            Assert.Equal("(Challenge|Completed)", sut.Get(GameTextKeys.Domain.ChallengeCompleted, "en"));
            // 被覆盖的内置语言保持原位置和名称，不重复出现
            Assert.Equal(GameTextService.BuiltinLanguages, sut.Languages);
        }

        [Fact]
        public void AddPluginLanguages_SameCode_ShouldUseLastRegistered()
        {
            var sut = CreateSut();
            sut.AddPluginLanguages("a", [Pack("it", "Italiano A", (GameTextKeys.Common.Revive, "A"))]);
            sut.AddPluginLanguages("b", [Pack("it", "Italiano B", (GameTextKeys.Common.Revive, "B"))]);
            Assert.Equal("B", sut.Get(GameTextKeys.Common.Revive, "it"));

            sut.RemovePluginLanguages("b");
            Assert.Equal("A", sut.Get(GameTextKeys.Common.Revive, "it"));
        }

        [Fact]
        public void RemovePluginLanguages_ShouldFallbackToBaseLanguage()
        {
            var sut = CreateSut();
            sut.AddPluginLanguages("lang_it", [Pack("it", "Italiano", (GameTextKeys.Common.Revive, "Rianima"))]);
            var changed = 0;
            sut.LanguagesChanged += (_, _) => changed++;

            sut.RemovePluginLanguages("lang_it");
            // 没有登记过的来源不触发变化
            sut.RemovePluginLanguages("lang_it");
            sut.AddPluginLanguages("other", []);

            Assert.Equal(1, changed);
            Assert.False(sut.IsAvailable("it"));
            Assert.Equal(GameTextService.BuiltinLanguages, sut.Languages);
            Assert.Equal("复苏", sut.Get(GameTextKeys.Common.Revive, "it"));
            Assert.Equal(GameTextService.BaseLanguage, sut.GetEffectiveLanguage("it"));
        }

        /// <summary>
        /// 构造一个插件游戏语言。
        /// </summary>
        private static GameTextPack Pack(string code, string displayName, params (string Key, string Value)[] entries)
        {
            return new GameTextPack(code, displayName, entries.ToDictionary(e => e.Key, e => e.Value));
        }
    }
}
