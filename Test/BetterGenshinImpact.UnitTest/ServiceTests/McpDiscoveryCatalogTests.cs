using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.Mcp;
using BetterGenshinImpact.Service.Agent;
using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.UnitTest.ServiceTests;

public class McpDiscoveryCatalogTests
{
    [Fact]
    public void SettingsCatalog_ShouldExposeDescriptionsDefaultsAndSections()
    {
        var catalog = new McpSettingsCatalog();
        var entries = catalog.Build(new AllConfig());

        Assert.True(entries.Count > 600);
        var triggerInterval = Assert.Single(entries, x => x.Path == "triggerInterval");
        Assert.Contains("触发器触发频率", triggerInterval.Description);
        Assert.Equal("root", triggerInterval.Section);
        Assert.Equal(50, triggerInterval.DefaultValue);
        Assert.Contains(entries, x => x.Section == "autoDomainConfig");
        Assert.Contains(entries, x => x.Section == "notificationConfig" && x.Sensitive);
        Assert.Contains(entries, x => x.Path == "agentConfig.apiKey" && x.Sensitive);
        var inferred = Assert.Single(entries, x => x.Path == "scriptConfig.autoUpdateSubscribedScripts");
        Assert.Equal("inferred", inferred.DescriptionSource);
        Assert.Contains("脚本仓库", inferred.Description);
    }

    [Fact]
    public void RepositoryIndex_ShouldFlattenPathsAndPreserveMetadata()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"BetterGI-McpIndex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var file = Path.Combine(tempDirectory, "repo.json");
            File.WriteAllText(file, """
                {
                  "time": "20260820183550",
                  "url": "https://example.invalid/repo.zip",
                  "indexes": [
                    {
                      "name": "pathing",
                      "type": "directory",
                      "children": [
                        {
                          "name": "地方特产",
                          "type": "directory",
                          "children": [
                            {
                              "name": "慕风蘑菇.json",
                              "type": "file",
                              "version": "1.0",
                              "author": "测试作者",
                              "description": "采集慕风蘑菇",
                              "tags": ["蒙德", "地方特产"],
                              "lastUpdated": "2026-08-20 12:00:00"
                            }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);

            var index = McpRepositoryIndex.LoadFile(file);

            Assert.Equal(3, index.Nodes.Count);
            var item = index.ByPath["pathing/地方特产/慕风蘑菇.json"];
            Assert.Equal("pathing/地方特产", item.ParentPath);
            Assert.Equal("pathing", item.RootType);
            Assert.Equal("测试作者", item.Author);
            Assert.Contains("蒙德", item.Tags);
            Assert.Single(index.ChildrenOf("pathing/地方特产"));

            File.WriteAllText(file, File.ReadAllText(file).Replace("慕风蘑菇.json", "慕风蘑菇更新.json"));
            var updatedIndex = McpRepositoryIndex.LoadFile(file);
            Assert.Contains("pathing/地方特产/慕风蘑菇更新.json", updatedIndex.ByPath.Keys);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void AgentConversationCache_ShouldEnforceMessageAndCharacterLimits()
    {
        var config = new AgentConfig
        {
            MaxPersistedMessages = 20,
            MaxPersistedCharacters = 10000,
            MaxPersistedMessageCharacters = 1000,
        };
        var messages = Enumerable.Range(0, 30)
            .Select(index => new AgentConversationMessage(
                index % 2 == 0 ? "user" : "assistant",
                new string((char)('a' + index % 26), 3000)))
            .ToArray();

        var limited = McpAgentService.LimitPersistedConversation(messages, config);

        Assert.True(limited.Count <= 20);
        Assert.True(limited.Sum(x => x.Content.Length) <= 10000);
        Assert.All(limited, x => Assert.True(x.Content.Length <= 1000));
        Assert.Equal(messages[^1].Role, limited[^1].Role);
    }

    [Fact]
    public async Task TaskExecutionSignalHub_ShouldSignalOnlyCurrentExecution()
    {
        var firstId = Guid.NewGuid().ToString("N");
        var secondId = Guid.NewGuid().ToString("N");
        var firstSignal = TaskExecutionSignalHub.Register(firstId);
        var secondSignal = TaskExecutionSignalHub.Register(secondId);
        try
        {
            using (TaskExecutionSignalHub.Enter(firstId))
            {
                TaskExecutionSignalHub.SignalRunning();
            }

            Assert.True(firstSignal.IsCompletedSuccessfully);
            Assert.False(secondSignal.IsCompleted);
        }
        finally
        {
            TaskExecutionSignalHub.Unregister(firstId);
            TaskExecutionSignalHub.Unregister(secondId);
        }
    }
}
