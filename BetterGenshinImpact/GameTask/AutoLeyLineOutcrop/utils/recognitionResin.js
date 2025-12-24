/*
    代码迁移中，还未完成适配
*/


const CondensedRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/Condensed Resin.png"));
const FragileRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/Fragile Resin.png"));
const TemporaryRo = RecognitionObject.TemplateMatch(file.ReadImageMatSync("RecognitionObject/5.png"));
CondensedRo.threshold = 0.70;
CondensedRo.Use3Channels = true;
FragileRo.threshold = 0.70;
FragileRo.Use3Channels = true;

this.recognitionResin = 
async function() {
    let totalRunNum = 0;
	await genshin.returnMainUi();
	await sleep(2000);
	keyPress("m");
	await sleep(2000);

	let captureRegion = captureGameRegion();
	let resList = captureRegion.findMulti(RecognitionObject.ocr(1043, 5, 300, 100));
	captureRegion.dispose();
	let IsOver = false

	for (let i = 0; i < resList.count; i++) {
		let resStamina = resList[i];
		log.info(`第 ${i + 1} 个结果: ${resStamina.text}`);
		await sleep(2000);

		// 提取/前面的数字
		const rawText = resStamina.text;
		const splitResult = rawText.split('/');  // 用/分割字符串

		// 确保分割后得到两部分且第一部分是有效数字
		if (splitResult.length >= 1) {
			const staminaValue = parseInt(splitResult[0]);  // 只取第一部分

			if (!isNaN(staminaValue)) {
				log.info(`提取的体力值: ${staminaValue}`);

				if (staminaValue >= 40) {
					IsOver = true;
				}
                break;
			} else {
				log.warn("无效的数字格式");
			}
		} else {
			log.warn("未找到/分隔符");
		}

		await sleep(2000)
		await genshin.returnMainUi();
		keyPress("b");
		await sleep(2000);
		click(1245, 50);
		await sleep(2000);

		// 浓缩树脂识别
		let condensedCaptureRegion = captureGameRegion();
		let Condensed = condensedCaptureRegion.find(CondensedRo);
		condensedCaptureRegion.dispose();
		let Isfive = false;
		if (Condensed.isExist()) {
			log.info("识别到浓缩树脂");
			let CondensedX = Math.round(Condensed.x + Condensed.width / 2 - 20)
			let Condensedy = Math.round(Condensed.y + Condensed.height / 2 + 60)
			log.info(`点击坐标: (${CondensedX}, ${Condensedy})`);

			let captureRegion = captureGameRegion();
			let Condensedres = captureRegion.findMulti(RecognitionObject.ocr(CondensedX, Condensedy, 50, 50));
			captureRegion.dispose();
			for (let i = 0; i < Condensedres.count; i++) {
				let resCondensed = Condensedres[i];
				log.info(`浓缩树脂: ${resCondensed.text}`);
				await sleep(2000);
				if (resCondensed.text == 5) {
					Isfive = true;
					log.info("浓缩树脂已满")
					await sleep(2000);

				}
			}
		}
		// 脆弱树脂识别
		let FragileCapture = captureGameRegion();
		let Fragile = FragileCapture.find(FragileRo);
		FragileCapture.dispose();
		if (Fragile.isExist()) {
			log.info("识别到脆弱树脂");

			let FragileX = Math.round(Fragile.x + Fragile.width / 2 - 20)
			let Fragiley = Math.round(Fragile.y + Fragile.height / 2 + 60)

			let captureRegion = captureGameRegion();
			let Fragileres = captureRegion.findMulti(RecognitionObject.ocr(FragileX, Fragiley, 50, 50));
			captureRegion.dispose();

			if (Fragileres.count === 0) {
				log.error("OCR识别失败：未能识别到脆弱树脂数量");
			} else {
				for (let i = 0; i < Fragileres.count; i++) {
					let resFragile = Fragileres[i];
					if (resFragile.text && resFragile.text.trim() !== "") {
						log.info("脆弱树脂数量: " + resFragile.text);
					} else {
						log.warn("OCR识别结果为空或无效");
					}
				}
			}
		} else {
			log.info("未识别到脆弱树脂");
			await sleep(2000);
		}
		// 须臾树脂识别
		let TemporaryCapture = captureGameRegion();
		let Temporary = TemporaryCapture.find(TemporaryRo);
		TemporaryCapture.dispose();
		let Temporaryres = null;
		if (Temporary.isExist()) {
			log.info("识别到须臾树脂");

			let TemporaryX = Math.round(Temporary.x + Temporary.width / 2 - 20)
			let Temporaryy = Math.round(Temporary.y + Temporary.height / 2 + 40)
			log.info(`点击坐标: (${TemporaryX}, ${Temporaryy})`);

			let captureRegion = captureGameRegion();
			Temporaryres = captureRegion.findMulti(RecognitionObject.ocr(TemporaryX, Temporaryy, 50, 50));
			captureRegion.dispose();
		} else {
			log.info("未识别到须臾树脂");
		}

		if (Temporaryres && Temporaryres.count === 0) {
			log.error("OCR识别失败：未能识别到须臾树脂数量");
		} else {
			for (let i = 0; i < Temporaryres.count; i++) {
				let resTemporary = Temporaryres[i];
				if (resTemporary.text && resTemporary.text.trim() !== "") {
					log.info("须臾树脂数量: " + resTemporary.text);
				} else {
					log.warn("OCR识别结果为空或无效");
				}
				await sleep(2000);
			}
		}


		// 尝试调用任务


		if (IsOver && Isfive == true) {
			log.info("需要前往合成台"); // 输出 true
		} else {
			log.info("不需要前往合成台");
		}
	}
}