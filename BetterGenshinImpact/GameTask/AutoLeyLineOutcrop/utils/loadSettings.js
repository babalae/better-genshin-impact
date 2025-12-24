/**
 * 加载、验证、输出用户设置
 * @returns {Object} 处理过的设置对象
 */
this.loadSettings = 
function () {
    try {
        // 直接使用全局settings对象而不是重新创建
        // 这样能保留原始设置内容

        // 验证必要的设置
        if (!settings.start) {
            throw new Error("请仔细阅读脚本介绍和手册，并在调度器内进行配置，如果你是直接运行的脚本，请将脚本加入调度器内运行！");
        }

        if (!settings.leyLineOutcropType) {
            throw new Error("请选择你要刷取的地脉花类型（经验书/摩拉）");
        }

        if (!settings.country) {
            throw new Error("请在游戏中确认地脉花的第一个点的位置，然后在js设置中选择地脉花所在的国家。");
        }

        if (settings.friendshipTeam && !settings.team) {
            throw new Error("未配置战斗队伍！当配置了好感队时必须配置战斗队伍！");
        }

        // 为了向后兼容，确保某些设置有默认值
        //settings.timeout = settings.timeout * 1000 || 120000;

        // 处理刷取次数
        if (!settings.count || !/^-?\d+\.?\d*$/.test(settings.count)) {
            log.warn(`刷取次数 ${settings.count} 不是数字，使用默认次数6次`);
            settings.timesValue = 6;
        } else {
            // 转换为数字
            const num = parseFloat(settings.count);

            // 范围检查
            if (num < 1) {
                settings.timesValue = 1;
                log.info(`⚠️ 次数 ${num} 小于1，已调整为1`);
            } else {
                // 处理小数
                if (!Number.isInteger(num)) {
                    settings.timesValue = Math.floor(num);
                    log.info(`⚠️ 次数 ${num} 不是整数，已向下取整为 ${settings.timesValue}`);
                } else {
                    settings.timesValue = num;
                }
            }
        }

        // 记录使用的设置
        log.info(`地脉花类型：${settings.leyLineOutcropType}`);
        log.info(`国家：${settings.country}`);

        if (settings.friendshipTeam) {
            log.info(`好感队：${settings.friendshipTeam}`);
        }
        if (settings.isResinExhaustionMode) {
            log.warn("树脂耗尽模式已开启，若统计成功将覆盖设置的刷取次数");
        } else {
            log.info(`刷取次数：${settings.timesValue}`);
        }

        // 设置通知状态
        isNotification = settings.isNotification;

        // 设置一条龙模式
        oneDragonMode = settings.oneDragonMode;

        if (isNotification) {
            notification.send(`全自动地脉花开始运行，以下是本次运行的配置：\n\n地脉花类型：${settings.leyLineOutcropType}\n国家：${settings.country}\n刷取次数：${settings.timesValue}`);
        }
    } catch (error) {
        log.error(`加载设置失败: ${error.message}`);
        throw error;
    }
}