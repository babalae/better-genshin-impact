// 分钟 小时 日期 月份 星期 [年份可选]
const cronRegex = /^(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)(?:\s+(\S+))?$/;

/**
 * 解析单个 cron 字段，返回匹配的数值数组
 * @param {string} field - 字段值 如 "1,3-5,* /10"
 * @param {number} min - 最小值
 * @param {number} max - 最大值
 * @param {number} [stepBase=0] - 步进基准（星期从0或1开始都支持）
 * @returns {Set<number>} 所有匹配的数值集合
 */
function parseCronField(field, min, max, stepBase = 0) {
    const result = new Set();
    const parts = field.split(',');

    for (const part of parts) {
        // 处理 */n 格式
        if (part.startsWith('*/')) {
            const step = parseInt(part.slice(2), 10);
            if (isNaN(step) || step <= 0) continue;

            for (let i = min; i <= max; i += step) {
                result.add(i);
            }
            continue;
        }

        // 处理 n-n/n 格式
        if (part.includes('/')) {
            const [range, stepStr] = part.split('/');
            const step = parseInt(stepStr, 10);
            if (isNaN(step) || step <= 0) continue;

            if (range === '*') {
                for (let i = min; i <= max; i += step) {
                    result.add(i);
                }
            } else if (range.includes('-')) {
                const [start, end] = range.split('-').map(Number);
                if (isNaN(start) || isNaN(end)) continue;
                for (let i = Math.max(min, start); i <= Math.min(max, end); i += step) {
                    result.add(i);
                }
            }
            continue;
        }

        // 处理范围 n-n
        if (part.includes('-')) {
            const [start, end] = part.split('-').map(Number);
            if (!isNaN(start) && !isNaN(end)) {
                for (let i = Math.max(min, start); i <= Math.min(max, end); i++) {
                    result.add(i);
                }
            }
            continue;
        }

        // 处理单个数字 / 列表
        const num = parseInt(part, 10);
        if (!isNaN(num) && num >= min && num <= max) {
            result.add(num);
        }

        // 处理 L（最后一天） - 只对日期字段有意义，这里简单处理
        if (part === 'L' && max === 31) {
            // 实际使用时需要知道当月天数，这里先占位
            result.add(99); // 特殊标记，后续需处理
        }

        // ? 在日期/星期中代表「无特定值」 - 这里简单忽略具体值
        if (part === '?') {
            // 实际使用时需配合另一字段判断
        }
    }

    return result;
}

/**
 * 简单版 cron 解析器（只解析出每个字段允许的时间值）
 * @param {string} cron - cron表达式 "0 9 * * 1-5"
 * @returns {object|null} 解析结果 或 null（格式错误）
 */
function parseCron(cron) {
    const match = cron.trim().match(cronRegex);
    if (!match) return null;

    const [, min, hour, day, month, dow] = match;

    try {
        const minutes = parseCronField(min, 0, 59);
        const hours = parseCronField(hour, 0, 23);
        const days = parseCronField(day, 1, 31);
        const months = parseCronField(month, 1, 12);
        const dows = parseCronField(dow, 0, 7); // 0和7都代表周日

        // 星期字段 7 → 0
        if (dows.has(7)) dows.add(0);

        return {
            minutes: [...minutes].sort((a, b) => a - b),
            hours: [...hours].sort((a, b) => a - b),
            days: [...days].sort((a, b) => a - b),
            months: [...months].sort((a, b) => a - b),
            dows: [...dows].sort((a, b) => a - b),
            // 注：days和dows同时有特定值时，实际cron是「或」关系
            // 这里仅简单分开列出，真实调度需复杂判断
            original: cron.trim(),
            isValid: true
        };
    } catch (e) {
        return {isValid: false, error: e.message, original: cron};
    }
}


/**
 * 获取下一个Cron时间戳
 * @param {string} cronExpression - Cron表达式
 * @param {number} [startTimestamp=Date.now()] - 开始时间戳，默认为当前时间
 * @param {number} endTimestamp - 结束时间戳
 * @returns {Promise} 返回一个Promise，解析为下一个Cron时间戳
 */
async function getNextCronTimestamp(cronExpression, startTimestamp = Date.now(), endTimestamp, url) {
    log.warn("使用cron CD算法 请开启JS HTTP 权限 如已开启请忽略")
    const result = await http.request("POST", url, JSON.stringify({
        cronExpression: `${cronExpression}`,
        startTimestamp: startTimestamp,
        endTimestamp: endTimestamp
    }), JSON.stringify({
        "Content-Type": "application/json"
    })).then(res => {
        log.debug(`[{0}]res=>{1}`, 'next', JSON.stringify(res))
        if (res.status_code === 200 && res.body) {
            let result_json = JSON.parse(res.body);
            if (result_json?.code === 200) {
                return result_json?.data
            }
            throw new Error("请求失败,error:" + result_json?.message)
        }
        return undefined
    })

    return result === null || !result ? undefined : result
}

/**
 * 获取所有cron表达式的时间戳
 * @param {Array} bodyJson - 包含cron表达式和相关参数的对象数组，默认值为空数组
 * @param {string} url - 请求的目标URL
 * @returns {Map} 返回一个Map对象，其中key为输入参数中的key，value为对应的ok状态
 */
async function getNextCronTimestampAll(bodyJson = [{
    key: '',           // 标识符
    cronExpression: "", // cron表达式
    startTimestamp: Date.now(), // 开始时间戳
    endTimestamp: 0   // 结束时间戳
}], url) {
    // 打印警告日志，提示用户需要开启JS HTTP权限
    log.warn("使用cron CD算法 请开启JS HTTP 权限 如已开启请忽略")
    // 初始化结果列表，默认包含一个成功的空key项
    let resultList = [{key: '', ok: true,next:0}]
    // 发送HTTP POST请求并处理响应
    try {
        resultList = (
            await http.request("POST", url, JSON.stringify({cronList:bodyJson}), JSON.stringify({
                "Content-Type": "application/json"
            })).then(res => {
                // 打印调试日志，记录响应内容
                log.debug(`[{0}]res=>{1}`, 'next', JSON.stringify(res))
                // 检查响应状态码和响应体
                if (res.status_code === 200 && res.body) {
                    let result_json = JSON.parse(res.body);
                    // 检查响应代码是否为200
                    if (result_json?.code === 200) {
                        return result_json?.data
                    }
                    // 如果响应代码不为200，抛出错误
                    throw new Error("请求失败,error:" + result_json?.message)
                }
                return undefined
            })
        )
    } catch (e) {
        log.warn("CD HTTP请求失败，跳过CD过滤继续执行: {0}", e?.message || e)
        resultList = bodyJson.map(item => ({key: item.key, ok: true, next: 0}))
    }
    if (!Array.isArray(resultList)) {
        log.warn("CD HTTP请求未返回有效列表，跳过CD过滤继续执行")
        resultList = bodyJson.map(item => ({key: item.key, ok: true, next: 0}))
    }
    // 创建一个Map对象，用于存储结果
    let map = new Map()
    // 遍历结果列表，将每个项的key和ok状态存入Map
    resultList.forEach(item => {
        map.set(item.key, item)
    })

    // 返回包含结果的Map对象
    return map
}

//影响到性能 改http 第三方
// function getNextCronTimestamp(cron, fromTime = Date.now(),endTime) {
//     const parts = cron.trim().split(/\s+/);
//     if (parts.length < 5 || parts.length > 6) {
//         throw new Error("不支持的 cron 格式，应为 5~6 段");
//     }
//
//     const [minStr, hourStr, dayStr, monthStr, dowStr] = parts;
//
//     // 解析每个字段
//     const minutes = parseField(minStr, 0, 59);
//     const hours = parseField(hourStr, 0, 23);
//     const days = parseField(dayStr, 1, 31);
//     const months = parseField(monthStr, 1, 12);
//     const dows = parseField(dowStr, 0, 7);
//
//     // 星期 7 → 0 (周日)
//     if (dows.has(7)) dows.add(0);
//
//     let current = new Date(fromTime);
//     // 如果没有指定 endTime，默认设置为明天 00:00:00
//     if (endTime === undefined) {
//         const tomorrow = new Date(current);
//         tomorrow.setDate(tomorrow.getDate() + 1);
//         tomorrow.setHours(0, 0, 0, 0);
//         endTime = tomorrow.getTime();
//     }
//
//     // 动态计算最大迭代次数
//     // 将时间差（毫秒）转换为分钟，向上取整，确保覆盖所有可能的分钟点
//     const timeDiffMinutes = Math.ceil((endTime - fromTime) / 60000);
//
//     // 设置最大迭代次数，防止意外情况（如 endTime 极大）导致内存溢出或死循环
//     // 即使 endTime 是 10 年后，也限制在约 2 年内（避免极端情况）
//     // 2年 ≈ 365 * 2 * 24 * 60 = 1,051,200
//     const MAX_ITERATIONS_LIMIT = 1051200;
//     const MAX_ITERATIONS = Math.min(timeDiffMinutes, MAX_ITERATIONS_LIMIT);
//
//     let iteration = 0;
//     while (iteration++ < MAX_ITERATIONS) {
//         // 先推进到下一分钟，避免死循环在同一分钟
//         current.setMinutes(current.getMinutes() + 1);
//         current.setSeconds(0);
//         current.setMilliseconds(0);
//
//         const m = current.getMinutes();
//         const h = current.getHours();
//         const d = current.getDate();
//         const mon = current.getMonth() + 1; // JS 月份 0~11
//         const dow = current.getDay();       // 0=周日, 1=周一, ..., 6=周六
//
//         // 核心匹配条件（日期和星期是 OR 关系）
//         const minuteMatch = minutes.has(m) || minutes.size === 0;
//         const hourMatch = hours.has(h) || hours.size === 0;
//         const monthMatch = months.has(mon) || months.size === 0;
//         const dayMatch = days.has(d) || days.size === 0;
//         const dowMatch = dows.has(dow) || dows.size === 0;
//
//         const dateOrDowMatch = (days.size === 0 && dows.size === 0) ||  // 两者都是 *
//             (days.size > 0 && dows.size === 0) ||     // 只指定了日期
//             (days.size === 0 && dows.size > 0) ||     // 只指定了星期
//             (dayMatch && dowMatch);                   // 两者都满足才算（最严格）
//
//         if (minuteMatch && hourMatch && monthMatch && dateOrDowMatch) {
//             return current.getTime();
//         }
//     }
//
//     // 如果是因为超过 MAX_ITERATIONS_LIMIT 而退出，说明时间跨度太大
//     if (timeDiffMinutes > MAX_ITERATIONS_LIMIT) {
//         log.warn("查找范围过大，已达到最大迭代次数限制");
//     } else {
//         log.warn("未找到合理下一次执行时间");
//     }
//     return null;
// }

/**
 * 解析单个 cron 字段，返回匹配的数值 Set
 * 支持： *  ,  -  /  数值列表  * /n
 */
function parseField(field, min, max) {
    const result = new Set();

    if (field === '*' || field === '?') {
        return result; // 空 set 代表任意
    }

    const parts = field.split(',');

    for (let part of parts) {
        part = part.trim();

        // */n
        if (part.startsWith('*/')) {
            const step = Number(part.slice(2));
            if (!isNaN(step) && step > 0) {
                for (let i = min; i <= max; i += step) {
                    result.add(i);
                }
            }
            continue;
        }

        // n-n
        if (part.includes('-') && !part.includes('/')) {
            const [start, end] = part.split('-').map(Number);
            if (!isNaN(start) && !isNaN(end)) {
                for (let i = Math.max(min, start); i <= Math.min(max, end); i++) {
                    result.add(i);
                }
            }
            continue;
        }

        // n/n 或 */n 已处理，剩下是普通数字或范围/步进
        if (part.includes('/')) {
            const [range, stepStr] = part.split('/');
            const step = Number(stepStr);
            if (isNaN(step) || step <= 0) continue;

            if (range === '*') {
                for (let i = min; i <= max; i += step) result.add(i);
            } else {
                const [start, end] = range.split('-').map(Number);
                if (!isNaN(start) && !isNaN(end)) {
                    for (let i = Math.max(min, start); i <= Math.min(max, end); i += step) {
                        result.add(i);
                    }
                }
            }
            continue;
        }

        // 单个数字
        const num = Number(part);
        if (!isNaN(num) && num >= min && num <= max) {
            result.add(num);
        }
    }

    return result;
}


globalThis.cronUtil = {
    getNextCronTimestamp,
    getNextCronTimestampAll
}
