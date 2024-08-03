(async function() {
    log.info('等待 {m} s', 1);
    await sleep(1000);
    log.info('测试 {name}', 'TP方法');
    await genshin.tp(3452.310059,2290.465088);
    log.warn('TP完成');
    //await sleep(1000);
    //await runKeyMouseScript('操作1.json');
})();