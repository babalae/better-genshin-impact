using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.SoloTask;
using BetterGenshinImpact.Pulonia.Executors;
using BetterGenshinImpact.Pulonia.Models;
using BetterGenshinImpact.Pulonia.Services;
using Newtonsoft.Json.Linq;

namespace BetterGenshinImpact.UnitTest.Pulonia;

/// <summary>
/// 内置能力说明的合法性由单元测试保证，运行准备不再逐次复验：类型与资源唯一、默认值为标准 JSON、Schema 只用受支持的关键字。
/// </summary>
public sealed class PuloniaTaskDefinitionContractTests
{

    /// <summary>
    /// 可在测试进程创建的内置执行器，加上与独立任务执行器同规则生成的说明，全部通过注册校验。
    /// </summary>
    [Fact]
    public void BuiltInDefinitions_PassRegistrationValidation()
    {
        IPuloniaTaskExecutor[] executors =
        [
            new PuloniaCSharpTaskExecutor(new PuloniaCSharpTaskRegistry()), new PuloniaShellTaskExecutor(),
            new PuloniaPathingTaskExecutor(new AllConfig()), new PuloniaJavaScriptTaskExecutor(), new PuloniaKeyMouseTaskExecutor()
        ];
        PuloniaTaskValidator.ValidateDefinitions(executors.SelectMany(executor => executor.Definitions)
            .Concat(PuloniaSoloTestDefinitions.OneDragonCandidates()));
    }

    /// <summary>
    /// 真实独立任务注册表中每个可编排任务的参数 Schema 都只使用受支持的关键字。
    /// </summary>
    [Fact]
    public void SoloTaskSchemas_UseSupportedKeywordsOnly()
    {
        var registry = new SoloTaskRegistry([typeof(SoloTaskRegistry).Assembly]);
        foreach (var descriptor in registry.GetByScope(SoloTaskScopes.Plan))
            PuloniaTaskValidator.ValidateParameterSchema(PuloniaSoloTaskSchema.Create(descriptor.ParamType), descriptor.TaskId);
    }

    /// <summary>
    /// 注册校验拒绝重复的类型与资源、未知 Schema 关键字以及非标准 JSON 默认值。
    /// </summary>
    [Fact]
    public void ValidateDefinitions_RejectsDuplicateOrInvalidDeclarations()
    {
        var definition = new PuloniaTaskDefinition { TaskType = "sample.echo" };
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateDefinitions([definition, definition]));
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateDefinitions(
            [new PuloniaTaskDefinition { TaskType = "sample.echo", ParameterSchema = new JObject { ["pattern"] = "x" } }]));
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateDefinitions(
            [new PuloniaTaskDefinition { TaskType = "sample.echo", DefaultParameters = new JObject { ["nan"] = double.NaN } }]));
    }

    /// <summary>
    /// Schema 的 minimum 约束：等于下限通过，低于下限拒绝，非数值不受影响。
    /// </summary>
    [Fact]
    public void ValidateParameters_EnforcesMinimum()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject { ["limit"] = new JObject { ["type"] = "integer", ["minimum"] = 0 } }
        };
        PuloniaTaskValidator.ValidateParameterSchema(schema, "schema");
        PuloniaTaskValidator.ValidateParameters(new JObject { ["limit"] = 0 }, schema, "values");
        Assert.Throws<PuloniaTaskValidationException>(
            () => PuloniaTaskValidator.ValidateParameters(new JObject { ["limit"] = -1 }, schema, "values"));
        Assert.Throws<PuloniaTaskValidationException>(() => PuloniaTaskValidator.ValidateParameterSchema(
            new JObject { ["minimum"] = "0" }, "schema"));
    }
}
