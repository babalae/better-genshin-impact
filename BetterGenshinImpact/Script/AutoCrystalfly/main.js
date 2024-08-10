(async function () {
    log.info('开始捕捉晶蝶，请在队伍中务必携带{zy}，使用成男/成女角色', '早柚');

    log.info('前往 {name}', '枫丹-塔拉塔海谷');
    await genshin.tp(4328, 3960);
    await sleep(1000);
    log.info('前往并捕捉晶蝶, {num}只', 4);
    await keyMouseScript.runFile('assets/枫丹-塔拉塔海谷.json');

    log.info('前往 {name}', '枫丹-枫丹廷区');
    await genshin.tp(4822, 3628);
    await sleep(1000);
    log.info('前往并捕捉晶蝶, {num}只', 3);
    await keyMouseScript.runFile('assets/枫丹-枫丹廷区.json');

})();