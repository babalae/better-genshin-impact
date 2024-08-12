/**
 * 注意本脚本的方案是首次传送到晶蝶位置后的方案。
 * 如果你在传送点待了很久的话，晶蝶会自己飞走，即便是有早柚的情况下。
 * 这种情况下没法完全捕捉到晶蝶。
 */
(async function () {

    // 启用自动拾取的实时任务
    dispatcher.addTimer(new RealtimeTimer("AutoPick"));

    log.info('开始捕捉晶蝶，请在队伍中务必携带{zy}，使用成男/成女角色', '早柚');

    async function captureCrystalfly(locationName, x, y, num) {
        log.info('前往 {name}', locationName);
        await genshin.tp(x, y);
        await sleep(1000);
        log.info('尝试捕捉晶蝶, {num}只', num);
        let filePath = `assets/${locationName}.json`;
        await keyMouseScript.runFile(filePath);
    }

    await captureCrystalfly('枫丹-塔拉塔海谷', 4328, 3960, 4);
    await captureCrystalfly('枫丹-枫丹廷区2', 4822, 3628, 3);
    await captureCrystalfly('枫丹-苍白的遗荣', 4188, 2992, 2);
    await captureCrystalfly('枫丹-幽林雾道', 3376, 3290, 2);
    await captureCrystalfly('枫丹-莫尔泰区', 3810, 2334, 2);
    await captureCrystalfly('枫丹-特别温暖的地方', 4790, 2520, 3);
    await captureCrystalfly('须弥-下风蚀地', 4452, -2456, 3);
})();