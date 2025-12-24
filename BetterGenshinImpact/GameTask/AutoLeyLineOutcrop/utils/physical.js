const commonPath = 'assets/icon/'
const commonMap = new Map([
    ['main_ui', {
        path: `${commonPath}`,
        name: 'paimon_menu',
        type: '.png',
    }],
    ['yue', {
        path: `${commonPath}`,
        name: 'yue',
        type: '.png',
    }],
    ['200', {
        path: `${commonPath}`,
        name: '200',
        type: '.png',
    }],
    ['add_button', {
        path: `${commonPath}`,
        name: 'add_button',
        type: '.jpg',
    }],
])
//====================================================
const genshinJson = {
    width: 1920,//genshin.width,
    height: 1080,//genshin.height,
}
const MinPhysical = settings.minPhysical?parseInt(settings.minPhysical+''):parseInt(20+'')
const OpenModeCountMin = settings.openModeCountMin
let AlreadyRunsCount=0
let NeedRunsCount=0
const TemplateOrcJson={x: 1568, y: 16, width: 225, height: 60,}
//====================================================
/**
 * 根据键值获取JSON路径
 * @param {string} key - 要查找的键值
 * @returns {any} 返回与键值对应的JSON路径值
 */
function getJsonPath(key) {
    return commonMap.get(key); // 通过commonMap的get方法获取指定键对应的值
}

/**
 * 从字符串中提取数字并组合成一个整数
 * @param {string} str - 包含数字的字符串
 * @returns {number} - 由字符串中所有数字组合而成的整数
 */
async function saveOnlyNumber(str) {
    // 使用正则表达式匹配字符串中的所有数字
    // \d+ 匹配一个或多个数字
    // .join('') 将匹配到的数字数组连接成一个字符串
    // parseInt 将连接后的字符串转换为整数
    return parseInt(str.match(/\d+/g).join(''));
}

/**
 * 识别原粹树脂（体力）的函数
 * @param {boolean} [opToMainUi=false] - 是否操作到主界面
 * @returns {Promise<Object>} 返回一个包含识别结果的Promise对象
 *   - ok {boolean}: 是否可执行（体力是否足够）
 *   - min {number}: 最小可执行体力值
 *   - remainder {number}: 当前剩余体力值
 */
async function ocrPhysical(opToMainUi = false,openMap=false) {
    // 检查是否启用体力识别功能，如果未启用则直接返回默认结果
    if (!settings.isResinExhaustionMode) {
        log.info(`===未启用===`)
        return {
            ok: true,
            min: 0,
            remainder: 0,
        }
    }
    log.info(`===开始识别原粹树脂===`)
    let ms = 1000  // 定义操作延迟时间（毫秒）
    if (opToMainUi) {
        await toMainUi();  // 切换到主界面
    }
    //设置最小可执行体力值
    let minPhysical = MinPhysical
    if (openMap){
        //打开地图界面
        await keyPress('M')
    }
    await sleep(ms)
    log.debug(`===[点击+]===`)
    //点击+ 按钮 x=1264,y=39,width=18,height=19
    let add_buttonJSON = getJsonPath('add_button');
    let add_objJson = {
        path: `${add_buttonJSON.path}${add_buttonJSON.name}${add_buttonJSON.type}`,
        x: 1264,
        y: 39,
        width: genshinJson.width - 1264,
        height: 60,
    }
    let templateMatchAddButtonRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync(`${add_objJson.path}`), add_objJson.x, add_objJson.y, add_objJson.width, add_objJson.height);
    let regionA = captureGameRegion()
    // let buttonA = captureGameRegion().find(templateMatchAddButtonRo);
    let buttonA = regionA.find(templateMatchAddButtonRo);
    regionA.Dispose()

    await sleep(ms)
    if (!buttonA.isExist()) {
        log.error(`未找到${add_objJson.path}请检查路径是否正确`)
        throwError(`未找到${add_objJson.path}请检查路径是否正确`)
    }
    await buttonA.click()
    // let add_obj = {
    //     x: 1264,
    //     y: 39,
    // }
    // await click(add_obj.x, add_obj.y)
    await sleep(ms)

    log.debug(`===[定位原粹树脂]===`)
    //定位月亮
    let jsonPath = getJsonPath('yue');
    let tmJson = {
        path: `${jsonPath.path}${jsonPath.name}${jsonPath.type}`,
        x: TemplateOrcJson.x,
        y: TemplateOrcJson.y,
        width: TemplateOrcJson.width,
        height: TemplateOrcJson.height,
    }
    let templateMatchButtonRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync(`${tmJson.path}`), tmJson.x, tmJson.y, tmJson.width, tmJson.height);
    let region =captureGameRegion()
    // let button = captureGameRegion().find(templateMatchButtonRo);
    let button = region.find(templateMatchButtonRo);
    region.Dispose()
    await sleep(ms)
    if (!button.isExist()) {
        log.error(`${tmJson.path} 匹配异常`)
        throwError(`${tmJson.path} 匹配异常`)
    }

    log.debug(`===[定位/200]===`)
    //定位200
    let jsonPath2 = getJsonPath('200');
    let tmJson2 = {
        path: `${jsonPath2.path}${jsonPath2.name}${jsonPath2.type}`,
        x: TemplateOrcJson.x,
        y: TemplateOrcJson.y,
        width: TemplateOrcJson.width,
        height: TemplateOrcJson.height,
    }
    let templateMatchButtonRo2 = RecognitionObject.TemplateMatch(file.ReadImageMatSync(`${tmJson2.path}`), tmJson2.x, tmJson2.y, tmJson2.width, tmJson2.height);
    let region2 = captureGameRegion()
    let button2 = region2.find(templateMatchButtonRo2);
    region2.Dispose()

    await sleep(ms)
    if (!button2.isExist()) {
        log.error(`${tmJson2.path} 匹配异常`)
        throwError(`${tmJson2.path} 匹配异常`)
    }

    log.debug(`===[识别原粹树脂]===`)
    //识别体力 x=1625,y=31,width=79,height=30 / x=1689,y=35,width=15,height=26
    let ocr_obj = {
        // x: 1623,
        x: button.x + button.width-20,
        // y: 32,
        y: button.y,
        // width: 61,
        width: Math.abs(button2.x - button.x - button.width+20),
        height: button2.height
    }

    log.debug(`ocr_obj: x={x},y={y},width={width},height={height}`, ocr_obj.x, ocr_obj.y, ocr_obj.width, ocr_obj.height)

    try {
        let recognitionObjectOcr = RecognitionObject.Ocr(ocr_obj.x, ocr_obj.y, ocr_obj.width, ocr_obj.height);
        let region3 = captureGameRegion()
        let res = region3.find(recognitionObjectOcr);
        region3.Dispose()

        log.info(`[OCR原粹树脂]识别结果: ${res.text}, 原始坐标: x=${res.x}, y=${res.y},width:${res.width},height:${res.height}`);
        let remainder = await saveOnlyNumber(res.text)
        let execute = (remainder - minPhysical) >= 0
        log.info(`最小可执行原粹树脂:{min},原粹树脂:{key}`, minPhysical, remainder,)

        // await keyPress('VK_ESCAPE')
        return {
            ok: execute,
            min: minPhysical,
            remainder: remainder,
        }
    } catch (e) {
        throwError(`识别失败,err:${e.message}`)
    } finally {
        //返回地图操作
        if (opToMainUi) {
            await toMainUi();  // 切换到主界面
        }
    }

}

/**
 * 抛出错误函数
 * 该函数用于显示错误通知并抛出错误对象
 * @param {string} msg - 错误信息，将用于通知和错误对象
 */
function throwError(msg) {
    // 使用notification组件显示错误通知
    // notification.error(`${msg}`);
    if (setting.isNotification) {
        notification.error(`${msg}`);
    }
    // 抛出一个包含错误信息的Error对象
    throw new Error(`${msg}`);
}

// 判断是否在主界面的函数
const isInMainUI = () => {
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

async function toMainUi() {
    let ms = 300
    let index = 1
    await sleep(ms);
    while (!isInMainUI()) {
        await sleep(ms);
        await genshin.returnMainUi(); // 如果未启用，则返回游戏主界面
        await sleep(ms);
        if (index > 3) {
            throwError(`多次尝试返回主界面失败`);
        }
        index += 1
    }

}

this.physical = {
    ocrPhysical,
    MinPhysical,
    OpenModeCountMin,
    AlreadyRunsCount,
    NeedRunsCount,
}