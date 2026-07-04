// Party Setup
const QuickSetupButtonRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Quick Setup Button.png"), 1100, 900, 400, 180);
const ConfigureTeamButtonRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Configure Team Button.png"), 0, 900, 200, 180);
const ConfirmDeployButtonRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Confirm Deploy Button.png"), 0, 900, 1920, 180);
// Slider
const LeftSliderTopRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Slider Top.png"), 650, 50, 100, 100);
const LeftSliderBottomRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Slider Bottom.png"), 650, 100, 100, 900);
const MiddleSliderTopRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Slider Top.png"), 1250, 50, 100, 200);
const MiddleSliderBottomRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Slider Bottom.png"), 1250, 100, 100, 900);
const RightSliderTopRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Slider Top.png"), 1750, 100, 100, 100);
const RightSliderBottomRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/Slider Bottom.png"), 1750, 100, 100, 900);

// 翻页
async function pageDown(SliderBottomRo) {
    let captureRegion = captureGameRegion();
    let SliderBottom = captureRegion.find(SliderBottomRo);
    captureRegion.dispose();
    if (SliderBottom.isExist()) {
        log.info("当前页面已识别&点击完毕，向下滑动");
        // log.info("滑块当前位置:({x},{y},{h},{w})", SliderBottom.x, SliderBottom.y, SliderBottom.Width, SliderBottom.Height);
        click(Math.ceil(SliderBottom.x + SliderBottom.Width / 2), Math.ceil(SliderBottom.y + SliderBottom.Height * 2));
        await moveMouseTo(0, 0);
        await sleep(100);
    }
}

//	滑条顶端
async function pageTop(SliderTopRo) {
    let captureRegion = captureGameRegion();
    let SliderTop = captureRegion.find(SliderTopRo);
    captureRegion.dispose();
    if (SliderTop.isExist()) {
        log.info("识别到滑条顶端位置:({x},{y},{h},{w})", SliderTop.x, SliderTop.y, SliderTop.Width, SliderTop.Height);
        await moveMouseTo(Math.ceil(SliderTop.x + SliderTop.Width / 2), Math.ceil(SliderTop.y + SliderTop.Height * 1));
        leftButtonDown();
        await sleep(1000);
        leftButtonUp();
        await moveMouseTo(0, 0);
        await sleep(100);
    }
}

// 切换队伍
async function SwitchParty(partyName) {
    let ConfigureStatue = false;

    let foundQuickSetup = false;
    for (let j = 0; j < 2; j++) {  // 尝试两次
        keyPress("VK_L");
        await sleep(2000);
        for (let i = 0; i < 2; i++) {
            let captureRegion = captureGameRegion();
            let QuickSetupButton = captureRegion.find(QuickSetupButtonRo);
            captureRegion.dispose();
            if (QuickSetupButton.isExist()) {
                log.info("已进入队伍配置页面");
                foundQuickSetup = true;
                break;
            } else {
                await sleep(1000);
            }
        }
        if (foundQuickSetup) {
            break;  // 第一次找到就退出循环
        }
    }

    if (!foundQuickSetup) {
        log.error("两次尝试都未能进入队伍配置页面");
        return false;
    }
    // 识别当前队伍
    let captureRegion = captureGameRegion();
    let resList = captureRegion.findMulti(RecognitionObject.ocr(100, 900, 300, 180));
    captureRegion.dispose();
    let currentPartyFound = false;

    for (let i = 0; i < resList.count; i++) {
        let res = resList[i];
        log.info("当前队伍名称位置:({x},{y},{w},{h}), 识别结果：{text}", res.x, res.y, res.Width, res.Height, res.text);
        if (res.text.includes(partyName)) {
            log.info("当前队伍即为目标队伍，无需切换");
            notification.send(`当前队伍即为目标队伍：${partyName}，无需切换`);
            keyPress("VK_ESCAPE");
            await sleep(500);
            currentPartyFound = true;
            break;
        }
    }
    if (!currentPartyFound) {
        await sleep(1000);
        let captureRegion = captureGameRegion();
        let ConfigureTeamButton = captureRegion.find(ConfigureTeamButtonRo);
        captureRegion.dispose();
        if (ConfigureTeamButton.isExist()) {
            log.info("识别到配置队伍按钮");
            ConfigureTeamButton.click();
            await sleep(500);
            await pageTop(LeftSliderTopRo);

            for (let p = 0; p < 4; p++) {
                // 识别当前页
                let captureRegion = captureGameRegion();
                let resList = captureRegion.findMulti(RecognitionObject.ocr(0, 100, 400, 900));
                captureRegion.dispose();
                for (let i = 0; i < resList.count; i++) {
                    let res = resList[i];
                    if (settings.enableDebug) {
                        log.info("文本位置:({x},{y},{w},{h}), 识别内容：{text}", res.x, res.y, res.Width, res.Height, res.text);
                    }
                    if (res.text.includes(partyName)) {
                        log.info("目标队伍位置:({x},{y},{w},{h}), 识别结果：{text}", res.x, res.y, res.Width, res.Height, res.text);
                        click(Math.ceil(res.x + 360), res.y + Math.ceil(res.Height / 2));

                        // 找到目标队伍，点击确定、部署
                        await sleep(1500);
                        let ConfirmButtonCaptureRegion = captureGameRegion();
                        let ConfirmButton = ConfirmButtonCaptureRegion.find(ConfirmDeployButtonRo);
                        ConfirmButtonCaptureRegion.dispose();
                        if (ConfirmButton.isExist()) {
                            log.info("识别到确定按钮:({x},{y},{w},{h})", ConfirmButton.x, ConfirmButton.y, ConfirmButton.Width, ConfirmButton.Height);
                            ConfirmButton.click();
                        }
                        await sleep(1500);
                        let DeployButtonCaptureRegion = captureGameRegion();
                        let DeployButton = DeployButtonCaptureRegion.find(ConfirmDeployButtonRo);
                        DeployButtonCaptureRegion.dispose();
                        if (DeployButton.isExist()) {
                            log.info("识别到部署按钮:({x},{y},{w},{h})", DeployButton.x, DeployButton.y, DeployButton.Width, DeployButton.Height);
                            DeployButton.click();
                            await sleep(100);
                            notification.send(`寻找到目标队伍：${partyName}`);
                            ConfigureStatue = true;
                            break;
                        }
                    }
                }
                if (ConfigureStatue) {
                    await genshin.returnMainUi();
                    break;
                } else {
                    await pageDown(LeftSliderBottomRo);
                }
            }
            if (!ConfigureStatue) {
                // 没找到指定队伍名称的队伍，抛出异常
                log.error(`没有找到指定队伍名称：${partyName}`);
                notification.error(`没有找到指定队伍名称：${partyName}`);
                await genshin.returnMainUi();
                throw new Error(`没有找到指定队伍名称：${partyName}`);
            }
        } else {
            // 没找到配置队伍按钮，抛出异常
            log.error("没有找到配置队伍按钮");
            notification.error("没有找到配置队伍按钮");
            await genshin.returnMainUi();
            throw new Error("没有找到配置队伍按钮");
        }
    } else {
        // 当前队伍就是目标队伍，设置成功状态
        ConfigureStatue = true;
    }
    return ConfigureStatue;
}

async function SwitchPartyMain(partyName, disableGoStatue) {
    if (!!partyName) {
        try {
            if (!disableGoStatue) {
                // 强制去七天神像换队
                log.info("强制传送到七天神像切换队伍");
                await genshin.TpToStatueOfTheSeven();
                log.info("正在尝试切换至" + partyName);
                await SwitchParty(partyName);
            } else {
                // 先尝试在当前位置换队
                await genshin.returnMainUi();
                log.info("正在尝试切换至" + partyName);
                let switchResult = await SwitchParty(partyName);

                if (!switchResult) {
                    // 如果当前位置换队失败，去七天神像再试一次
                    log.info("当前位置换队失败，传送到七天神像重试");
                    await genshin.TpToStatueOfTheSeven();
                    log.info("正在七天神像重新尝试切换至" + partyName);
                    await SwitchParty(partyName);
                }
            }
            genshin.clearPartyCache();
            return partyName
        } catch (error) {
            log.error("队伍切换失败：" + error.message);
            notification.error("队伍切换失败：" + error.message);
            await genshin.returnMainUi();
        }
    } else {
        log.error("没有设置切换队伍");
        notification.error("没有设置切换队伍");
        await genshin.returnMainUi();
    }
}
globalThis.switchUtil={
    SwitchPartyMain
}
// /**
//  * @returns {Promise<void>}
//  */
//
// (async function () {
//     await SwitchPartyMain(settings.partyName, settings.disableGoStatue);
// })();
