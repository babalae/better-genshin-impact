/**
 * 带验证的单击函数
 * @param {number} x - X坐标
 * @param {number} y - Y坐标
 * @param {string} targetText - 需要验证消失的目标文字
 * @param {number} maxRetries - 最大重试次数，默认为10
 * @returns {Promise<boolean>} 是否成功
 */
this.clickWithVerification = async function(x, y, targetText, maxRetries = 20) {
    for (let i = 0; i < maxRetries; i++) {
        keyUp("LBUTTON");
        click(x, y);
        await sleep(400); 
        
        // 验证目标文字是否消失
        let captureRegion = captureGameRegion();
        let resList = captureRegion.findMulti(ocrRoThis);
        captureRegion.dispose();
        let textFound = false;
        
        if (resList && resList.count > 0) {
            for (let j = 0; j < resList.count; j++) {
                if (resList[j].text.includes(targetText)) {
                    textFound = true;
                    break;
                }
            }
        }
        
        // 如果文字消失了，说明点击成功
        if (!textFound) {
            return true;
        }
    }
    
    log.warn(`经过${maxRetries}次点击，文字"${targetText}"仍未消失`);
    return false;
}

/**
 * 验证是否在奖励界面
 * 使用OCR识别"地脉之花"或"激活地脉之花"文字，不受分辨率影响
 * @returns {Promise<boolean>}
 */
this.verifyRewardPage = async function() {
    let captureRegion = null;
    
    try {
        captureRegion = captureGameRegion();
        
        // 使用OCR识别上半区域
        let ocrRo = RecognitionObject.Ocr(0, 0, captureRegion.width, captureRegion.height / 2);
        let textList = captureRegion.findMulti(ocrRo);
        
        let isValid = false;
        if (textList && textList.count > 0) {
            for (let i = 0; i < textList.count; i++) {
                let text = textList[i].text;
                // 识别关键文字
                if (text.includes("激活地脉之花") ||
                    text.includes("选择激活方式")) {
                    isValid = true;
                    log.info(`奖励界面验证: 成功（识别到文字: "${text}"）`);
                    break;
                }
            }
        }
        
        // 已注释：减少日志输出
        // if (!isValid) {
        //     log.info(`奖励界面验证: 失败（未识别到关键文字）`);
        // }
        
        return isValid;
    } catch (error) {
        log.error(`验证奖励界面失败: ${error.message}`);
        return false;
    } finally {
        if (captureRegion) {
            captureRegion.dispose();
        }
    }
}

/**
 * 检查原粹树脂是否耗尽（通过OCR识别"补充"文字）
 * 如果原粹树脂耗尽，第一个按钮会变成"补充"按钮
 * @returns {Promise<boolean>}
 */
async function checkOriginalResinEmpty() {
    let captureRegion = null;
    
    try {
        captureRegion = captureGameRegion();
        
        // 使用OCR识别"补充"文字
        let ocrRo = RecognitionObject.Ocr(0, 0, captureRegion.width, captureRegion.height);
        let textList = captureRegion.findMulti(ocrRo);
        
        if (textList && textList.count > 0) {
            for (let i = 0; i < textList.count; i++) {
                let text = textList[i].text;
                if (text.includes("补充")) {
                    log.warn("检测到补充文字，原粹树脂已耗尽");
                    return true;
                }
            }
        }
        
        return false;
    } catch (error) {
        log.error(`检查原粹树脂状态失败: ${error.message}`);
        return false;
    } finally {
        if (captureRegion) {
            captureRegion.dispose();
        }
    }
}

/**
 * 查找并排序所有使用按钮（通过OCR识别"使用"文字）
 * 注意：如果原粹树脂耗尽，第一个位置是"补充"按钮，不会被识别为"使用"按钮
 * @returns {Promise<Array>}
 */
async function findAndSortUseButtons() {
    let captureRegion = null;
    
    try {
        captureRegion = captureGameRegion();
        
        // 使用OCR识别所有"使用"文字
        let ocrRo = RecognitionObject.Ocr(0, 0, captureRegion.width, captureRegion.height);
        let textList = captureRegion.findMulti(ocrRo);
        
        if (!textList || textList.count === 0) {
            log.warn("未找到任何文本");
            return [];
        }
        
        // 查找只包含"使用"两个字的文本（真正的按钮）
        let buttons = [];
        for (let i = 0; i < textList.count; i++) {
            let textRegion = textList[i];
            let text = textRegion.text.trim();  // 去除首尾空格
            
            // 只匹配恰好是"使用"的文本，排除描述性文字
            if (text === "使用") {
                // 按钮就在文本位置（OCR识别到的就是按钮本身）
                let buttonX = Math.round(textRegion.x + textRegion.width / 2);
                let buttonY = Math.round(textRegion.y + textRegion.height / 2);
                let textY = textRegion.y;  // 提前保存Y坐标值
                let textContent = textRegion.text;  // 提前保存文本内容
                
                // 创建虚拟按钮Region对象（不保存textRegion引用，避免dispose后访问失败）
                let virtualButton = {
                    index: buttons.length,
                    region: {
                        x: buttonX,
                        y: buttonY,
                        click: function() {
                            click(buttonX, buttonY);
                        }
                    },
                    x: buttonX,
                    y: textY,  // 用文本的Y坐标进行排序
                    text: textContent  // 保存文本内容而非引用
                };
                
                buttons.push(virtualButton);
            }
        }
        
        if (buttons.length === 0) {
            log.warn("未找到包含'使用'的文本");
            return [];
        }
        
        // 按Y坐标排序
        buttons.sort((a, b) => a.y - b.y);
        
        log.info(`找到 ${buttons.length} 个使用按钮`);
        
        return buttons;
    } catch (error) {
        log.error(`查找使用按钮失败: ${error.message}`);
        return [];
    } finally {
        if (captureRegion) {
            captureRegion.dispose();
        }
    }
}

/**
 * 分析树脂选项并决定使用哪个
 * @param {Array} sortedButtons - 排序后的使用按钮数组
 * @param {boolean} isOriginalResinEmpty - 原粹树脂是否耗尽
 * @returns {Promise<Object|null>}
 */
async function analyzeResinOptions(sortedButtons, isOriginalResinEmpty) {
    let captureRegion = null;
    
    try {
        captureRegion = captureGameRegion();
        
        // OCR识别整个界面的文本
        let ocrRo = RecognitionObject.Ocr(0, 0, captureRegion.width, captureRegion.height);
        let textList = captureRegion.findMulti(ocrRo);
        
        if (!textList || textList.count === 0) {
            log.warn("OCR未识别到任何文本");
            return null;
        }

        // 收集所有识别到的文本
        let allTexts = [];
        for (let i = 0; i < textList.count; i++) {
            allTexts.push({
                text: textList[i].text,
                y: textList[i].y
            });
        }
        

        // 检测是否有双倍/多倍产出
        let hasDoubleReward = allTexts.some(t => 
            t.text.includes("双倍") || 
            t.text.includes("2倍产出") || 
            t.text.includes("2倍")
        );
        
        // 只在有双倍产出时输出
        if (hasDoubleReward) {
            log.info("检测到双倍产出");
        }

        // 识别树脂类型（注意：如果原粹树脂耗尽，应该忽略这些识别）
        let hasOriginalResin20 = !isOriginalResinEmpty && allTexts.some(t => 
            (t.text.includes("20") && t.text.includes("原粹树脂")) ||
            (t.text.includes("20个") && t.text.includes("原粹树脂"))
        );
        
        let hasOriginalResin40 = !isOriginalResinEmpty && allTexts.some(t => 
            (t.text.includes("40") && t.text.includes("原粹树脂")) ||
            (t.text.includes("40个") && t.text.includes("原粹树脂"))
        );
        
        let hasCondensedResin = allTexts.some(t => 
            t.text.includes("浓缩树脂") || t.text.includes("浓缩")
        );
        
        let hasTransientResin = allTexts.some(t => 
            t.text.includes("须臾树脂") || t.text.includes("须臾")
        );
        
        let hasFragileResin = allTexts.some(t => 
            t.text.includes("脆弱树脂") || t.text.includes("脆弱")
        );
        
        let hasPrimogems = allTexts.some(t => 
            t.text.includes("原石") && t.text.includes("3次")
        );
        
        // 输出识别到的树脂类型（调试用）
        log.info(`识别到的树脂类型 - 原粹20:${hasOriginalResin20}, 原粹40:${hasOriginalResin40}, 浓缩:${hasCondensedResin}, 须臾:${hasTransientResin}, 脆弱:${hasFragileResin}, 原石:${hasPrimogems}, 双倍:${hasDoubleReward}`);

        // 决策逻辑（根据原粹树脂是否耗尽，决策不同）
        let choice = null;

        if (isOriginalResinEmpty) {
            // ===== 原粹树脂耗尽的情况 =====
            // 此时第一个"使用"按钮对应的是浓缩/须臾/脆弱树脂
            log.warn("原粹树脂已耗尽，检测是否有其他可用树脂");
            
            if (hasCondensedResin && sortedButtons.length >= 1) {
                choice = {
                    type: "使用1个浓缩树脂（原粹耗尽）",
                    button: sortedButtons[0],
                    buttonIndex: 0
                };
            } else if (hasTransientResin && sortedButtons.length >= 1 && settings.useTransientResin) {
                choice = {
                    type: "使用1个须臾树脂（原粹耗尽）",
                    button: sortedButtons[0],
                    buttonIndex: 0
                };
            } else if (hasFragileResin && sortedButtons.length >= 1 && settings.useFragileResin) {
                choice = {
                    type: "使用1个脆弱树脂（原粹耗尽）",
                    button: sortedButtons[0],
                    buttonIndex: 0
                };
            } else {
                // 输出详细的调试信息
                if (hasTransientResin && !settings.useTransientResin) {
                    log.warn(`原粹树脂耗尽，检测到须臾树脂但配置禁止使用（settings.useTransientResin=${settings.useTransientResin}）`);
                } else if (hasFragileResin && !settings.useFragileResin) {
                    log.warn(`原粹树脂耗尽，检测到脆弱树脂但配置禁止使用（settings.useFragileResin=${settings.useFragileResin}）`);
                } else {
                    log.warn(`原粹树脂耗尽且无其他可用树脂（浓缩:${hasCondensedResin}, 须臾:${hasTransientResin}, 脆弱:${hasFragileResin}, 原石:${hasPrimogems}）`);
                }
                return null;
            }
        } else {
            // ===== 原粹树脂充足的情况 =====
            // 第一个"使用"按钮对应原粹树脂
            // 第二个"使用"按钮对应浓缩/须臾/脆弱树脂
            
            // 优先级1: 如果有双倍产出，优先使用原粹树脂
            if (hasDoubleReward && (hasOriginalResin20 || hasOriginalResin40)) {
                // 如果当前是20个原粹树脂，先尝试切换到40个
                if (hasOriginalResin20 && !hasOriginalResin40) {
                    let switchSuccess = await trySwitch20To40Resin();
                    if (switchSuccess) {
                        choice = {
                            type: "使用40个原粹树脂（从20切换，双倍产出）",
                            button: sortedButtons[0],
                            buttonIndex: 0
                        };
                    } else {
                        choice = {
                            type: "使用20个原粹树脂（双倍产出）",
                            button: sortedButtons[0],
                            buttonIndex: 0
                        };
                    }
                } else {
                    choice = {
                        type: hasOriginalResin40 ? "使用40个原粹树脂（双倍产出）" : "使用20个原粹树脂（双倍产出）",
                        button: sortedButtons[0],
                        buttonIndex: 0
                    };
                }
            }
            // 优先级2: 优先使用浓缩树脂
            else if (hasCondensedResin && sortedButtons.length >= 2) {
                choice = {
                    type: "使用1个浓缩树脂",
                    button: sortedButtons[1],
                    buttonIndex: 1
                };
            }
            // 优先级3: 使用须臾树脂
            else if (hasTransientResin && settings.useTransientResin && sortedButtons.length >= 2) {
                choice = {
                    type: "使用1个须臾树脂",
                    button: sortedButtons[1],
                    buttonIndex: 1
                };
            }
            // 优先级4: 使用原粹树脂
            else if (hasOriginalResin20 || hasOriginalResin40) {
                // 如果当前是20个原粹树脂，先尝试切换到40个
                if (hasOriginalResin20 && !hasOriginalResin40) {
                    let switchSuccess = await trySwitch20To40Resin();
                    if (switchSuccess) {
                        choice = {
                            type: "使用40个原粹树脂（从20切换）",
                            button: sortedButtons[0],
                            buttonIndex: 0
                        };
                    } else {
                        choice = {
                            type: "使用20个原粹树脂",
                            button: sortedButtons[0],
                            buttonIndex: 0
                        };
                    }
                } else {
                    choice = {
                        type: hasOriginalResin40 ? "使用40个原粹树脂" : "使用20个原粹树脂",
                        button: sortedButtons[0],
                        buttonIndex: 0
                    };
                }
            }
            // 优先级5: 如果配置允许，使用脆弱树脂
            else if (hasFragileResin && settings.useFragileResin && sortedButtons.length >= 2) {
                choice = {
                    type: "使用1个脆弱树脂",
                    button: sortedButtons[1],
                    buttonIndex: 1
                };
            }
            // 默认: 点击第一个按钮（原粹树脂）
            else if (sortedButtons.length >= 1) {
                // 尝试切换到40个原粹树脂（如果当前是20个）
                if (hasOriginalResin20 && !hasOriginalResin40) {
                    let switchSuccess = await trySwitch20To40Resin();
                    choice = {
                        type: switchSuccess ? "默认使用40个原粹树脂（从20切换）" : "默认使用20个原粹树脂",
                        button: sortedButtons[0],
                        buttonIndex: 0
                    };
                } else {
                    choice = {
                        type: "默认使用原粹树脂",
                        button: sortedButtons[0],
                        buttonIndex: 0
                    };
                }
            }
        }

        return choice;

    } catch (error) {
        log.error(`分析树脂选项失败: ${error.message}`);
        return null;
    } finally {
        if (captureRegion) {
            captureRegion.dispose();
        }
    }
}

/**
 * 尝试将20个原粹树脂切换到40个原粹树脂
 * @returns {Promise<boolean>} 是否成功切换
 */
async function trySwitch20To40Resin() {
    let switchButtonIcon = null;
    let switchButtonRo = null;
    let currentCaptureRegion = null;
    let newCaptureRegion = null;
    
    try {
        log.info("检测到20个原粹树脂，尝试切换到40个");
        
        currentCaptureRegion = captureGameRegion();
        
        // 检测切换按钮
        switchButtonIcon = file.ReadImageMatSync("assets/icon/switch_button.png");
        switchButtonRo = RecognitionObject.TemplateMatch(switchButtonIcon);
        switchButtonRo.threshold = 0.7;  // 设置合适的阈值
        
        // 查找切换按钮
        let switchButtonPos = currentCaptureRegion.find(switchButtonRo);
        
        if (!switchButtonPos || switchButtonPos.isEmpty()) {
            log.info("未找到切换按钮（树脂不足40或按钮不可用），保持使用20个原粹树脂");
            return false;
        }
        
        // 找到可用的切换按钮，点击切换
        log.info(`找到切换按钮，点击切换到40个原粹树脂`);
        switchButtonPos.click();
        await sleep(800);
        
        // 验证是否切换成功
        newCaptureRegion = captureGameRegion();
        let ocrRo = RecognitionObject.Ocr(0, 0, newCaptureRegion.width, newCaptureRegion.height);
        let textList = newCaptureRegion.findMulti(ocrRo);
        
        if (textList && textList.count > 0) {
            for (let i = 0; i < textList.count; i++) {
                let text = textList[i].text;
                if ((text.includes("40") && text.includes("原粹")) || 
                    (text.includes("40个") && text.includes("树脂"))) {
                    log.info("成功切换到40个原粹树脂");
                    return true;
                }
            }
        }
        
        log.warn("点击切换按钮后，未能确认切换到40个原粹树脂");
        return false;
        
    } catch (error) {
        log.error(`切换树脂数量失败: ${error.message}`);
        return false;
    } finally {
        if (currentCaptureRegion) {
            currentCaptureRegion.dispose();
        }
        if (newCaptureRegion) {
            newCaptureRegion.dispose();
        }
        if (switchButtonIcon) {
            switchButtonIcon.dispose();
        }
        switchButtonRo = null;
    }
}

/**
 * 切换回战斗队伍
 * @returns {Promise<void>}
 */
async function switchBackToCombatTeam() {
    try {
        log.info("切换回战斗队伍");
        await sleep(500);
        const switchSuccess = await switchTeam(settings.team);
        if (!switchSuccess) {
            log.warn("切换队伍可能失败");
        }
    } catch (error) {
        log.error(`切换队伍失败: ${error.message}`);
    }
}

/**
 * 确保退出奖励界面
 * 循环检测并退出，直到确认不在奖励界面
 * @returns {Promise<void>}
 */
this.ensureExitRewardPage = async function() {
    const MAX_ATTEMPTS = 5;  // 最多尝试5次
    let attempts = 0;
    
    try {
        log.info("检查是否需要退出奖励界面");
        
        while (attempts < MAX_ATTEMPTS) {
            attempts++;
            
            // 检测是否在奖励界面
            let isInRewardPage = await this.verifyRewardPage();
            
            if (!isInRewardPage) {
                log.info("已确认不在奖励界面");
                return;
            }
            
            // 还在奖励界面，按ESC退出
            log.info(`检测到仍在奖励界面，按ESC退出 (第${attempts}次)`);
            keyPress("VK_ESCAPE");
            await sleep(800);  // 等待界面关闭动画
        }
        
        // 超过最大尝试次数
        log.warn(`已尝试${MAX_ATTEMPTS}次退出奖励界面，可能仍在界面中`);
        
    } catch (error) {
        log.error(`退出奖励界面时出错: ${error.message}`);
    }
}

/**
 * 尝试领取地脉花奖励（图像识别+OCR混合版本）
 * @param {number} retryCount - 重试次数
 * @returns {Promise<boolean>}
 */
this.attemptReward = async function (retryCount = 0) {
    const MAX_RETRY = 3;
    if (retryCount >= MAX_RETRY) {
        throw new Error("超过最大重试次数，领取奖励失败");
    }

    log.info("开始领取地脉奖励");
    keyPress("F");
    await sleep(800);

    // 步骤1: 验证是否在奖励界面
    if (!await this.verifyRewardPage()) {
        log.warn("当前不在奖励界面，尝试重试");
        await genshin.returnMainUi();
        await sleep(1000);
        await autoNavigateToReward();
        return await this.attemptReward(++retryCount);
    }

    let isOriginalResinEmpty = false;
    let sortedButtons = [];
    let resinChoice = null;

    try {
        // 步骤2: 检查原粹树脂是否耗尽（通过"补充"按钮）
        isOriginalResinEmpty = await checkOriginalResinEmpty();
        
        // 步骤3: 识别所有使用按钮并排序
        sortedButtons = await findAndSortUseButtons();
        
        if (sortedButtons.length === 0) {
            log.error("未找到任何使用按钮");
            keyPress("VK_ESCAPE");
            await sleep(500);
            await this.ensureExitRewardPage();
            return false;
        }

        // 步骤4: 根据原粹树脂状态调整决策逻辑
        resinChoice = await analyzeResinOptions(sortedButtons, isOriginalResinEmpty);
        
        if (!resinChoice) {
            // 已在 analyzeResinOptions 中输出详细错误信息，这里不再重复
            keyPress("VK_ESCAPE");
            await sleep(500);
            await this.ensureExitRewardPage();
            return false;
        }

    } catch (error) {
        log.error(`处理奖励界面时出错: ${error.message}`);
        keyPress("VK_ESCAPE");
        await sleep(500);
        await this.ensureExitRewardPage();
        return false;
    }

    // 步骤5: 点击对应的使用按钮
    log.info(`选择: ${resinChoice.type}，点击按钮 (X=${resinChoice.button.x}, Y=${resinChoice.button.y})`);
    resinChoice.button.region.click();
    await sleep(1000);

    // 步骤6: 如果需要切换回战斗队伍
    if (settings.friendshipTeam) {
        await switchBackToCombatTeam();
    }

    // 等待领奖动画/道具到账
    await sleep(1200);

    // 确保完全退出奖励界面
    await this.ensureExitRewardPage();
    
    return true;
}