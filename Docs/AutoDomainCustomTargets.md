# JS 自定义培养目标

```js
const param = new AutoDomainParam(0);
param.setTrainingTargets(JSON.stringify([
    { material: "「诤言」的哲学", target: 28 },
    { material: "贡祭炽心的荣膺", target: 4 }
]));
param.trainingGuideRewardRecognitionEnabled = true;
// 可选：充分刷取，不预留合成天赋收益。
param.trainingGuideRunPreference = 1;
// 可选：目标完成后结束，不继承本地配置的备选秘境。
param.trainingGuideFallbackDomainName = "";
await dispatcher.runAutoDomainTask(param);
```

- `target` 是最终目标库存，不是额外刷取数量。材料名称必须完整，以确定家族和等级。
- 未设置 `trainingTargetsJson`（默认 null）时沿用原有任务模式；设置后自动启用自定义培养规划，无需设置秘境名称或培养规划开关。
- 可直接设置 `trainingTargetsJson`，或使用 `setTrainingTargets(json)` 提前验证。传入空数组、非法名称、非正整数、重复材料会报错。
- 同一家族可指定多个等级的目标。低级材料先保留本级需求，剩余部分参与普通合成折算；程序不实际执行合成。
- 直接前往材料对应秘境，跳过提升指南预读和“需求角色”检查。弹窗只提供实际库存，游戏内的目标不参与本次规划。
- 未开放或未识别的入口会报告并跳过。存在此类目标时结束后不进入备选秘境。
- 刷取偏好、合成收益预留、树脂限制、战斗策略等仍沿用 AutoDomainParam，可逐项覆盖；不修改独立任务的持久化配置。
- 自定义目标不能与仅扫描开发模式同时启用。接口返回值仍为原有奖励汇总，不是库存或目标完成状态。
- 仍以最高难度和已有掉落期望计算，未解锁最高难度的适配限制不变。
