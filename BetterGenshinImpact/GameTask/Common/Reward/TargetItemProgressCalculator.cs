using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterGenshinImpact.GameTask.Common.Reward;

/// <summary>
/// 材料目标进度计算器。纯函数，不依赖游戏窗口。
/// </summary>
public static class TargetItemProgressCalculator
{
    /// <summary>
    /// 计算目标进度：同族从低到高先保留低级目标，再把富余按配方逐级折算。
    /// </summary>
    /// <param name="targets">目标物品与数量。</param>
    /// <param name="totals">本次统计到的物品数量。</param>
    /// <param name="recipes">合成配方。</param>
    /// <returns>按输入目标顺序排列的进度。</returns>
    public static TargetProgress Calculate(
        IReadOnlyList<TargetItemSpec> targets,
        IReadOnlyDictionary<string, int> totals,
        IReadOnlyList<RewardCraftRecipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(totals);
        ArgumentNullException.ThrowIfNull(recipes);

        var targetByName = new Dictionary<string, TargetItemSpec>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.ItemName))
            {
                throw new ArgumentException("目标物品名不能为空", nameof(targets));
            }

            if (target.TargetCount <= 0)
            {
                throw new ArgumentException($"目标数量必须大于 0：{target.ItemName}", nameof(targets));
            }

            if (!targetByName.TryAdd(target.ItemName, target))
            {
                throw new ArgumentException($"目标物品重复：{target.ItemName}", nameof(targets));
            }
        }

        if (targetByName.Count == 0)
        {
            return new TargetProgress([]);
        }

        var recipeByInput = new Dictionary<string, RewardCraftRecipe>(StringComparer.Ordinal);
        var recipeByOutput = new Dictionary<string, RewardCraftRecipe>(StringComparer.Ordinal);
        foreach (var recipe in recipes)
        {
            if (string.IsNullOrWhiteSpace(recipe.InputName) || string.IsNullOrWhiteSpace(recipe.OutputName))
            {
                throw new ArgumentException("配方物品名不能为空", nameof(recipes));
            }

            if (recipe.InputCount <= 0 || recipe.OutputCount <= 0)
            {
                throw new ArgumentException($"配方数量必须大于 0：{recipe.InputName} → {recipe.OutputName}", nameof(recipes));
            }

            if (!recipeByInput.TryAdd(recipe.InputName, recipe))
            {
                throw new ArgumentException($"配方输入重复：{recipe.InputName}", nameof(recipes));
            }

            if (!recipeByOutput.TryAdd(recipe.OutputName, recipe))
            {
                throw new ArgumentException($"配方输出重复：{recipe.OutputName}", nameof(recipes));
            }
        }

        // 每个目标向上回溯到没有配方输入的根，按根分组，保证同一材料族只计算一次。
        var targetsByRoot = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var targetName in targetByName.Keys)
        {
            var root = targetName;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (recipeByOutput.TryGetValue(root, out var recipe))
            {
                if (!visited.Add(root))
                {
                    throw new ArgumentException($"配方存在环：{targetName}", nameof(recipes));
                }

                root = recipe.InputName;
            }

            if (!targetsByRoot.TryGetValue(root, out var familyTargets))
            {
                familyTargets = [];
                targetsByRoot[root] = familyTargets;
            }

            familyTargets.Add(targetName);
        }

        var progressByName = new Dictionary<string, TargetItemProgress>(StringComparer.Ordinal);

        foreach (var (root, familyTargets) in targetsByRoot)
        {
            // 从根沿配方向上展开完整材料链。
            var chain = new List<string> { root };
            var current = root;
            var visited = new HashSet<string>(StringComparer.Ordinal) { root };
            while (recipeByInput.TryGetValue(current, out var recipe))
            {
                if (!visited.Add(recipe.OutputName))
                {
                    throw new ArgumentException($"配方存在环：{current}", nameof(recipes));
                }

                chain.Add(recipe.OutputName);
                current = recipe.OutputName;
            }

            foreach (var targetName in familyTargets)
            {
                if (!chain.Contains(targetName, StringComparer.Ordinal))
                {
                    throw new ArgumentException($"目标物品不在同一材料链上：{targetName}", nameof(targets));
                }
            }

            // 从低到高：先保留本级目标，再把富余按配方折算给下一级。
            var carry = 0;
            foreach (var itemName in chain)
            {
                var available = GetCount(totals, itemName) + carry;
                if (targetByName.TryGetValue(itemName, out var target))
                {
                    var counted = Math.Min(available, target.TargetCount);
                    progressByName[itemName] = new TargetItemProgress(
                        itemName,
                        target.TargetCount,
                        counted,
                        counted >= target.TargetCount);
                    available -= counted;
                }

                carry = recipeByInput.TryGetValue(itemName, out var recipe)
                    ? available / recipe.InputCount * recipe.OutputCount
                    : 0;
            }
        }

        var result = new List<TargetItemProgress>(targets.Count);
        foreach (var target in targets)
        {
            if (!progressByName.TryGetValue(target.ItemName, out var progress))
            {
                // 未参与任何配方的目标直接比较原始数量。
                var counted = Math.Min(GetCount(totals, target.ItemName), target.TargetCount);
                progress = new TargetItemProgress(
                    target.ItemName,
                    target.TargetCount,
                    counted,
                    counted >= target.TargetCount);
            }

            result.Add(progress);
        }

        return new TargetProgress(result);
    }

    private static int GetCount(IReadOnlyDictionary<string, int> totals, string itemName)
    {
        return totals.TryGetValue(itemName, out var value) ? Math.Max(0, value) : 0;
    }
}
