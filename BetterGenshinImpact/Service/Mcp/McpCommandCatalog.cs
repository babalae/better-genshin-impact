using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace BetterGenshinImpact.Service.Mcp;

/// <summary>
/// 将主应用 DI 中注册的 ViewModel 命令投影为稳定、可发现的 function calling 目录。
/// </summary>
public sealed partial class McpCommandCatalog(
    IServiceProvider services,
    McpCommandCatalogOptions options)
{
    private readonly IReadOnlyDictionary<string, CommandRegistration> _commands = BuildCatalog(options.ViewModelTypes);

    public IReadOnlyList<McpCommandDescriptor> List(string? filter, bool includeDangerous)
    {
        IEnumerable<CommandRegistration> query = _commands.Values;
        if (!includeDangerous)
        {
            query = query.Where(x => !x.Descriptor.RequiresConfirmation);
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(x =>
                x.Descriptor.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || x.Descriptor.ViewModel.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || x.Descriptor.Command.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        return query.Select(x => x.Descriptor).OrderBy(x => x.Name).ToArray();
    }

    public async Task<McpCommandInvocationResult> InvokeAsync(
        string name,
        JsonElement? argument,
        bool confirm,
        CancellationToken cancellationToken)
    {
        if (!_commands.TryGetValue(name, out var registration))
        {
            throw new ArgumentException($"未找到命令“{name}”。请先调用 list_commands 获取可用名称。", nameof(name));
        }

        if (registration.Descriptor.RequiresConfirmation && !confirm)
        {
            throw new InvalidOperationException($"命令“{name}”可能修改或删除数据；请在确认影响后将 confirm 设为 true。");
        }

        var parameter = ConvertArgument(argument, registration.ParameterType);
        var startedAt = DateTimeOffset.Now;
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var viewModel = services.GetService(registration.ViewModelType)
                            ?? throw new InvalidOperationException(
                                $"ViewModel 未注册：{registration.ViewModelType.FullName}");
            var command = registration.Property.GetValue(viewModel) as ICommand
                          ?? throw new InvalidOperationException($"属性不是 ICommand：{registration.Property.Name}");
            if (!command.CanExecute(parameter))
            {
                throw new InvalidOperationException($"命令“{name}”当前不可执行，请检查页面状态和必填参数。");
            }

            if (command is IAsyncRelayCommand asyncCommand)
            {
                await asyncCommand.ExecuteAsync(parameter).WaitAsync(cancellationToken);
            }
            else
            {
                command.Execute(parameter);
            }
        }).Task.Unwrap();

        return new McpCommandInvocationResult(name, true, startedAt, DateTimeOffset.Now);
    }

    private static IReadOnlyDictionary<string, CommandRegistration> BuildCatalog(IEnumerable<Type> viewModelTypes)
    {
        var result = new Dictionary<string, CommandRegistration>(StringComparer.OrdinalIgnoreCase);
        foreach (var viewModelType in viewModelTypes)
        {
            foreach (var property in viewModelType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(x => typeof(ICommand).IsAssignableFrom(x.PropertyType) &&
                                     x.Name.EndsWith("Command", StringComparison.Ordinal)))
            {
                var viewModelName = TrimSuffix(viewModelType.Name, "ViewModel");
                var commandName = TrimSuffix(property.Name, "Command");
                var name = $"{ToSnakeCase(viewModelName)}.{ToSnakeCase(commandName)}";
                var parameterType = FindCommandParameterType(property.PropertyType);
                var dangerous = IsDangerous(commandName);
                result[name] = new CommandRegistration(
                    new McpCommandDescriptor(
                        name,
                        viewModelType.Name,
                        property.Name,
                        parameterType?.FullName,
                        typeof(IAsyncRelayCommand).IsAssignableFrom(property.PropertyType),
                        dangerous),
                    viewModelType,
                    property,
                    parameterType);
            }
        }

        return result;
    }

    private static object? ConvertArgument(JsonElement? argument, Type? parameterType)
    {
        if (parameterType is null)
        {
            return null;
        }

        if (argument is null || argument.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) is null)
            {
                throw new ArgumentException($"命令需要 {parameterType.FullName} 类型的 argument。");
            }

            return null;
        }

        return JsonSerializer.Deserialize(argument.Value.GetRawText(), parameterType, ConfigService.JsonOptions)
               ?? throw new ArgumentException($"无法把 argument 转换为 {parameterType.FullName}。");
    }

    private static Type? FindCommandParameterType(Type commandType)
    {
        var genericCommand = commandType.GetInterfaces()
            .Append(commandType)
            .FirstOrDefault(x => x.IsGenericType &&
                                 (x.GetGenericTypeDefinition() == typeof(IRelayCommand<>)
                                  || x.GetGenericTypeDefinition() == typeof(IAsyncRelayCommand<>)));
        return genericCommand?.GetGenericArguments()[0];
    }

    private static bool IsDangerous(string name)
    {
        string[] keywords =
        [
            "Delete", "Remove", "Clear", "Reset", "Exit", "Close", "Shutdown", "Restart",
            "Install", "Update", "Import", "Save", "Write", "Overwrite", "Uninstall",
        ];
        return keywords.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static string TrimSuffix(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.Ordinal) ? value[..^suffix.Length] : value;

    private static string ToSnakeCase(string value) =>
        SnakeCaseRegex().Replace(value, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SnakeCaseRegex();

    private sealed record CommandRegistration(
        McpCommandDescriptor Descriptor,
        Type ViewModelType,
        PropertyInfo Property,
        Type? ParameterType);
}

public sealed record McpCommandDescriptor(
    string Name,
    string ViewModel,
    string Command,
    string? ParameterType,
    bool IsAsync,
    bool RequiresConfirmation);

public sealed record McpCommandInvocationResult(
    string Command,
    bool Accepted,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);