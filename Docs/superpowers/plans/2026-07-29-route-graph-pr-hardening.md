# Route Graph PR Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复四个确定性的自动寻路缺陷，并从不含 AutoQuest 的分支向 `physligl/better-genshin-impact:RefactorPathing` 准备 PR。

**Architecture:** 保持现有规划器、成本模型、路网工作室和 Provider 的边界不变，只在缺陷产生的位置增加安全约束或容错。每个行为先增加独立回归测试并确认失败，再做最小实现；GraphId 兼容策略和编辑中快照重建不在本次范围。

**Tech Stack:** .NET 8、C#、xUnit、WPF、CommunityToolkit.Mvvm、System.Text.Json

## Global Constraints

- PR 基线为 `physligl/better-genshin-impact:RefactorPathing` (`0cafca33`)。
- 包含现有主线同步和自动寻路提交。
- 不包含 `9d9425ab` AutoQuest 提交及其合并提交 `282e3929`。
- 只修复安全距离、混合来源成本、跨地图人工边和 Provider 签名读取异常。
- 最终只运行一次 `dotnet build BetterGenshinImpact.sln -c Debug`；编译失败不得提交修复。

---

### Task 1: 限制传送点直达距离

**Files:**
- Modify: `BetterGenshinImpact/GameTask/AutoPathing/Telemetry/RouteNavigationPlanner.cs:529-550`
- Test: `Test/BetterGenshinImpact.UnitTest/GameTask/AutoPathing/Telemetry/RouteNavigationPlannerTests.cs`

**Interfaces:**
- Consumes: `RouteNavigationCostOptions.LocalDirectMaxGameDistance`
- Produces: `TryCreateTargetTeleportDirectPlan` 只接受安全距离内的传送点到目标连接

- [x] **Step 1: Write the failing test**

新增 `TryPlan_WhenTargetTeleportExceedsLocalLimit_DoesNotCreateDirectRoute`：构造无当前位置、路网远离目标、唯一传送落地点距目标 200 游戏单位的场景，断言规划失败且 `FailureCode` 为 `TeleportUnavailable`。

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~TryPlan_WhenTargetTeleportExceedsLocalLimit_DoesNotCreateDirectRoute"
```

Expected: FAIL，因为当前代码返回 `Complete` 传送直达计划。

- [x] **Step 3: Write minimal implementation**

在传送直达候选过滤中同时要求：

```csharp
item.LocalCost.IsValid &&
item.LocalCost.GameDistance <= options.CostOptions.LocalDirectMaxGameDistance
```

- [x] **Step 4: Run test to verify it passes**

重复 Step 2 命令，Expected: PASS。

### Task 2: 保留 mixed 边的遥测耗时

**Files:**
- Modify: `BetterGenshinImpact/GameTask/AutoPathing/Telemetry/RouteNavigationCostModel.cs:235-238`
- Test: `Test/BetterGenshinImpact.UnitTest/GameTask/AutoPathing/Telemetry/RouteNavigationCostModelTests.cs`

**Interfaces:**
- Consumes: `RouteNavigationEdge.Sources[*].IsTelemetry`
- Produces: mixed 来源代表边仍可返回 `RouteNavigationCostSource.Telemetry`

- [x] **Step 1: Write the failing test**

新增 `EvaluateEdge_UsesMeasuredDurationWhenMixedSourcesContainTelemetry`：设置 `SourceKind = "mixed"`、`AverageDurationMs = 30000`，并加入 `IsTelemetry = true` 的来源，断言成本为 30 秒且来源为 Telemetry。

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~EvaluateEdge_UsesMeasuredDurationWhenMixedSourcesContainTelemetry"
```

Expected: FAIL，当前代码返回距离估算。

- [x] **Step 3: Write minimal implementation**

让 `IsTelemetry` 同时识别 `SourceKind` 和来源列表：

```csharp
return edge.SourceKind.Contains("telemetry", StringComparison.OrdinalIgnoreCase) ||
       edge.Sources.Any(source => source.IsTelemetry);
```

- [x] **Step 4: Run test to verify it passes**

重复 Step 2 命令，Expected: PASS。

### Task 3: 阻止跨地图人工边

**Files:**
- Modify: `BetterGenshinImpact/ViewModel/Windows/RouteGraphStudioViewModel.cs:104-118,703-709`
- Test: `Test/BetterGenshinImpact.UnitTest/ViewModel/Windows/RouteGraphStudioViewModelTests.cs`

**Interfaces:**
- Consumes: `RouteNavigationNode.MapName`
- Produces: 地图切换清除连接起点，`AddManualEdge` 拒绝不同地图端点

- [x] **Step 1: Write the failing tests**

新增：

- `FilterMapChange_ClearsConnectionStart`
- `AddConnection_DoesNotCreateCrossMapEdge`

使用包含 Teyvat 与 TheChasm 节点的真实临时生成图初始化 ViewModel；分别断言切图后连接起点为空，以及跨地图连接不会增加待保存操作或边。

- [x] **Step 2: Run tests to verify they fail**

Run:

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~RouteGraphStudioViewModelTests.FilterMapChange_ClearsConnectionStart|FullyQualifiedName~RouteGraphStudioViewModelTests.AddConnection_DoesNotCreateCrossMapEdge"
```

Expected: 两个测试至少各自因连接起点未清理或跨地图边被创建而 FAIL。

- [x] **Step 3: Write minimal implementation**

- `OnFilterMapChanged` 设置 `ConnectionStartNode = null`。
- `AddManualEdge` 在地图不同时设置状态文本并返回 `false`。

- [x] **Step 4: Run tests to verify they pass**

重复 Step 2 命令，Expected: PASS。

### Task 4: 将签名读取异常转换为 Provider 失败状态

**Files:**
- Modify: `BetterGenshinImpact/GameTask/AutoPathing/Telemetry/RouteNavigationGraphProvider.cs:89-125`
- Modify: `Test/BetterGenshinImpact.UnitTest/GameTask/AutoPathing/Telemetry/RouteGraphOverrideTests.cs`

**Interfaces:**
- Consumes: `RouteNavigationGraphProvider.TryGetSnapshot`
- Produces: 补丁文件不可读时返回 `false`、`RouteNavigationGraphLoadStatus.Invalid`，不向调用方抛出 I/O 异常

- [x] **Step 1: Write the failing test**

新增 `Provider_WhenOverrideCannotBeRead_ReturnsInvalidInsteadOfThrowing`：创建有效生成图和补丁文件，以 `FileShare.None` 锁定补丁，再断言调用不抛异常、返回 false 且状态为 Invalid。

- [x] **Step 2: Run test to verify it fails**

Run:

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~Provider_WhenOverrideCannotBeRead_ReturnsInvalidInsteadOfThrowing"
```

Expected: FAIL，因为 `BuildLoadSignature` 在 try/catch 外抛出 IOException。

- [x] **Step 3: Write minimal implementation**

将 `BuildLoadSignature` 和缓存命中判断移入现有 try 块，使签名构造与图加载共享同一失败状态转换。

- [x] **Step 4: Run test to verify it passes**

重复 Step 2 命令，Expected: PASS。

### Task 5: 回归验证、提交和 PR 准备

**Files:**
- Modify: only files listed above

**Interfaces:**
- Consumes: all four fixes and tests
- Produces: clean commit and PR-ready branch

- [x] **Step 1: Run targeted regression tests**

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~AutoPathing.Telemetry.Route|FullyQualifiedName~RouteGraphStudioViewModelTests|FullyQualifiedName~TargetNavigationWorkflowTests"
```

- [x] **Step 2: Verify scope**

```powershell
git diff --check
git log --oneline -- BetterGenshinImpact/GameTask/AutoQuest
git diff --name-only 0cafca33..HEAD
```

确认当前提交历史不包含 AutoQuest 合并，工作区仅有计划内文件。

- [x] **Step 3: Run the requested solution build once**

```powershell
dotnet build BetterGenshinImpact.sln -c Debug
```

Expected: 0 errors。若失败，停止提交并报告。

- [ ] **Step 4: Commit the fix**

```powershell
git add Docs/superpowers/plans/2026-07-29-route-graph-pr-hardening.md `
  BetterGenshinImpact/GameTask/AutoPathing/Telemetry/RouteNavigationPlanner.cs `
  BetterGenshinImpact/GameTask/AutoPathing/Telemetry/RouteNavigationCostModel.cs `
  BetterGenshinImpact/GameTask/AutoPathing/Telemetry/RouteNavigationGraphProvider.cs `
  BetterGenshinImpact/ViewModel/Windows/RouteGraphStudioViewModel.cs `
  Test/BetterGenshinImpact.UnitTest/GameTask/AutoPathing/Telemetry/RouteNavigationPlannerTests.cs `
  Test/BetterGenshinImpact.UnitTest/GameTask/AutoPathing/Telemetry/RouteNavigationCostModelTests.cs `
  Test/BetterGenshinImpact.UnitTest/GameTask/AutoPathing/Telemetry/RouteGraphOverrideTests.cs `
  Test/BetterGenshinImpact.UnitTest/ViewModel/Windows/RouteGraphStudioViewModelTests.cs
git commit -m "fix(pathing): harden route graph planning and editing"
```

- [ ] **Step 5: Push and prepare PR**

推送 `codex/route-graph-studio-pr` 到可写 fork，目标为 `physligl/better-genshin-impact:RefactorPathing`。在聊天中提供中文 PR 标题和正文；若 GitHub CLI 仍不可用，则保留可直接创建 PR 的分支并明确说明阻塞。
