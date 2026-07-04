const commonPath = 'assets/'
const commonMap = new Map([
    ['main_ui', {
        path: `${commonPath}`,
        name: 'paimon_menu',
        type: '.png',
    }],
])
const genshinJson = {
    width: 1920,//genshin.width,
    height: 1080,//genshin.height,
}

/**
 * 根据键值获取JSON路径
 * @param {string} key - 要查找的键值
 * @returns {any} 返回与键值对应的JSON路径值
 */
export function getJsonPath(key) {
    return commonMap.get(key); // 通过commonMap的get方法获取指定键对应的值
}


// 判断是否在主界面的函数
export const isInMainUI = () => {
    // let name = '主界面'
    let main_ui = getJsonPath('main_ui');
    // 定义识别对象
    let paimonMenuRo = RecognitionObject.TemplateMatch(
        file.ReadImageMatSync(`${main_ui.path}${main_ui.name}${main_ui.type}`),
        0,
        0,
        genshinJson.width / 3.0,
        genshinJson.width / 5.0
    );
    let captureRegion = captureGameRegion();
    let res = captureRegion.find(paimonMenuRo);
    captureRegion.Dispose()
    return !res.isEmpty();
};

export async function toMainUi() {
    let ms = 1000
    let index = 1
    await sleep(ms);
    while (!isInMainUI()) {
        await sleep(ms);
        await genshin.returnMainUi(); // 如果未启用，则返回游戏主界面
        await sleep(ms);
        if (index > 3) {
            throw new Error(`多次尝试返回主界面失败`);
        }
        index += 1
    }

}
