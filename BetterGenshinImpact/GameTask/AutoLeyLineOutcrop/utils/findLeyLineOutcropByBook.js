// 齿轮图标识别对象
const mapSettingButtonRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("assets/icon/map_setting_button.bmp"));

/**
 * 通过冒险之证查找地脉花位置
 * @param {string} country - 国家名称
 * @param {string} type - 地脉花类型
 * @returns {Promise<void>}
 */
this.findLeyLineOutcropByBook = async function (country, type) {
  await genshin.returnMainUi();
  await sleep(1000);
  log.info("使用冒险之证寻找地脉花");
  
  // 确保运行时位于主界面
  keyPress("F1");
  // 开书等待时间延长至2.5s
  await sleep(2500);
  click(300, 550); // 点击讨伐
  await sleep(1000);
  click(500, 200); // 点击筛选
  await sleep(1000);
  click(500, 500); // 点击其他
  await sleep(1000);
  
  // 选择地脉花类型
  if (type === "蓝花（经验书）") {
    click(700, 350); // 点击经验花
  } else {
    click(500, 350); // 点击摩拉花
  }
  await sleep(1000);
  click(1300, 800); // 点击推荐
  await sleep(1000);

  // 查找并点击指定国家
  await this.findAndClickCountry(country);
  
  // 查找并点击停止追踪按钮
  await this.findAndCancelTrackingInBook();
  
  // 最多重试3次确保大地图正确打开
  for (let retry = 0; retry < 3; retry++) {
    await sleep(1000);
    click(1500, 850);
    // 等待大地图打开延长至 2.5s
    await sleep(2500);
    
    if (await this.checkBigMapOpened()) {
      log.info("识别到大地图");
      break; // 成功打开大地图
    }
    
    if (retry < 2) { // 不是最后一次重试
      log.info(`大地图未正确打开，第${retry + 1}次重试...`);
      await genshin.returnMainUi();
      await this.findAndClickCountry(country);
      await this.findAndCancelTrackingInBook();
    } else {
      throw new Error("大地图打开失败，已重试3次");
    }
  }

  // 获取地脉花位置
  const center = genshin.getPositionFromBigMap();
  leyLineX = center.x;
  leyLineY = center.y;
  log.info(`找到地脉花的坐标：(${leyLineX}, ${leyLineY})`);

  // 取消追踪
  await this.cancelTrackingInMap();
};

/**
 * 检测大地图是否正确打开（通过检测齿轮图标）
 * @returns {Promise<boolean>} 返回是否检测到齿轮图标
 * @private
 */
this.checkBigMapOpened = async function() {
  let captureRegion = captureGameRegion();
  try {
    const imageResult = captureGameRegion().find(mapSettingButtonRo);
    return imageResult.isExist();
  } finally {
    captureRegion.dispose();
  }
};

/**
 * 查找并点击指定国家
 * @param {string} country - 国家名称
 * @private
 */
this.findAndClickCountry = async function (country) {
  // 挪德卡莱会被识别成挪德卡菜，需要特殊处理
  let match_country = country;
  if (country === "挪德卡莱") {
    match_country = "挪德卡";
  }
  let captureRegion = captureGameRegion();
  try {
    let resList = captureRegion.findMulti(ocrRoThis);
    let found = false;
    for (let i = 0; i < resList.count; i++) {
      let res = resList[i];
      if (res.text.includes(match_country)) {
        res.click();
        found = true;
        break;
      }
    }
    if (!found) {
      // 没找到国家
      throw new Error("冒险之征中未找到国家：" + country + "，请检查该国家地图是否开启");
    }
  } finally {
    captureRegion.dispose();
  }
};

/**
 * 在冒险之征中查找并点击停止追踪按钮
 * @private
 */
this.findAndCancelTrackingInBook = async function () {
  let captureRegion = captureGameRegion();
  try {
    let resList = captureRegion.findMulti(ocrRoThis);
    let stop = null;
    for (let i = 0; i < resList.count; i++) {
      let res = resList[i];
      if (res.text.includes("停止")) {
        stop = res;
        break;
      }
    }

    if(stop) {
      log.info("冒险之征中点击停止追踪并重新追踪获取坐标");
      stop.click();
    } else {
      log.info("冒险之征中无需取消追踪");
    }
  } finally {
    captureRegion.dispose();
  }
  await sleep(1000);
};

/**
 * 在地图中取消地脉花追踪
 * @private
 */
this.cancelTrackingInMap = async function () {
  // 点击地脉花(因为从书点击追踪后花会在屏幕正中间)
  click(960, 540);
  await sleep(1000);
  
  let captureRegion = captureGameRegion();
  let stop = null;
  let leyLine = null;
  
  try {
    let resList = captureRegion.findMulti(ocrRoThis);
    if (resList && resList.count > 0) {
      for (let i = 0; i < resList.count; i++) {
        let res = resList[i];
        if (res.text.includes("停止")) {
          stop = res;
        } else if (res.text.includes("地脉") || res.text.includes("衍出")) {
          leyLine = res;
        }
      }
    }
  } finally {
    captureRegion.dispose();
  }
  
  
  if (stop) {
    log.info("地图中点击取消追踪");
    stop.click();
  } else if (leyLine) {
    log.info("在地图中选择地脉花后点击取消追踪");
    leyLine.click();
    await sleep(1000);
    click(1700, 1010);
    await sleep(1000);
  }
};