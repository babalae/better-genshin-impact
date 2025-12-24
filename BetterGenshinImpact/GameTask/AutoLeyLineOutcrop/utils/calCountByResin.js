/**
 * OCR树脂数量统计脚本
 * 功能：自动识别并统计原神中各种树脂的数量
 * 支持：原粹树脂、浓缩树脂、须臾树脂、脆弱树脂
 */

// ==================== 常量定义 ====================

// 树脂图标识别对象
const RESIN_ICONS = {
    ORIGINAL: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/original_resin.png")),
    CONDENSED: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/condensed_resin.png")),
    FRAGILE: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/fragile_resin.png")),
    TRANSIENT: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/transient_resin.png")),
    REPLENISH_BUTTON: RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/icon/replenish_resin_button.png"))
};

// 普通数字识别对象（1-4）
const NUMBER_ICONS = [
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num1.png")), value: 1},
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num2.png")), value: 2},
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num3.png")), value: 3},
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num4.png")), value: 4}
];

// 白色数字识别对象（0-5，用于浓缩树脂）
const WHITE_NUMBER_ICONS = [
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num0_white.png")), value: 0},
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num1_white.png")), value: 1},
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num2_white.png")), value: 2},
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num3_white.png")), value: 3},
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num4_white.png")), value: 4},
    {ro: RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/num5_white.png")), value: 5}
];

// 配置常量
const CONFIG = {
    RECOGNITION_TIMEOUT: 2000,      // 图像识别超时时间（毫秒）
    SLEEP_INTERVAL: 500,            // 循环间隔时间（毫秒）
    UI_DELAY: 1500,                 // UI操作延迟时间（毫秒）
    MAP_ZOOM_LEVEL: 6,              // 地图缩放级别

    // 点击坐标
    COORDINATES: {
        MAP_SWITCH: {x: 1840, y: 1020},    // 地图右下角切换按钮
        MONDSTADT: {x: 1420, y: 180},      // 蒙德选择按钮
        AVOID_SELECTION: {x: 1090, y: 450}  // 避免选中效果的点击位置
    },

    // OCR识别区域配置
    OCR_REGIONS: {
        ORIGINAL_RESIN: {width: 200, height: 40},
        CONDENSED_RESIN: {width: 90, height: 40},
        OTHER_RESIN: {width: 0, height: 60}  // width会根据图标宽度动态设置
    }
};

// 树脂数量存储
let resinCounts = {
    original: 0,    // 原粹树脂数量
    condensed: 0,   // 浓缩树脂数量
    transient: 0,   // 须臾树脂数量
    fragile: 0      // 脆弱树脂数量
};

// ==================== 工具函数 ====================

/**
 * 通用图像识别函数
 * @param {Object} recognitionObject - 识别对象
 * @param {number} timeout - 超时时间（毫秒）
 * @returns {Object|null} 识别结果或null
 */
async function recognizeImage(recognitionObject, timeout = CONFIG.RECOGNITION_TIMEOUT) {
    const startTime = Date.now();

    while (Date.now() - startTime < timeout) {
        try {
            // 直接链式调用，不保存gameRegion变量，避免内存管理问题
            const imageResult = captureGameRegion().find(recognitionObject);
            if (imageResult.isExist()) {
                return imageResult;
            }
        } catch (error) {
            log.error(`识别图像时发生异常: ${error.message}`);
        }
        await sleep(CONFIG.SLEEP_INTERVAL);
    }

    log.warn(`经过多次尝试，仍然无法识别图像`);
    return null;
}

/**
 * 在指定区域内识别数字图片
 * @param {Object} ocrRegion - OCR识别区域
 * @param {Array} numberIcons - 数字图标数组
 * @param {string} logPrefix - 日志前缀
 * @returns {number|null} 识别到的数字或null
 */
async function recognizeNumberInRegion(ocrRegion, numberIcons, logPrefix = "") {
    try {
        for (const numObj of numberIcons) {
            try {
                // 直接链式调用，避免内存管理问题
                const numResult = captureGameRegion().find(numObj.ro);
                if (numResult && isPointInRegion(numResult, ocrRegion)) {
                    log.info(`${logPrefix}通过图片识别到数字: ${numObj.value}`);
                    return numObj.value;
                }
            } catch (error) {
                log.error(`${logPrefix}识别数字图片时发生异常: ${error.message}`);
            }
        }
    } catch (error) {
        log.error(`${logPrefix}识别数字区域时发生异常: ${error.message}`);
    }
    return null;
}

/**
 * 普通数字图片识别函数
 * @param {Object} ocrRegion - OCR识别区域
 * @returns {number|null} 识别到的数字或null
 */
async function recognizeNumberByImage(ocrRegion) {
    return await recognizeNumberInRegion(ocrRegion, NUMBER_ICONS, "普通数字 - ");
}

/**
 * 白色数字图片识别函数（用于浓缩树脂）
 * @param {Object} ocrRegion - OCR识别区域
 * @returns {number|null} 识别到的数字或null
 */
async function recognizeWhiteNumberByImage(ocrRegion) {
    return await recognizeNumberInRegion(ocrRegion, WHITE_NUMBER_ICONS, "白色数字 - ");
}

/**
 * 检查点是否在指定区域内
 * @param {Object} point - 点坐标 {x, y}
 * @param {Object} region - 区域 {x, y, width, height}
 * @returns {boolean} 是否在区域内
 */
function isPointInRegion(point, region) {
    return point.x >= region.x &&
        point.x <= region.x + region.width &&
        point.y >= region.y &&
        point.y <= region.y + region.height;
}

/**
 * 通过OCR识别数字
 * @param {Object} ocrRegion - OCR识别区域
 * @param {RegExp} pattern - 匹配模式
 * @returns {number|null} 识别到的数字或null
 */
async function recognizeNumberByOCR(ocrRegion, pattern) {
    let resList = null;
    let captureRegion = null;
    try {
        const ocrRo = RecognitionObject.ocr(ocrRegion.x, ocrRegion.y, ocrRegion.width, ocrRegion.height);
        captureRegion = captureGameRegion();
        resList = captureRegion.findMulti(ocrRo);

        if (!resList || resList.length === 0) {
            log.warn("OCR未识别到任何文本");
            return null;
        }

        for (const res of resList) {
            if (!res || !res.text) {
                continue;
            }

            const numberMatch = res.text.match(pattern);
            if (numberMatch) {
                const number = parseInt(numberMatch[1] || numberMatch[0]);
                if (!isNaN(number)) {
                    return number;
                }
            }
        }
        return null;
    } catch (error) {
        log.error(`OCR识别时发生异常: ${error.message}`);
        return null;
    } finally {
        if (resList && typeof resList.dispose === 'function') {
            resList.dispose();
        }
        if (captureRegion && typeof captureRegion.dispose === 'function') {
            captureRegion.dispose();
        }
    }
}

// ==================== 树脂计数函数 ====================

/**
 * 统计原粹树脂数量
 * @returns {number} 原粹树脂数量
 */
/**
 * 统计原粹树脂数量
 * @returns {number} 原粹树脂数量
 */
async function countOriginalResin(tryOriginalMode,opToMainUi,openMap) {
    if (tryOriginalMode) {
        log.info("尝试使用原始模式");
        return await countOriginalResinBackup()
    } else {
        log.info('尝试使用优化模式');
        let ocrPhysical = await physical.ocrPhysical(opToMainUi,openMap);
        await sleep(600)
        if (ocrPhysical && ocrPhysical.ok) {
            return ocrPhysical.remainder;
        } else {
            log.error(`ocrPhysical error`);
            throw new Error("ocrPhysical error");
        }
    }
}

async function countOriginalResinBackup() {
    const originalResin = await recognizeImage(RESIN_ICONS.ORIGINAL);
    if (!originalResin) {
        log.warn(`未找到原粹树脂图标`);
        return 0;
    }

    const ocrRegion = {
        x: originalResin.x,
        y: originalResin.y,
        width: CONFIG.OCR_REGIONS.ORIGINAL_RESIN.width,
        height: CONFIG.OCR_REGIONS.ORIGINAL_RESIN.height
    };

    // 匹配 xxx/200 格式中的第一个数字（1-3位）
    const count = await recognizeNumberByOCR(ocrRegion, /(\d{1,3})\/\d+/);
    if (count !== null) {
        log.info(`原粹树脂数量: ${count}`);
        return count;
    }

    log.warn(`未能识别原粹树脂数量`);
    return 0;
}

/**
 * 统计浓缩树脂数量
 * @returns {number} 浓缩树脂数量
 */
async function countCondensedResin() {
    const condensedResin = await recognizeImage(RESIN_ICONS.CONDENSED);
    if (!condensedResin) {
        log.warn(`未找到浓缩树脂图标`);
        return 0;
    }

    const ocrRegion = {
        x: condensedResin.x,
        y: condensedResin.y,
        width: CONFIG.OCR_REGIONS.CONDENSED_RESIN.width,
        height: CONFIG.OCR_REGIONS.CONDENSED_RESIN.height
    };

    // 首先尝试OCR识别
    const ocrCount = await recognizeNumberByOCR(ocrRegion, /\d+/);
    if (ocrCount !== null) {
        log.info(`浓缩树脂数量: ${ocrCount}`);
        return ocrCount;
    }

    // OCR识别失败，尝试白色数字图片识别
    log.info(`OCR识别浓缩树脂失败，尝试白色数字图片识别`);
    const imageCount = await recognizeWhiteNumberByImage(ocrRegion);
    if (imageCount !== null) {
        log.info(`浓缩树脂数量(白色数字图片识别): ${imageCount}`);
        return imageCount;
    }

    log.info(`白色数字图片识别识别浓缩树脂失败，尝试在说明界面获取`);
    // 点击浓缩树脂打开说明界面统计
    condensedResin.click();
    await sleep(CONFIG.UI_DELAY);
    let captureRegion = captureGameRegion();
    let textList = null;
    try {
        // OCR识别整个界面的文本
        let ocrRo = RecognitionObject.Ocr(0, 0, captureRegion.width, captureRegion.height);
        textList = captureRegion.findMulti(ocrRo);

        for (const res of textList) {
            if (res.text.includes("当前拥有")) {
                const match = res.text.match(/当前拥有\s*([0-5ss])/);
                if (match && match[1]) {
                    const count = parseInt(match[1]);
                    log.info(`浓缩树脂数量(说明界面): ${count}`);
                    keyPress("ESCAPE");
                    await sleep(CONFIG.UI_DELAY);
                    return count;
                }
            }
        }
    } finally {
        if (textList && typeof textList.dispose === 'function') {
            textList.dispose();
        }
        captureRegion.dispose();
    }

    log.warn(`未能识别浓缩树脂数量`);
    return 0;
}

/**
 * 统计须臾树脂数量
 * @returns {number} 须臾树脂数量
 */
async function countTransientResin() {
    const transientResin = await recognizeImage(RESIN_ICONS.TRANSIENT);
    if (!transientResin) {
        log.warn(`未找到须臾树脂图标`);
        return 0;
    }

    const ocrRegion = {
        x: transientResin.x,
        y: transientResin.y + transientResin.height,
        width: transientResin.width,
        height: CONFIG.OCR_REGIONS.OTHER_RESIN.height
    };

    return await countResinWithFallback(ocrRegion, "须臾树脂", recognizeNumberByImage);
}

/**
 * 统计脆弱树脂数量
 * @returns {number} 脆弱树脂数量
 */
async function countFragileResin() {
    const fragileResin = await recognizeImage(RESIN_ICONS.FRAGILE);
    if (!fragileResin) {
        log.warn(`未找到脆弱树脂图标`);
        return 0;
    }

    const ocrRegion = {
        x: fragileResin.x,
        y: fragileResin.y + fragileResin.height,
        width: fragileResin.width,
        height: CONFIG.OCR_REGIONS.OTHER_RESIN.height
    };

    return await countResinWithFallback(ocrRegion, "脆弱树脂", recognizeNumberByImage);
}

/**
 * 通用树脂计数函数（带图片识别回退）
 * @param {Object} ocrRegion - OCR识别区域
 * @param {string} resinType - 树脂类型名称
 * @param {Function} fallbackFunction - 回退识别函数
 * @returns {number} 树脂数量
 */
async function countResinWithFallback(ocrRegion, resinType, fallbackFunction) {
    // 首先尝试OCR识别
    const ocrCount = await recognizeNumberByOCR(ocrRegion, /\d+/);
    if (ocrCount !== null) {
        log.info(`${resinType}数量: ${ocrCount}`);
        return ocrCount;
    }

    // OCR识别失败，尝试图片识别
    log.info(`OCR识别${resinType}失败，尝试图片识别`);
    const imageCount = await fallbackFunction(ocrRegion);
    if (imageCount !== null) {
        log.info(`${resinType}数量(图片识别): ${imageCount}`);
        return imageCount;
    }

    log.warn(`未能识别${resinType}数量`);
    return 0;
}

// ==================== UI操作函数 ====================

/**
 * 打开并设置地图界面
 */
async function openMap() {
    log.info("打开地图界面");
    keyPress("M");
    await sleep(CONFIG.UI_DELAY);

    // 切换到国家选择界面
    // click(CONFIG.COORDINATES.MAP_SWITCH.x, CONFIG.COORDINATES.MAP_SWITCH.y);
    // await sleep(CONFIG.UI_DELAY);

    // 选择蒙德
    // click(CONFIG.COORDINATES.MONDSTADT.x, CONFIG.COORDINATES.MONDSTADT.y);
    // await sleep(CONFIG.UI_DELAY);
    // await switchtoCountrySelection(CONFIG.COORDINATES.MONDSTADT.x, CONFIG.COORDINATES.MONDSTADT.y)

    // 设置地图缩放级别，排除识别干扰
    await genshin.setBigMapZoomLevel(CONFIG.MAP_ZOOM_LEVEL);
    log.info("地图界面设置完成");
}

/**
 * 切换到国家选择界面的异步函数
 * 通过点击指定坐标并等待界面加载来完成切换操作
 */
async function switchtoCountrySelection(x, y) {
    // 切换到国家选择界面
    click(CONFIG.COORDINATES.MAP_SWITCH.x, CONFIG.COORDINATES.MAP_SWITCH.y);
    await sleep(CONFIG.UI_DELAY);
    click(x, y);
    await sleep(CONFIG.UI_DELAY);
}

/**
 * 打开补充树脂界面
 */
async function openReplenishResinUi() {
    log.info("尝试打开补充树脂界面");
    const replenishResinButton = await recognizeImage(RESIN_ICONS.REPLENISH_BUTTON);
    if (replenishResinButton) {
        replenishResinButton.Click();
        log.info("成功打开补充树脂界面");
    } else {
        log.warn("未找到补充树脂按钮");
    }
}

/**
 * 显示统计结果并发送通知
 * @param {Object} results - 统计结果对象
 */
function displayResults(results) {
    const resultText = `原粹:${results.original} 浓缩:${results.condensed} 须臾:${results.transient} 脆弱:${results.fragile}`;

    log.info(`============ 树脂统计结果 ============`);
    log.info(`原粹树脂数量: ${results.original}`);
    log.info(`浓缩树脂数量: ${results.condensed}`);
    log.info(`须臾树脂数量: ${results.transient}`);
    log.info(`脆弱树脂数量: ${results.fragile}`);
    log.info(`====================================`);
}

// ==================== 主要功能函数 ====================

/**
 * 统计所有树脂数量的主函数
 * @returns {Object} 包含所有树脂数量的对象
 */
this.countAllResin = async function () {
    try {
        setGameMetrics(1920, 1080, 1);
        log.info("开始统计树脂数量");

        await genshin.returnMainUi();
        await sleep(CONFIG.UI_DELAY);

        // 打开地图界面统计原粹/浓缩树脂
        await openMap();
        await sleep(CONFIG.UI_DELAY);
        let tryPass = true;
        try {
            log.info("[开始]统计补充树脂界面中的树脂");
            resinCounts.original = await countOriginalResin(false,false);
            moveMouseTo(CONFIG.COORDINATES.AVOID_SELECTION.x, CONFIG.COORDINATES.AVOID_SELECTION.y)
            await sleep(500);
            resinCounts.transient = await countTransientResin();
            resinCounts.fragile = await countFragileResin();
            log.info("[完成]统计补充树脂界面中的树脂");
            // 点击避免选中效果影响统计
            click(CONFIG.COORDINATES.AVOID_SELECTION.x, CONFIG.COORDINATES.AVOID_SELECTION.y);
        } catch (e) {
            tryPass = false
        }
        await sleep(CONFIG.UI_DELAY);
        log.info("开始统计地图界面中的树脂");
        if (!tryPass){
            // 如果第一次尝试失败，则切换到蒙德
            await switchtoCountrySelection(CONFIG.COORDINATES.MONDSTADT.x, CONFIG.COORDINATES.MONDSTADT.y)
            resinCounts.original = await countOriginalResin(!tryPass);
        }
        resinCounts.condensed = await countCondensedResin();
        if (!tryPass) {
            // 打开补充树脂界面统计须臾/脆弱树脂
            await openReplenishResinUi();
            await sleep(CONFIG.UI_DELAY);

            // 点击避免选中效果影响统计
            click(CONFIG.COORDINATES.AVOID_SELECTION.x, CONFIG.COORDINATES.AVOID_SELECTION.y);
            await sleep(500);

            log.info("开始统计补充树脂界面中的树脂");
            resinCounts.transient = await countTransientResin();
            resinCounts.fragile = await countFragileResin();
        }
        // 显示结果
        displayResults(resinCounts);

        // 返回主界面
        await genshin.returnMainUi();
        await sleep(CONFIG.UI_DELAY);

        log.info("树脂统计完成");
        return {
            originalResinCount: resinCounts.original,
            condensedResinCount: resinCounts.condensed,
            transientResinCount: resinCounts.transient,
            fragileResinCount: resinCounts.fragile
        };

    } catch (error) {
        log.error(`统计树脂数量时发生异常: ${error.message}`);
        throw error;
    }
}

/**
 * @returns {Object} 包含可刷取次数的对象
 * {
 *     count: number,
 *     originalResinTimes: number,
 *     condensedResinTimes: number,
 *     transientResinTimes: number,
 *     fragileResinTimes: number
 * }
 */
this.calCountByResin = async function () {
    try {
        let countResult = await this.countAllResin();
        let count = 0;
        // 计算可刷取次数
        // 1. 原粹树脂：优先消耗40/次，不满40消耗20/次，不满20不消耗
        let originalResinTimes = 0;
        let remainingOriginalResin = countResult.originalResinCount;
        
        // 先计算40树脂的次数
        if (remainingOriginalResin >= 40) {
            const times40 = Math.floor(remainingOriginalResin / 40);
            originalResinTimes += times40;
            remainingOriginalResin = remainingOriginalResin - (times40 * 40);
        }
        
        // 再计算20树脂的次数
        if (remainingOriginalResin >= 20) {
            const times20 = Math.floor(remainingOriginalResin / 20);
            originalResinTimes += times20;
            remainingOriginalResin = remainingOriginalResin - (times20 * 20);
        }
        
        log.info(`原粹树脂可刷取次数: ${originalResinTimes}`);
        
        // 2. 浓缩树脂：每个计算为1次
        let condensedResinTimes = countResult.condensedResinCount;
        log.info(`浓缩树脂可刷取次数: ${condensedResinTimes}`);

        // 3. 须臾树脂：检查设置是否开启
        let transientResinTimes = 0;
        if (settings.useTransientResin) {
            transientResinTimes = countResult.transientResinCount;
            log.info(`须臾树脂可刷取次数: ${transientResinTimes}`);
        } else {
            log.info(`须臾树脂未开启使用，跳过计算`);
        }

        // 4. 脆弱树脂：检查设置是否开启
        let fragileResinTimes = 0;
        if (settings.useFragileResin) {
            fragileResinTimes = countResult.fragileResinCount;
            log.info(`脆弱树脂可刷取次数: ${fragileResinTimes}`);
        } else {
            log.info(`脆弱树脂未开启使用，跳过计算`);
        }

        // 计算总次数
        count = originalResinTimes + condensedResinTimes + transientResinTimes + fragileResinTimes;
        log.info(`总计可刷取次数: ${count}`);

        let result = {
            count: count,
            originalResinTimes: originalResinTimes,
            condensedResinTimes: condensedResinTimes,
            transientResinTimes: transientResinTimes,
            fragileResinTimes: fragileResinTimes
        }

        return result;
    } catch (error) {
        log.error(`统计树脂数量时发生异常: ${error.message}`);
        throw error;
    }
}