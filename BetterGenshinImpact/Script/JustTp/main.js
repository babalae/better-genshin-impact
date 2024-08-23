(async function () {
    // settings 的对象内容来自于 settings.json 文件生成的动态配置页面
    await genshin.tp(settings.x, settings.y);
    await sleep(1000);
})();