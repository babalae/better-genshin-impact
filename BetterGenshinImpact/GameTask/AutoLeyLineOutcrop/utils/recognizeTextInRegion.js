/**
 * 识别战斗结果
 * @param {number} timeout - 超时时间
 * @returns {Promise<boolean>} 战斗是否成功
 */
this.recognizeTextInRegion =
async function (timeout) {
    return new Promise((resolve, reject) => {
        (async () => {
            try {
                let startTime = Date.now();
                let noTextCount = 0;
                const successKeywords = ["挑战达成", "战斗胜利", "挑战成功"];
                const failureKeywords = ["挑战失败"];

                // 循环检测直到超时
                while (Date.now() - startTime < timeout) {
                    let captureRegion = null;
                    try {
                        captureRegion = captureGameRegion();
                        let result = captureRegion.find(ocrRo1);
                        let text = result.text;

                        // 检查成功关键词
                        for (let keyword of successKeywords) {
                            if (text.includes(keyword)) {
                                log.debug("检测到战斗成功关键词: {0}", keyword);
                                captureRegion.dispose();
                                resolve(true);
                                return;
                            }
                        }

                        // 检查失败关键词
                        for (let keyword of failureKeywords) {
                            if (text.includes(keyword)) {
                                log.debug("检测到战斗失败关键词: {0}", keyword);
                                captureRegion.dispose();
                                resolve(false);
                                return;
                            }
                        }

                        let foundText = recognizeFightText(captureRegion);
                        if (!foundText) {
                            noTextCount++;
                            log.info(`检测到可能离开战斗区域，当前计数: ${noTextCount}`);

                            if (noTextCount >= 10) {
                                log.warn("已离开战斗区域");
                                resolve(false);
                                return;
                            }
                        }
                        else {
                            noTextCount = 0; // 重置计数
                        }
                    }
                    catch (error) {
                        log.error("OCR过程中出错: {0}", error);
                    }
                    finally {
                        if (captureRegion) {
                            captureRegion.dispose();
                        }
                    }
                    await sleep(1000); // 检查间隔
                }

                log.warn("在超时时间内未检测到战斗结果");
                resolve(false);
            } catch (error) {
                reject(error);
            }
        })();
    });
}