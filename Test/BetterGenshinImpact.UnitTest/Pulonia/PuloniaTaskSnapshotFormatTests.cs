using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 固定运行快照（含展开节点树）的持久化格式：旧快照读入后再写出必须逐字一致，已排队或中断的运行才能按原快照续跑。
/// </summary>
public sealed class PuloniaTaskSnapshotFormatTests
{
    /// <summary>
    /// 按旧实现输出格式写成的运行快照：覆盖空值、false、0、整数与浮点、超长整数、类日期字符串、转义、
    /// 嵌套对象和空集合，以及仓库资源、采集 CD 规则和继承后的执行策略。
    /// </summary>
    private const string LegacySnapshotJson = """
        {
          "plan_id": "pln-a",
          "revision": 3,
          "prepared_at": "2024-05-01T08:00:00.123+08:00",
          "account_id": "acc-a",
          "genshin_account_json": null,
          "root_task": {
            "plan_id": "pln-a",
            "task_id": "tsk-root",
            "source_task_id": "tsk-root",
            "task_address": "pln-a/tsk-root",
            "name": "根分组",
            "task_type": "group",
            "is_enabled": true,
            "path": null,
            "resource_version": null,
            "parameters": {},
            "parameter_sources": {},
            "policy": {
              "timeout_seconds": null,
              "failure_behavior": "stop_plan",
              "max_retries": 0,
              "retry_delay_seconds": 0.0
            },
            "children": [
              {
                "plan_id": "pln-a",
                "task_id": "tsk-leaf",
                "source_task_id": "tsk-leaf",
                "task_address": "pln-a/tsk-root/tsk-leaf",
                "name": "路线",
                "task_type": "pathing",
                "is_enabled": true,
                "path": null,
                "resource": {
                  "repository_id": "official",
                  "relative_path": "pathing/示例/路线.json",
                  "approved_revision": "0123456789abcdef0123456789abcdef01234567"
                },
                "resource_version": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "collection_cooldown": {
                  "file_identity": "official/pathing/示例/路线.json",
                  "material_name": "清心",
                  "mode": "rolling",
                  "hours": 12.5,
                  "days": 0,
                  "reset_hour": 4,
                  "reset_weekday": 1,
                  "is_reference": false,
                  "source": "目录自动识别"
                },
                "parameters": {
                  "text": "中文 \"引号\" \\ 斜杠",
                  "date_like": "2024-01-02T03:04:05Z",
                  "flag": false,
                  "zero": 0,
                  "ratio": 1.5,
                  "whole_float": 2.0,
                  "big": 12345678901234567890,
                  "none": null,
                  "list": [
                    1,
                    "two",
                    null
                  ],
                  "nested": {
                    "inner": {
                      "deep": true
                    }
                  },
                  "empty_object": {},
                  "empty_array": []
                },
                "parameter_sources": {
                  "text": "pln-a/tsk-root/tsk-leaf",
                  "ratio": "default/pathing"
                },
                "policy": {
                  "timeout_seconds": 30.5,
                  "failure_behavior": "continue",
                  "max_retries": 2,
                  "retry_delay_seconds": 1.25
                },
                "children": []
              }
            ]
          },
          "plan_json_by_id": {
            "pln-a": "{\"id\":\"pln-a\"}"
          },
          "preset_json_by_id": {}
        }
        """;

    /// <summary>
    /// 旧快照读入后再写出逐字一致：字段名、顺序、空值、数值类型与字符串内容都不变。
    /// </summary>
    [Fact]
    public void LegacySnapshot_RoundTripsWithoutChanges()
    {
        var snapshot = PuloniaTaskJson.ReadSnapshot(LegacySnapshotJson);

        Assert.Equal(LegacySnapshotJson.ReplaceLineEndings("\n"), snapshot.ToJson().ReplaceLineEndings("\n"));
        // 再读一次仍得到相同文本，续跑后再次归档也不会漂移。
        Assert.Equal(snapshot.ToJson(), PuloniaTaskJson.ReadSnapshot(snapshot.ToJson()).ToJson());
    }

    /// <summary>
    /// 展开节点对外只给出参数和策略的副本；修改副本或构造时传入的对象都不影响运行树和持久化内容。
    /// </summary>
    [Fact]
    public void PreparedTask_ExposesIndependentCopies()
    {
        var snapshot = PuloniaTaskJson.ReadSnapshot(LegacySnapshotJson);
        var leaf = snapshot.RootTask.Children[0];

        // 1. 修改读取到的副本
        var parameters = leaf.Parameters;
        parameters["text"] = "被修改";
        ((JObject)parameters["nested"]!)["inner"] = null;
        var policy = leaf.Policy;
        policy.MaxRetries = 99;

        Assert.NotSame(leaf.Parameters, leaf.Parameters);
        Assert.NotSame(leaf.Policy, leaf.Policy);
        Assert.Equal("中文 \"引号\" \\ 斜杠", leaf.Parameters.Value<string>("text"));
        Assert.True(leaf.Parameters["nested"]!["inner"]!.Value<bool>("deep"));
        Assert.Equal(2, leaf.Policy.MaxRetries);
        Assert.Equal(LegacySnapshotJson.ReplaceLineEndings("\n"), snapshot.ToJson().ReplaceLineEndings("\n"));

        // 2. 修改构造时传入的对象
        var inputParameters = new JObject { ["value"] = 1 };
        var inputPolicy = new PuloniaTaskPolicy { MaxRetries = 1 };
        var sources = new Dictionary<string, string> { ["value"] = "node" };
        var prepared = new PuloniaTaskPreparedTask("pln-a", "tsk-a", "tsk-a", "pln-a/tsk-a", "节点", "sample.echo", true,
            null, null, inputParameters, sources, inputPolicy, []);
        inputParameters["value"] = 2;
        inputPolicy.MaxRetries = 5;
        sources["value"] = "changed";

        Assert.Equal(1, prepared.Parameters.Value<int>("value"));
        Assert.Equal(1, prepared.Policy.MaxRetries);
        Assert.Equal("node", prepared.ParameterSources["value"]);
    }
}
