import {toMainUi} from "./utils/tool";

let manifest_json = "manifest.json";
let manifest = undefined
let configSettings = undefined
const auto = {
    semi: false,//半自动
    run: false,//运行
    skip: false,//跳过
    key: "",
}
const dev = {
    isDebug: false,
    debug: undefined,
}
const cd = {
    open: settings.open_cd || false,
    http_api: settings.http_api || undefined,
}
const pathingName = "pathing"
let loadingLevel = 2
// const pathAsMap = new Map([])
// const pathRunMap = new Map([])
const needRunMap = new Map([])
const PATHING_ALL = new Array({
    id: `${pathingName}`,
    level: 0,
    name: `${pathingName}`,
    parent_name: "",
    child_names: []
})
let settingsNameList = new Array()
const settingsNameAsList = new Array()
let PATH_JSON_LIST = new Array()
const config_root = 'config'
const levelName = "treeLevel"
const json_path_name = {
    RecordText: `${config_root}\\record.json`,
    RecordPathText: `${config_root}\\PathRecord.json`,
    uidSettingsJson: `${config_root}\\uidSettings.json`,
    templateMatchSettingsJson: `${config_root}\\settings.json`,
    cdPath: `${config_root}\\cd-${pathingName}.json`,
    SevenElement: `${config_root}\\SevenElement.json`,
    RefreshSettings: `${config_root}\\RefreshSettings.json`,
    pathJsonByUid: `${config_root}\\path-json-by-uid.json`,
    PathOrder: `${config_root}\\PathOrder.json`,
    HoeGround: `${config_root}\\HoeGround.json`,
    LimitMax: `${config_root}\\LimitMax.json`,
}
// 定义记录文件的路径
// let RecordText = `${config_root}\\record.json`
// let RecordPathText = `${config_root}\\PathRecord.json`
let RecordList = new Array()
let RecordPathList = new Array()
let RecordLast = {
    uid: "",
    data: undefined,
    timestamp: 0,
    paths: new Set(), // 记录路径
    errorPaths: new Set(),
    groupPaths: new Set(),
}
const Record = {
    uid: "",
    data: undefined,
    timestamp: 0,
    paths: new Set(), // 记录路径
    errorPaths: new Set(), // 记录错误路径
    groupPaths: new Set(), // 记录分组路径
}
let RecordPath = {
    uid: "",
    paths: new Set(), // 记录路径
    //{timestamp,path}
}
const config_list = {
    black: [],
    white: [],
}

const SevenElement = {
    SevenElements: ['矿物', '火', '水', '风', '雷', '草', '冰', '岩'],
    SevenElementsMap: new Map([
        ['矿物', ['夜泊石', '石珀', '清水玉', '万相石', '矿物']],
        ['火', []],
        ['水', ['海露花']],
        ['风', ['蒲公英籽']],
        ['雷', ['琉鳞石', '绯樱绣球']],
        ['草', []],
        ['冰', []],
        ['岩', []],
    ]),
}


const team = {
    current: undefined,
    fight: false,
    fightName: settings.team_fight,
    fightKeys: ['锄地专区', "敌人与魔物"],
    SevenElements: settings.team_seven_elements ? settings.team_seven_elements.split(',').map(item => item.trim()) : [],
    HoeGroundMap: new Map([]),

}
const timeType = Object.freeze({
    hours: 'hours',//小时
    cron: 'cron',//cron表达式
    // 添加反向映射（可选）
    fromValue(value) {
        return Object.keys(this).find(key => this[key] === value);
    }
});

/**
 * 实时任务处理函数
 * @param {boolean} is_common - 是否为通用任务标志
 * @returns {void} 无返回值
 */
async function realTimeMissions(is_common = true) {
    let real_time_missions = settings.real_time_missions  // 从设置中获取实时任务列表
    if (!Array.isArray(real_time_missions)) {
        real_time_missions = [...real_time_missions]
    }
    if (!is_common) {  // 处理非通用任务
        if (real_time_missions.includes("自动战斗")) {
            // await dispatcher.runAutoFightTask(new AutoFightParam());
            await dispatcher.runTask(new SoloTask("AutoFight"));  // 执行自动战斗任务
        }
        return  // 非通用任务处理完毕后直接返回
    }
    // 处理通用任务
    if (real_time_missions.includes("自动对话")) {
        dispatcher.addTrigger(new RealtimeTimer("AutoSkip"));  // 添加自动对话触发器
    }
    if (real_time_missions.includes("自动拾取")) {
        // 启用自动拾取的实时任务
        dispatcher.addTrigger(new RealtimeTimer("AutoPick"));  // 添加自动拾取触发器
    }

}

/**
 * 根据用户ID加载路径JSON列表
 * 该函数尝试从指定路径读取并解析JSON文件，将解析后的数据转换为Map对象，
 * 然后根据当前用户ID获取对应的路径列表
 *
 * @returns {boolean} 加载成功返回true，失败返回false
 */
function loadPathJsonListByUid() {
    try {
        // 读取并解析JSON文件内容
        const raw = JSON.parse(file.readTextSync(json_path_name.pathJsonByUid));
        // 将解析后的数组转换为Map对象
        const map = new Map(raw);

        // 获取当前用户ID对应的路径列表；静态预配置场景下回退到公共缓存。
        const list = map.get(Record.uid) || map.get("");
        // 检查获取的列表是否为有效数组且不为空
        if (Array.isArray(list) && list.length > 0) {
            // 更新全局PATH_JSON_LIST变量
            PATH_JSON_LIST = list;
            // 记录成功日志，包含用户ID和路径数量
            log.info(
                "[PATH] 已加载 PATH_JSON_LIST，uid={0}，count={1}",
                Record.uid,
                PATH_JSON_LIST.length
            );
            return true;
        }
    } catch (e) {
        // 捕获并记录异常信息
        log.warn("[PATH] 加载 PATH_JSON_LIST 失败: {0}", e.message);
    }
    return false;
}

async function initRefresh(settingsConfig) {
    let level = 0
    const parent_level = level + 1
    // 获取当前路径下的所有文件/文件夹
    let pathSyncList = file.readPathSync(`${PATHING_ALL[level].name}`);
    log.debug("{0}文件夹下有{1}个文件/文件夹", `${pathingName}`, pathSyncList.length);
    // 预处理黑白名单数组，移除空字符串并trim
    const processedBlackList = config_list.black
        .map(item => item.trim())
        .filter(item => item !== "");

    const processedWhiteList = config_list.white
        .map(item => item.trim())
        .filter(item => item !== "");
    let blacklistSet = new Set(processedBlackList)
    processedWhiteList.forEach(item => {
        blacklistSet.delete(item)
    })
    const blacklist = Array.from(blacklistSet)

    let settingsList = settingsConfig
    let settingsRefreshList = []
    let parentJson = {
        name: `${levelName}_${level}_${level}`,
        type: "multi-checkbox",
        label: `选择要执行的${parent_level}级路径`,
        options: []
    }
    for (const element of pathSyncList) {
        // log.warn("element={0}", element)
        const item = element.replace(`${pathingName}\\`, "");
        if (!blacklist.find(black => item === black)) {
            parentJson.options.push(item)
        }
    }
    // settingsRefreshList.push({type: "separator"})

    let treePathList = await readPaths(`${pathingName}`)
    await debugKey('[init-refresh]_log-treePathList.json', JSON.stringify(treePathList))
    let pathJsonList = await treeToList(treePathList)
    await debugKey('[init-refresh]_log-pathJsonList.json', JSON.stringify(pathJsonList))
    let obj = {
        id: `${pathingName}`,
        level: level,
        name: `${pathingName}`,
        parentId: undefined,
        parentName: undefined,
        rootName: undefined,        // 最父级下一级名称
        child_names: parentJson.options
    };
    await addUniquePath(obj)

    PATH_JSON_LIST = pathJsonList

    const forList = pathJsonList.filter(item => item.isFile)
    for (const element of forList) {
        const pathRun = element.path

        // 检查路径是否被允许
        const isBlacklisted = processedBlackList.some(item => pathRun?.includes(item));
        const isWhitelisted = processedWhiteList.some(item => pathRun?.includes(item));

        if (isBlacklisted && !isWhitelisted) {
            continue;
        }
        //方案1
        try {
            loadingLevel = parseInt(settings.loading_level)
        } catch (e) {
            log.warn("配置 {0} 错误，将使用默认值{0}", "加载路径层级", loadingLevel)
        } finally {
            //
            loadingLevel = loadingLevel < 1 ? 2 : loadingLevel
        }
        // 优化版本
        for (let i = 0; i < loadingLevel; i++) {
            const currentLevel = parent_level + 1 + i;
            const parentLevel = parent_level + i;

            const currentName = getChildFolderNameFromRoot(pathRun, currentLevel);
            const childName = getChildFolderNameFromRoot(pathRun, currentLevel + 1);

            // 检查当前层级是否存在
            if (!currentName) {
                break; // 没有当前层级，停止处理
            }

            // 过滤JSON文件
            const filteredChildName = childName?.endsWith(".json") ? undefined : childName;
            let child_names = Array.from(new Set(filteredChildName ? [filteredChildName] : []).difference(new Set(blacklist)))
            // 获取父级名称用于建立层级关系
            const parentName = getChildFolderNameFromRoot(pathRun, parentLevel);
            const rootName = getChildFolderNameFromRoot(pathRun, parent_level + 1);

            let obj1 = {
                id: undefined,
                parentId: element.parentId,
                level: parentLevel,        // 存储到目标层级 属于目标层级
                name: currentName,              // 当前层级名称
                parentName: parentName,        // 父级名称
                rootName: rootName,        // 最父级下一级名称
                child_names: [...child_names]
            };
            await addUniquePath(obj1);
        }
    }
    // 正确的排序方式
    PATHING_ALL.sort((a, b) => {
        // 首先按 level 排序
        if (a.level !== b.level) {
            return a.level - b.level;
        }
        const pathA = a?.path || '';
        const pathB = b?.path || '';
        // if (a.parent_name !== b.parent_name) {
        //     return a.parent_name.localeCompare(b.parent_name);
        // }
        // level 相同时按 path 排序
        return pathA.localeCompare(pathB);
    });

    await debugKey('[init-refresh]_log-PATHING_ALL.json', JSON.stringify(PATHING_ALL))
    const groupLevel = groupByLevel(PATHING_ALL);
    await debugKey('[init-refresh]_log-groupLevel.json', JSON.stringify(groupLevel))
    // const initLength = settingsList.length
    let parentNameLast = undefined
    // let parentNameNow = undefined
    // const line = 30
    // const br = `${"=".repeat(line)}\n`
    let idx = 0
    // const settingsSortMap = new Map([])
    settingsRefreshList.push({type: "separator"})
    settingsRefreshList.push({type: "separator"})
    groupLevel.filter(list => list.length > 0).forEach(
        (list) => {
            let i = 0
            list.filter(item => item && item.child_names && item.child_names.length > 0).forEach(item => {
                const name = `${levelName}_${item.level}_${i}`
                let prefix = ''
                log.debug(`[{2}]Last{0},Current{1},Name{5}`, "比对", parentNameLast, item.parentName, item.name)
                const isCommonLastAndCurrent = item.parentName !== parentNameLast;
                if (isCommonLastAndCurrent) {
                    parentNameLast = item.parentName;
                    // let b = (line - item.parentName.length) % 2 === 0;
                    // const localLine = b ? ((line - item.parentName.length) / 2) : (Math.ceil((line - item.parentName.length) / 2))
                    // prefix = br + `${"=".repeat(localLine)}${item.parentName}${"=".repeat(localLine)}\n` + br
                }
                // const p = idx === 0 ? "【地图追踪】\n" : `${prefix}[${item.parent_name}-${item.name}]\n`
                const p = `\n${prefix}${(item.rootName && item.name !== item.rootName) ? "《" + item.rootName + "》->" : ""}[${item.name}]`
                idx++
                let leveJson = {
                    name: `${name}`,
                    type: "multi-checkbox",
                    label: `选择要执行的${item.level + 1}级路径${p}`,
                    options: []
                }
                // todo: 待优化
                const targetItem = PATH_JSON_LIST.filter(list_item => !list_item.levelName)
                    .find(list_item => list_item.id === item.parentId);
                if (targetItem) {
                    targetItem.levelName = name || undefined;
                }

                // leveJson.options = leveJson.options.concat(item.child_names)
                leveJson.options = [...item.child_names]
                if (leveJson.options && leveJson.options.length > 0) {
                    settingsNameAsList.push({
                        settings_name: name,
                        settings_as_name: item.name
                    })
                    settingsNameList.push(name)
                    const existingIndex = settingsRefreshList.findIndex(item => item.name === leveJson.name);
                    if (existingIndex !== -1) {
                        // 替换已存在的配置项
                        settingsRefreshList[existingIndex] = leveJson;
                    } else {
                        if (isCommonLastAndCurrent) {
                            settingsRefreshList.push({type: "separator"})
                        }
                        parentNameLast = item.parentName;
                        settingsRefreshList.push({type: "separator"})
                        // 添加新的配置项
                        settingsRefreshList.push(leveJson);
                    }
                    i++
                }
            })
        }
    )
    await debugKey('[init-refresh]_log-settingsRefreshList.json', JSON.stringify(settingsRefreshList))
    //settingsRefreshList 二级排序 todo:
    level++
    settingsList = Array.from(new Set(settingsList.concat(settingsRefreshList)))

    settingsList.filter(
        item => item.name === 'key'
    ).forEach(item => {
        // 刷新settings自动设置密钥
        item.default = manifest.key
    })
    settingsList.filter(
        item => item.name === 'config_run'
    ).forEach(item => {
        // 刷新settings自动设置执行
        item.default = "执行"
    })
    const uidSettingsMap = new Map([])
    // 更新当前用户的配置
    uidSettingsMap.set(Record.uid, settingsList)
    // 安全写入配置文件
    try {
        file.writeTextSync(json_path_name.uidSettingsJson, JSON.stringify([...uidSettingsMap]))
        log.debug("用户配置已保存: {uid}", Record.uid)
    } catch (error) {
        log.error("保存用户配置失败: {error}", error.message)
    }
    settingsList.filter(
        item => item.name === 'config_uid'
    ).forEach(item => {
        // 刷新settings自动设置执行
        item.label = "当前配置uid:\{" + Record.uid + "\}\n(仅仅显示配置uid无其他作用)"
    })
    file.writeTextSync(manifest.settings_ui, JSON.stringify(settingsList))

    // ===== 保存 PATH_JSON_LIST（按 uid）=====
    try {
        let pathJsonMap = new Map();

        try {
            const raw = JSON.parse(file.readTextSync(json_path_name.pathJsonByUid));
            pathJsonMap = new Map(raw);
        } catch (e) {
            log.debug("PATH_JSON_LIST 映射文件不存在，将新建");
        }

        pathJsonMap.set(Record.uid, PATH_JSON_LIST);

        file.writeTextSync(
            json_path_name.pathJsonByUid,
            JSON.stringify([...pathJsonMap])
        );

        log.info(
            "[PATH] 已保存 PATH_JSON_LIST，uid={0}，count={1}",
            Record.uid,
            PATH_JSON_LIST.length
        );
    } catch (e) {
        log.error("[PATH] 保存 PATH_JSON_LIST 失败: {0}", e.message);
    }
    if (settings.refresh_record) {
        if (settings.refresh_record_mode === "UID") {
            RecordList = RecordList.filter(item => item.uid !== Record.uid)
            RecordPathList = RecordPathList.filter(item => item.uid !== Record.uid)
            file.writeTextSync(json_path_name.RecordPathText, JSON.stringify(RecordPathList))
            file.writeTextSync(json_path_name.RecordText, JSON.stringify(RecordList))
            log.info("已清空UID:{0}记录文件", Record.uid)
            return
        }
        file.writeTextSync(json_path_name.RecordPathText, JSON.stringify([]))
        file.writeTextSync(json_path_name.RecordText, JSON.stringify([]))
        log.info("已清空全部记录文件")
    }
}


/**
 * 初始化用户ID设置映射表
 * @param {Map} uidSettingsMap - 用于存储用户ID设置的Map对象
 * @returns {Map} 返回初始化后的用户ID设置映射表
 */
async function initUidSettingsMap(uidSettingsMap) {
    // 获取用户设置JSON文件的路径
    const uidSettingsJson = json_path_name.uidSettingsJson;
    try {
        // 读取并解析JSON文件内容，转换为Map对象
        const existingData = JSON.parse(file.readTextSync(uidSettingsJson))
        uidSettingsMap = new Map(existingData)
    } catch (e) {
        // 文件不存在时使用空Map
        log.debug("配置文件不存在，将创建新的");
    }
    return uidSettingsMap;
}

/**
 * 加载用户ID设置映射表
 * @param {Map} uidSettingsMap - 用户ID到设置项的映射表
 */
async function loadUidSettingsMap(uidSettingsMap) {
    // 从映射表中获取当前用户的设置
    let uidSettings = uidSettingsMap.get(Record.uid);
    // 如果存在用户设置
    if (uidSettings) {
        if (!loadPathJsonListByUid()) {
            throw new Error(
                "未找到 PATH_JSON_LIST，请先执行一次【刷新配置】"
            );
        }
        try {
            let templateMatchSettings = JSON.parse(file.readTextSync(json_path_name.templateMatchSettingsJson));
            // 筛选出名称为'config_run'的设置项
            templateMatchSettings.filter(
                item => item.name === 'config_run'
            ).forEach(item => {
                // 刷新settings自动设置执行
                item.default = "执行"
            })
            templateMatchSettings.filter(
                item => item.name === 'config_uid'
            ).forEach(item => {
                // 刷新settings自动设置执行
                item.label = "当前配置uid:\{" + Record.uid + "\}\n(仅仅显示配置uid无其他作用)"
            })
            let filterSettings = []
            const filterUidSettings = uidSettings.filter(item => item?.name?.startsWith(levelName))
            let tempFilterUidSettings = []

            let last_parent_level = undefined
            for (let i = 0; i < filterUidSettings.length; i++) {
                let item = filterUidSettings[i]
                // log.warn(`item:{0}`,item)
                if (item?.name?.startsWith(levelName)) {
                    if (i === 0) {
                        tempFilterUidSettings.push({type: "separator"})
                        tempFilterUidSettings.push({type: "separator"})
                    }

                    tempFilterUidSettings.push({type: "separator"})

                    let parent_level = item?.name?.replace(levelName + "_", "")?.split("_")[0] || undefined
                    if (i !== 0 && parent_level && parent_level !== last_parent_level) {
                        tempFilterUidSettings.push({type: "separator"})
                    }
                    tempFilterUidSettings.push(item)
                    last_parent_level = parent_level
                }
            }
            // log.debug("用户配置: {0}", tempFilterUidSettings)
            filterSettings = tempFilterUidSettings
            // filterSettings = filterUidSettings
            // templateMatchSettings = Array.from(new Set(templateMatchSettings).difference(new Set(filterUidSettings)))
            try {
                loadingLevel = parseInt(settings.loading_level)
            } catch (e) {
                log.warn("配置 {0} 错误，将使用默认值{0}", "加载路径层级", loadingLevel)
            } finally {
                //
                loadingLevel = loadingLevel < 1 ? 2 : loadingLevel
            }
            //todo: 高阶层级过滤
            const highLevelFiltering = settings.high_level_filtering || undefined
            if (highLevelFiltering && highLevelFiltering?.trim() !== "") {
                /**
                 * 实例：pathing\地方特产\
                 * 地方特产
                 * 实例：pathing\地方特产\枫丹\
                 * 地方特产->枫丹
                 * 实例：pathing\地方特产\枫丹\幽光星星\
                 * 地方特产->枫丹->幽光星星
                 * 实例：pathing\地方特产\枫丹\幽光星星\幽光星星@jbcaaa\
                 * 地方特产->枫丹->幽光星星->幽光星星@jbcaaa
                 */
                let keys = new Set([])

                if (highLevelFiltering) {
                    const set = new Set(highLevelFiltering.split("->"));
                    keys = keys.union(set)
                }

                // function countMatchingElements(mainSet, subset) {
                //     const mainSetObj = new Set(mainSet);
                //     return subset.filter(item => mainSetObj.has(item)).length;
                // }

                // const key = keys[keys.size - 1]
                // PATH_JSON_LIST.filter(item => item.level > 0)
                // 预先建立 levelName 到路径信息的映射
                const levelNameMap = new Map();
                PATH_JSON_LIST.forEach(item => {
                    log.debug(`item:{0}`, JSON.stringify(item))
                    if (item.levelName) {
                        levelNameMap.set(item.levelName, item);
                    }
                });
                log.warn("levelNameMap:{0}", JSON.stringify([...levelNameMap]))
                //中间一段路径名称
                const dir_key = Array.from(keys).join("\\")
                filterSettings = filterSettings.filter(item => {
                    if (!item?.name?.startsWith(levelName)) {
                        return true
                    }
                    // const settings_level = PATH_JSON_LIST.filter(list_item => list_item.levelName === item.name).find();
                    const settings_level = levelNameMap.get(item.name);
                    if (settings_level) {
                        //只加载指定目录
                        return (settings_level.path.includes(dir_key))
                    }
                    return false
                })
            }
            const theLayer = settings.the_layer || false
            const levelSettings = filterSettings.filter(item => {
                if (!item?.name?.startsWith(levelName)) {
                    return true
                }
                const level_all = item.name.replaceAll(levelName + "_", "");
                // 获取级别
                const level = level_all.split("_").map(parseInt)[0]
                if (theLayer) {
                    //只加载指定级别的设置
                    return (loadingLevel === level + 1)
                }
                // 检查级别是否小于等于加载层级
                return (loadingLevel > level - 1)
            })
            templateMatchSettings = [...templateMatchSettings, ...levelSettings]
            while (templateMatchSettings.length > 0 &&
            templateMatchSettings[templateMatchSettings.length - 1]?.type === "separator") {
                templateMatchSettings.pop();
            }

            /**
             * 限制连续的分隔符数量不超过3个
             * @param {Array} settings - 设置项数组
             * @returns {Array} 处理后的设置项数组
             */
            function limitConsecutiveSeparators(settings) {
                if (!Array.isArray(settings) || settings.length === 0) {
                    return settings;
                }

                const result = [];
                let consecutiveSeparatorCount = 0;

                for (const item of settings) {
                    if (item?.type === "separator") {
                        consecutiveSeparatorCount++;

                        // 只有当连续分隔符数量不超过3个时才添加
                        if (consecutiveSeparatorCount <= 3) {
                            result.push(item);
                        }
                    } else {
                        // 遇到非分隔符时重置计数
                        consecutiveSeparatorCount = 0;
                        result.push(item);
                    }
                }

                return result;
            }

            templateMatchSettings = limitConsecutiveSeparators(templateMatchSettings)
            // uidSettings.push(levelSettings)
            // 将更新后的设置写入配置文件
            file.writeTextSync(manifest.settings_ui, JSON.stringify(templateMatchSettings))
        } catch (e) {
            // 记录错误日志
            log.error("加载用户配置失败: {error}", e.message)
        }
    }
    // 初始化配置设置
    configSettings = await initSettings()
}

async function initRun(config_run) {
    if (!loadPathJsonListByUid()) {
        throw new Error(
            "未找到 PATH_JSON_LIST，请先执行一次【刷新配置】"
        );
    }
    log.info(`初始{0}配置`, config_run)
    const cdPath = json_path_name.cdPath;
    const timeJson = (!cd.open) ? new Set() : new Set(JSON.parse(file.readTextSync(cdPath)).sort(
        (a, b) => b.level - a.level
    ))

    const multiCheckboxMap = getRunnableMultiCheckboxMap(await getMultiCheckboxMap());
    if (dev.isDebug) {
        await debugKey('[init-run]_log-multiCheckboxMap.json', JSON.stringify(Array.from(multiCheckboxMap)))
        const keysList = Array.from(multiCheckboxMap.keys());
        await debugKey('[init-run]_log-keysList.json', JSON.stringify(keysList))
    }

    settingsNameList = settingsNameList.concat(
        Array.from(multiCheckboxMap.keys().filter(key =>
            typeof key === "string" &&
            key.startsWith(levelName) &&
            multiCheckboxMap.get(key)?.options?.length > 0
        ))
    )


    settingsNameList = settingsNameList
        .filter(key => typeof key === "string" && key.trim() !== "")

    log.debug(`settingsNameList:{0}`, JSON.stringify(settingsNameList))


    // todo:补齐执行前配置
    // ================= 执行前配置（补齐 needRunMap） =================
    await debugKey(
        '[init-run]_log-PATH_JSON_LIST.json',
        JSON.stringify(PATH_JSON_LIST)
    );

    for (const settingsName of settingsNameList) {

        // 1. 读取 multi-checkbox 的 JSON 描述
        const multiJson = multiCheckboxMap.get(settingsName);
        if (!multiJson || !multiJson.options || multiJson.options.length === 0) continue;

        const labelParentName = getBracketContent(multiJson.label); // [xxx]
        const selectedOptions = multiJson.options;

        // 2. 从 PATH_JSON_LIST 中筛选命中的路径
        const filter = PATH_JSON_LIST.filter(item => item.children.length === 0);
        await debugKey(`[init-run]_log-filtermatchedPaths.json`, JSON.stringify(filter))
        let matchedPaths = filter.filter(item => {
            const hitParent = item.fullPathNames.includes(labelParentName) || labelParentName === `${pathingName}`;
            const hitOption = selectedOptions.some(opt =>
                item.fullPathNames.some(name => name.includes(opt))
            );

            return hitParent && hitOption;
        }).map(item => {
            const selected = selectedOptions.find(opt =>
                item.fullPathNames.some(name => name.includes(opt))
            );

            return {
                level: item.level,
                name: item.name,
                id: item.id,
                parentId: item.parentId,
                parentName: item.parentName,
                rootName: item.rootName,
                selected: selected,
                path: item.path,
                fullPathNames: item.fullPathNames
            };
        });
        await debugKey(`[init-run]_log-matchedPaths.json`, JSON.stringify(matchedPaths))

        function generatedKey(item, useParent = false) {
            const separator = "->";

            if (useParent) {
                // 使用父级名称的逻辑
                if (item?.parent_name && !item?.parentName) {
                    return `${item.parent_name}${separator}${item.name}`;
                } else if (item?.parentName) {
                    return `${item.parentName}${separator}${item.name}`;
                }
            } else {
                // 三层结构的逻辑
                if (item?.rootName && item?.parentName && item?.rootName !== "" && item?.parentName !== item?.rootName) {
                    return `${item.rootName}${separator}${item.parentName}${separator}${item.name}`;
                } else if (item?.root_name && item?.parent_name && item?.root_name !== "" && item?.parent_name !== item?.root_name) {
                    return `${item.root_name}${separator}${item.parent_name}${separator}${item.name}`;
                }
                // 二层结构的逻辑
                if (item?.parent_name && !item?.parentName) {
                    return `${item.parent_name}${separator}${item.name}`;
                } else if (item?.parentName) {
                    return `${item.parentName}${separator}${item.name}`;
                }
            }

            // 默认返回名称
            return item.name;
        }

        // 3. CD 过滤（可选）
        if (cd.open && matchedPaths.length > 0) {
            let recordPaths = [];
            try {
                recordPaths = Array.from(RecordPath.paths);
            } catch (e) {
                log.error("读取记录路径失败: {error}", e.message)
            }
            recordPaths.sort((a, b) => b.timestamp - a.timestamp)

            const timeConfigs = Array.from(timeJson);
            await debugKey(`[init-run]_log-timeConfigs.json`, JSON.stringify(timeConfigs))
            await debugKey(`[init-run]_log-recordPaths.json`, JSON.stringify(recordPaths))
            let bodyList = []
            const now = Date.now();
            //首次过滤
            let cdFilterMatchedPaths = matchedPaths.filter(item => {
                const timeConfig = timeConfigs.find(cfg =>
                    item.fullPathNames.includes(cfg.name)
                );
                if (!timeConfig) return false;

                const record = recordPaths.find(r =>
                    r.path.includes(item.path)
                );
                if (!record || !record.timestamp) return false;
                switch (timeType.fromValue(timeConfig.type)) {
                    case timeType.cron:
                        // timeConfig.name
                        // const key = generatedKey(item);
                        const item_key = bodyList.find(cfg => cfg.key === item.path)
                        if (!item_key) {
                            bodyList.push({
                                key: item.path,
                                cronExpression: timeConfig.value,
                                startTimestamp: record.timestamp,
                                endTimestamp: now
                            })
                        } else if (item_key.startTimestamp < record.timestamp) {
                            item_key.startTimestamp = record.timestamp
                            item_key.cronExpression = timeConfig.value
                        }

                        return true;
                    default:
                        return true;
                }
                return true
            })
            await debugKey(`[init-run]_log-cdFilterMatchedPaths.json`, JSON.stringify(cdFilterMatchedPaths))
            //多次请求改一次请求
            const nextMap = bodyList.length <= 0 ? new Map() : await cronUtil.getNextCronTimestampAll(bodyList, cd.http_api) ?? new Map();
            await debugKey(``, JSON.stringify({nextMap: [...nextMap]}), true)
            // 还在CD中的path。Array.filter 不会等待 async 回调，这里必须保持同步判断。
            const in_cd_paths = cdFilterMatchedPaths.filter(item => {
                const timeConfig = timeConfigs.find(cfg =>
                    item.fullPathNames.includes(cfg.name)
                );

                switch (timeType.fromValue(timeConfig.type)) {
                    case timeType.hours: {
                        const record = recordPaths.find(r =>
                            r.path.includes(item.path)
                        );
                        if (!record || !record.timestamp) return false;
                        const diff = getTimeDifference(record.timestamp, now);
                        return (diff.total.hours < timeConfig.value);
                    }
                    case timeType.cron: {
                        // const next = await cronUtil.getNextCronTimestamp(
                        //     `${timeConfig.value}`,
                        //     record.timestamp,
                        //     now,
                        //     cd.http_api
                        // );
                        // return (next && now >= next);
                        // const key = generatedKey(item);
                        const cron_ok = nextMap.get(item.path)
                        if (!cron_ok) {
                            log.warn("CD HTTP结果缺少路径，跳过CD过滤继续执行: {path}", item.path)
                            return false
                        }
                        return cron_ok.ok === false; // 不应该在CD中时返回true
                    }
                    default:
                        return false;
                }
            });
            await debugKey(`[init-run]_log-in_cd_paths.json`, JSON.stringify(in_cd_paths))
            // 移除 CD 未到的路径
            if (in_cd_paths.length > 0) {
                const cdPathSet = new Set(in_cd_paths.map(item => item.path));
                matchedPaths = matchedPaths.filter(item => !cdPathSet.has(item.path));
            }

        }

        // 4. 写入 needRunMap
        if (matchedPaths.length > 0) {
            //锄地队对应
            try {
                // {
                //   uid:"",
                //   parent_name:"",
                //   root_name: "",
                //   name:"",
                //   team_name:""
                // } json支持
                let teamHoeGroundList = JSON.parse(file.readTextSync(json_path_name.HoeGround)) ?? [{
                    uid: "",
                    is_common: false,
                    parent_name: undefined,
                    root_name: undefined,
                    name: undefined,
                    team_name: ""
                }]
                teamHoeGroundList = teamHoeGroundList.filter(item => item?.uid === Record.uid || item?.is_common)
                teamHoeGroundList.sort((a, b) => {
                    const orderA = a?.is_common ? 1 : 0;
                    const orderB = b?.is_common ? 1 : 0;
                    return orderB - orderA; // 这样 is_common 为 true 的会排在前面
                });
                // 自定义锄地队对应可覆盖公共锄地队对应
                teamHoeGroundList.forEach(item => {
                    if (item?.root_name?.trim() !== "") {
                        const key = generatedKey(item);
                        team.HoeGroundMap.set(key, item.team_name);

                    } else {
                        const key_parent = generatedKey(item, true);
                        team.HoeGroundMap.set(key_parent, item.team_name);
                    }

                })
                log.info(`{0}加载完成`, json_path_name.HoeGround)
            } catch (e) {
                log.error(`加载失败:{0}`, e.message)
            }
            //输入值优先覆盖
            try {
                const teamHoeGroundStr = settings.team_hoe_ground || "parentName->name=key"
                teamHoeGroundStr.split(",").forEach(item => {
                    const [key, team_name] = item.split("=");
                    team.HoeGroundMap.set(key, team_name)
                })
            } catch (e) {
                log.error(`加载失败:{0}`, e.message)
            }

            //   排序
            const orderMap = new Map()
            try {
                // {
                //   uid:"",
                //   parent_name:"",
                //   root_name: "",
                //   name:"",
                //   order:0
                // } json支持
                let orderList = JSON.parse(file.readTextSync(json_path_name.PathOrder)) ?? [{
                    uid: "",
                    is_common: false,
                    parent_name: undefined,
                    root_name: undefined,
                    name: undefined,
                    order: 0
                }]
                orderList = orderList.filter(item => item?.uid === Record.uid || item?.is_common)
                orderList.sort((a, b) => {
                    const orderA = a?.is_common ? 1 : 0;
                    const orderB = b?.is_common ? 1 : 0;
                    return orderB - orderA; // 这样 is_common 为 true 的会排在前面
                });
                // 自定义排序可覆盖公共排序
                orderList.forEach(item => {
                    if (item.root_name) {
                        const key = generatedKey(item);
                        orderMap.set(key, item.order)
                    } else {
                        const key_parent = generatedKey(item, true);
                        orderMap.set(key_parent, item.order)
                    }
                })
                log.info(`{0}加载完成`, json_path_name.PathOrder)
            } catch (e) {
                log.error(`加载失败:{0}`, e.message)
            }
            await debugKey("[init-run]_log-orderMap-By-json.json", JSON.stringify([...orderMap]))
            //输入值优先覆盖
            try {
                // 支持多条规则，例如: "rootName->parentName->name1=1,rootName->parentName->name2=2"
                const orderStr = settings.order_rules || "rootName->parentName->name=1"
                orderStr.split(",").forEach(item => {
                    const [key, order] = item.split("=");
                    orderMap.set(key, parseInt(order))
                })
            } catch (e) {
                log.error(`加载失败:{0}`, e.message)
            }
            await debugKey("[init-run]_log-orderMap-All.json", JSON.stringify([...orderMap]))
            //限制组最大执行数
            const openLimitMax = settings.open_limit_max
            let limitMaxByGroup = new Map()
            if (openLimitMax) {
                log.info(`{0}`, '已开启限制组最大执行数')
                try {
                    let limitMaxList = JSON.parse(file.readTextSync(json_path_name.LimitMax)) ?? [{
                        uid: "",
                        is_common: false,
                        parent_name: undefined,
                        root_name: undefined,
                        name: undefined,
                        max: 0
                    }]
                    limitMaxList = limitMaxList.filter(item => item?.uid === Record.uid || item?.is_common)
                    limitMaxList.sort((a, b) => {
                        const orderA = a?.is_common ? 1 : 0;
                        const orderB = b?.is_common ? 1 : 0;
                        return orderB - orderA; // 这样 is_common 为 true 的会排在前面
                    });
                    // 自定义排序可覆盖公共排序
                    limitMaxList.forEach(item => {
                        if (item?.root_name?.trim() !== "") {
                            const key = generatedKey(item);
                            limitMaxByGroup.set(key, parseInt(item.max))
                            log.debug(`limitMaxList=>{0}->{1}`, key, item.max)
                        } else {
                            const key_parent = generatedKey(item, true);
                            limitMaxByGroup.set(key_parent, parseInt(item.max))
                            log.debug(`limitMaxList=>{0}->{1}`, key_parent, item.max)
                        }
                    })

                    //输入值优先覆盖
                    try {
                        // 支持多条规则，例如: "rootName->parentName->name1=1,rootName->parentName->name2=2"
                        const limitMaxStr = settings.limit_max_group || "rootName->parentName->name=1"
                        limitMaxStr.split(",").forEach(item => {
                            const [key, max] = item.split("=");
                            limitMaxByGroup.set(key, parseInt(max))
                        })
                    } catch (e) {
                        log.error(`加载失败:{0}`, e.message)
                    }
                } catch (e) {
                    log.error(`加载失败:{0}`, e.message)

                }
            }


            function groupByParentAndName(list) {
                const map = new Map();

                list.forEach(item => {
                    // const key = `${item.parentName}->${item.name}`;
                    const key = generatedKey(item);
                    // const key_parent = generatedKey(item, true);
                    if (!map.has(key)) map.set(key, []);
                    map.get(key).push(item);
                    // if (!map.has(key_parent)) map.set(key_parent, []);
                    // map.get(key_parent).push(item);
                });

                return Array.from(map.values()); // 转成二维数组 [[], []]
            }

            let groups = groupByParentAndName(matchedPaths);
            groups.sort((a, b) => {
                const a_key = generatedKey(a)
                const b_key = generatedKey(b)
                const orderA = orderMap.get(a_key) ?? 0; // 没在 JSON 中的排到最后
                const orderB = orderMap.get(b_key) ?? 0;
                if (orderA === orderB) {
                    return a_key?.localeCompare(b_key);
                }
                return orderB - orderA; // 修改为倒序数字比较
            })
            const asMap = new Map()
            groups.forEach(group => {
                const groupOne = group[0]
                let groupKey_parent = generatedKey(groupOne, true);
                if (orderMap.has(groupKey_parent) || team.HoeGroundMap.has(groupKey_parent)) {
                    let groupKey = generatedKey(groupOne);
                    asMap.set(groupKey, groupKey_parent)
                }
            })
            groups.forEach(group => {
                const groupOne = group[0]
                let groupKey = generatedKey(groupOne);
                let key = groupKey
                if (asMap.has(key)) {
                    key = asMap.get(key)
                }
                let runGroup = group
                //限制组最大执行数
                if (openLimitMax && (limitMaxByGroup.has(key) || limitMaxByGroup.has(groupKey))) {
                    const limitMax = (limitMaxByGroup.get(key) || limitMaxByGroup.get(groupKey)) ?? 99999
                    const max = Math.min(group.length, limitMax)
                    runGroup = group.slice(0, max)
                    log.debug("[限制组最大执行数] groupKey={0},max={1},limitMax={2},group.length={3}", key, max, limitMax, group.length)
                }
                needRunMap.set(key, {
                    order: (orderMap.get(key) || orderMap.get(groupKey)) ?? 0,
                    paths: runGroup,
                    parent_name: groupOne.parentName,
                    key: key,
                    current_name: groupOne.name,
                    name: settingsName //多选项 名称 如 treeLevel_0_0
                });
            })
            // todo 对 needRunMap 进行排序
            // 对 needRunMap 进行整体排序
            const sortedNeedRunMap = new Map(
                [...needRunMap.entries()].sort((a, b) => {
                    // 使用值中的 order 字段进行排序
                    const orderA = a[1].order ?? 0;
                    const orderB = b[1].order ?? 0;

                    // 按降序排序（数值大的优先）
                    return orderB - orderA;
                })
            );

            // 替换原来的 needRunMap
            needRunMap.clear();
            for (const [key, value] of sortedNeedRunMap) {
                needRunMap.set(key, value);
            }


            await debugKey(
                '[init-run]_log-needRunMap.json',
                JSON.stringify([...needRunMap])
            );
        }
        log.info("[执行前配置完成] needRunMap.size={0}", needRunMap.size);
    }
}

async function init() {
    let settingsConfig = await initSettings(`${config_root}\\`);
    let utils = [
        "cron",
        "SwitchTeam",
    ]
    for (let util of utils) {
        eval(file.readTextSync(`utils/${util}.js`));
    }
    if (manifest.key !== settings.key) {
        let message = "密钥不匹配";
        if (settings.key) {
            message += ",脚本可能存在升级 密钥已经改变 请查看文档功能后重新获取密钥"
        }
        throw new Error(message)
    }
    auto.semi = settings.mode === "半自动"
    if (auto.semi) {
        auto.run = settings.auto_semi_key_mode === "继续运行"
        auto.skip = settings.auto_semi_key_mode === "跳过"
        auto.key = settings.auto_key
        if (!auto.key) {
            throw new Error(settings.mode + "模式下必须开启快捷键设置")
        }
    }

    dev.debug = (dev.debug) ? dev.debug : settings.debug
    dev.isDebug = settings.is_debug

    cd.open = (cd.open) ? cd.open : settings.cd_open
    cd.http_api = (cd.http_api) ? cd.http_api : settings.http_api
    await debugKey("[init]_log-cd-settings.json", JSON.stringify(cd))
    config_list.black = settings.config_black_list ? settings.config_black_list.split(",") : []
    config_list.white = settings.config_white_list ? settings.config_white_list.split(",") : []


    if (!file.IsFolder(`${pathingName}`)) {
        let batFile = "SymLink.bat";
        log.error("{0}文件夹不存在，请在BetterGI中右键点击本脚本，选择{1}。然后双击脚本目录下的{2}文件以创建文件夹链接", `${pathingName}`, "打开所在目录", batFile);
        return false;
    }
    try {
        let parse = JSON.parse(file.readTextSync(json_path_name.SevenElement));
        if (parse) {
            parse?.sort((a, b) => a.level - b.level)
            SevenElement.SevenElements = Array.from(new Set(SevenElement.SevenElements.concat(parse.map(item => item.name))))
            parse.forEach(item => {
                const name = item.name
                let value = item.value
                if (SevenElement.SevenElementsMap.has(name)) {
                    value = Array.from(new Set(SevenElement.SevenElementsMap.get(name).concat(value)))
                }
                SevenElement.SevenElementsMap.set(name, value)
            })
        }
    } catch (e) {
        log.warn("[SevenElement]初始化失败error:{0}", e.message)

    }
    //记录初始化
    await initRecord();

    // 读取现有配置并合并
    let uidSettingsMap = new Map()
    uidSettingsMap = await initUidSettingsMap(uidSettingsMap);

//总控
//刷新settings
    const config_run = settings.config_run;
    log.info("开始执行配置: {config_run}", config_run)
    if (config_run === "刷新") {
        await initRefresh(settingsConfig);
        log.info("配置{0}完成", config_run)
    } else if (config_run === "加载") {
        //直接从配置文件中加载对应账号的配置
        await loadUidSettingsMap(uidSettingsMap);
        log.info("配置{0}完成", config_run)
    } else
        // 初始化needRunMap
    if (config_run === "执行") {
        await initRun(config_run);
        await realTimeMissions()
    }
    return true
}

(async function () {
    try {
        if (await init()) {
            await main()
        }
    } finally {
        await saveRecordPaths();
        await saveRecord();
    }

})()

async function main() {
    let lastRunMap = new Map()

    function chooseBestRun() {
        if (settings.choose_best && RecordLast.paths.size > 0) {
            // 由于在迭代过程中删除元素会影响迭代，先收集要删除的键
            const keysToDelete = [];
            // 优先跑上次没跑过的路径
            // 使用 Set 提高性能
            const lastListSet = new Set([...RecordLast.paths]);

            for (const [key, one] of needRunMap.entries()) {
                // 检查当前任务的路径是否都不在上次执行的路径中
                const allPathsInLast = one.paths.every(pathObj => lastListSet.has(pathObj.path));

                if (!allPathsInLast) {
                    lastRunMap.set(key, one);
                    keysToDelete.push(key);
                }
            }


            // 然后批量删除
            for (const key of keysToDelete) {
                needRunMap.delete(key);
            }
            //保持排序
            // 对 lastRunMap 进行整体排序
            const sortedNeedRunMap = new Map(
                [...lastRunMap.entries()].sort((a, b) => {
                    // 使用值中的 order 字段进行排序
                    const orderA = a[1].order ?? 0;
                    const orderB = b[1].order ?? 0;

                    // 按降序排序（数值大的优先）
                    return orderB - orderA;
                })
            );

            // 替换原来的 lastRunMap
            lastRunMap.clear();
            for (const [key, value] of sortedNeedRunMap) {
                lastRunMap.set(key, value);
            }
        }
    }

    chooseBestRun();
    await debugKey("[run]_log-lastRunMap.json", JSON.stringify([...lastRunMap]))
    await debugKey("[run]_log-needRunMap.json", JSON.stringify([...needRunMap]))

    if (needRunMap.size > 0) {
        if (settings.choose_best) {
            log.info(`[{0}] [{1}]`, settings.mode, "择优模式-启动")
        }
        await runMap(needRunMap)
    }
    if (lastRunMap.size > 0) {
        log.info(`[{0}] [{1}]`, settings.mode, "择优模式-收尾")
        await runMap(lastRunMap)
    }

    // if (needRunMap.size <= 0 && lastRunMap.size <= 0) {
    //     log.info(`设置目录{0}完成`, "刷新")
    // }
    // log.info(`[{mode}] path==>{path},请按下{key}以继续执行[${manifest.name} JS]`, settings.mode, "path", AUTO_STOP)
    // await keyMousePressStart(AUTO_STOP);
    // log.info(`[{mode}] path==>{path},请按下{key}以继续执行[${manifest.name} JS]`, settings.mode, "path", AUTO_STOP)
}

/**
 * 保存记录路径的函数
 * 该函数将RecordPath对象中的Set类型数据转换为数组后保存到文件
 */
async function saveRecordPaths() {
    // 保存前将 Set 转换为数组，因为JSON不支持Set类型
    // 创建一个新的记录对象，包含原始记录的所有属性
    const recordToSave = {
        // 使用展开运算符复制Record对象的所有属性，保持其他数据不变
        ...RecordPath,
        // 处理 paths 数组
        paths: (() => {
            // 1. 使用 Map 来辅助去重，Map 的 key 是 path，value 是完整的 item 对象
            const pathMap = new Map();

            // 假设 RecordPath.paths 是一个 Set，先转为数组进行遍历
            [...RecordPath.paths].forEach(item => {
                // 获取当前项的路径字符串
                const currentPath = item.path;

                // 检查 Map 中是否已经存在该路径
                if (pathMap.has(currentPath)) {
                    // 如果存在，比较时间戳
                    const existingItem = pathMap.get(currentPath);
                    // 如果当前项的时间戳比已存在的大，则更新 Map 中的值
                    if (item.timestamp > existingItem.timestamp) {
                        pathMap.set(currentPath, item);
                    }
                } else {
                    // 如果不存在，直接存入 Map
                    pathMap.set(currentPath, item);
                }
            });

            // 2. 将 Map 中的值（去重后的对象数组）转换回我们需要的格式
            return Array.from(pathMap.values()).map(item => ({
                timestamp: item.timestamp,
                path: item.path
            }));
        })()
    };

    // 确保 RecordPathList 是数组
    if (!Array.isArray(RecordPathList)) {
        RecordPathList = Array.from(RecordPathList);
    }
    let temp = RecordPathList.find(item => item.uid === Record.uid)
    if (temp) {
        // RecordList.splice(RecordList.indexOf(temp),1)
        temp.paths = [...recordToSave.paths, ...temp.paths]
        // temp.errorPaths = [...recordToSave.errorPaths, ...temp.errorPaths]
        // temp.groupPaths = [...recordToSave.groupPaths, ...temp.groupPaths]
    } else {
        // 将记录对象添加到记录列表中
        RecordPathList.push(recordToSave)
    }
    // 将记录列表转换为JSON字符串并同步写入文件
    file.writeTextSync(json_path_name.RecordPathText, JSON.stringify(RecordPathList))
    log.info("saveRecordPath保存记录文件成功")
}

/**
 * 保存当前记录到记录列表并同步到文件
 * 该函数在保存前会将Set类型的数据转换为数组格式，确保JSON序列化正常进行
 */
async function saveRecord() {
    // 保存前将 Set 转换为数组
    // 创建一个新的记录对象，包含原始记录的所有属性
    const recordToSave = {
        // 使用展开运算符复制Record对象的所有属性
        ...Record,
        // 将paths Set转换为数组
        paths: [...Record.paths],
        // 将errorPaths Set转换为数组
        errorPaths: [...Record.errorPaths],
        // 将groupPaths Set转换为数组，并对每个元素进行特殊处理
        groupPaths: [...Record.groupPaths].map(item => ({
            // 保留name属性
            name: item.name,
            // 将item中的paths Set转换为数组
            paths: [...item.paths]
        }))
    };

    // 将记录对象添加到记录列表中
    RecordList.push(recordToSave)
    // 将记录列表转换为JSON字符串并同步写入文件
    file.writeTextSync(json_path_name.RecordText, JSON.stringify(RecordList))
    log.info("saveRecord保存记录文件成功")
}

/**
 * 计算两个时间之间的差值，并返回指定格式的JSON
 * @param {number|Date} startTime - 开始时间（时间戳或Date对象）
 * @param {number|Date} endTime - 结束时间（时间戳或Date对象）
 * @returns {Object} diff_json - 包含info和total的对象
 */
function getTimeDifference(startTime, endTime) {
    // 确保输入是时间戳
    const start = typeof startTime === 'object' ? startTime.getTime() : startTime;
    const end = typeof endTime === 'object' ? endTime.getTime() : endTime;

    // 计算总差值（毫秒）
    const diffMs = Math.abs(end - start);

    // 计算总时间（小数）
    const totalSeconds = diffMs / 1000;
    const totalMinutes = totalSeconds / 60;
    const totalHours = totalSeconds / 3600;

    // 计算info部分（整数）
    const infoHours = Math.floor(totalHours % 24);
    const remainingAfterHours = (totalHours % 24) - infoHours;
    const infoMinutes = Math.floor(remainingAfterHours * 60);
    const remainingAfterMinutes = (remainingAfterHours * 60) - infoMinutes;
    const infoSeconds = Math.floor(remainingAfterMinutes * 60);
// 输出类似：
// {
//     info: { hours: 1, minutes: 0, seconds: 0 },
//     total: { hours: 1, minutes: 60, seconds: 3600 }
// }
    const diff_json = {
        info: {
            hours: infoHours,
            minutes: infoMinutes,
            seconds: infoSeconds
        },
        total: {
            hours: parseFloat(totalHours.toFixed(6)),
            minutes: parseFloat(totalMinutes.toFixed(6)),
            seconds: parseFloat(totalSeconds.toFixed(6))
        }
    };

    return diff_json;
}


/**
 * 初始化记录函数
 * 该函数用于初始化一条新的记录，包括设置UID、时间戳和调整后的日期数据
 * 同时会检查记录列表中是否存在相同UID的最新记录，并进行更新
 */
async function initRecord() {
    await toMainUi()
    // 设置记录的唯一标识符，通过OCR技术获取
    Record.uid = await genshin.uid()
    // 设置记录的时间戳为当前时间
    Record.timestamp = Date.now()
    // 获取并设置调整后的日期数据
    Record.data = getAdjustedDate()

    try {
        // 尝试读取记录文件
        // 读取后将数组转换回 Set，处理特殊的数据结构
        RecordList = JSON.parse(file.readTextSync(json_path_name.RecordText), (key, value) => {
            // 处理普通路径集合
            if (key === 'paths' || key === 'errorPaths') {
                return new Set(value);
            }
            // 处理分组路径集合，保持嵌套的Set结构
            if (key === 'groupPaths') {
                return new Set(value.map(item => ({
                    name: item.name,
                    paths: new Set(item.paths)
                })));
            }
            return value;
        }) ?? RecordList;
    } catch (e) {
        // 如果读取文件出错，则忽略错误（可能是文件不存在或格式错误）
    }
    try {
        // 尝试读取记录文件
        // 读取后将数组转换回 Set，处理特殊的数据结构
        RecordPathList = JSON.parse(file.readTextSync(json_path_name.RecordPathText), (key, value) => {
            // 处理分组路径集合，保持嵌套的Set结构
            if (key === 'paths') {
                return new Set(value.map(item => ({
                    timestamp: item.timestamp,
                    path: item.path
                })));
            }
            return value;
        }) ?? RecordPathList
        RecordPath = RecordPathList.find(item => item.uid === Record.uid) ?? RecordPath
    } catch (e) {
        // 如果读取文件出错，则忽略错误（可能是文件不存在或格式错误）
    }
    RecordPath.uid ||= Record.uid
    RecordPath.paths ||= new Set()
    // 如果记录列表不为空，则查找最新记录
    if (RecordList.length > 0) {
        // 最优解：一次遍历找到最新的记录
        let latestRecord = undefined;
        // 遍历记录列表，查找相同UID且时间戳最大的记录
        for (const item of RecordList) {
            // 检查当前记录项的UID是否匹配，并且是最新记录
            if (item.uid === Record.uid &&
                // 如果还没有找到记录，或者当前记录的时间戳比已找到的记录更新
                (!latestRecord || item.timestamp > latestRecord.timestamp)) {
                // 更新最新记录为当前项
                latestRecord = item;
            }
        }
        // 如果找到最新记录，则更新RecordLast；否则保持原有RecordLast的值
        RecordLast = latestRecord ? latestRecord : RecordLast;
    }
    if (RecordLast.uid === Record.uid && Record.data === RecordLast.data) {
        // 判断是否为同一天 合并跑过的数据
        // 确保 RecordLast 的 Set 属性存在
        if (!RecordLast.paths || !(RecordLast.paths instanceof Set)) {
            RecordLast.paths = new Set();
        }
        if (!RecordLast.groupPaths || !(RecordLast.groupPaths instanceof Set)) {
            RecordLast.groupPaths = new Set();
        }

        // 判断是否为同一天 合并跑过的数据
        Record.paths = new Set([...Record.paths, ...RecordLast.paths])
        Record.groupPaths = new Set([...Record.groupPaths, ...RecordLast.groupPaths])
        // 删除RecordLast
        const index = RecordList.indexOf(RecordLast);
        if (index > -1) {
            RecordList.splice(index, 1);
        }
    }
}

function getAdjustedDate() {
    const now = new Date();
    // 减去4小时（4 * 60 * 60 * 1000 毫秒）
    now.setHours(now.getHours() - 4);

    const year = now.getFullYear();
    const month = String(now.getMonth() + 1).padStart(2, '0');
    const day = String(now.getDate()).padStart(2, '0');

    return `${year}-${month}-${day}`;
}

// 解析日期字符串
function parseDate(dateString) {
    const [year, month, day] = dateString.split('-').map(Number);
    return new Date(year, month - 1, day); // month - 1 因为月份从0开始
}

// Record.groupPaths.add({
//     name: "",
//     paths:new Set()
// })
// const pathingALLSize = []
// 使用函数来添加唯一元素
// 优化后的函数
function addUniquePath(obj, list = PATHING_ALL) {
    const existingIndex = list.findIndex(item =>
        item.level === obj.level && item.name === obj.name && item.parentName === obj.parentName && item.rootName === obj.rootName
    );

    if (existingIndex === -1) {
        list.push(obj);
    } else {
        // 合并 child_names 数组，避免重复元素
        const existingItem = list[existingIndex];
        if (obj.child_names) {
            const newChildren = obj.child_names || [];

            // 使用 Set 去重并合并数组
            const combinedChildren = [...new Set([
                ...(existingItem.child_names || []),
                ...newChildren
            ])];

            existingItem.child_names = combinedChildren;
        } else {
            const newChildren = obj.children || [];

            // 使用 Set 去重并合并数组
            const combinedChildren = [...new Set([
                ...(existingItem.children || []),
                ...newChildren
            ])];

            existingItem.children = combinedChildren;
        }

    }
}

/**
 * 初始化设置函数
 * 从配置文件中读取设置信息并返回
 * @returns {Object} 返回解析后的JSON设置对象
 */
async function initSettings(prefix = undefined) {
    // 默认设置文件路径
    let settings_ui = "settings.json";
    if (!configSettings) {
        try {
            // 读取并解析manifest.json文件
            manifest = manifest ? manifest : JSON.parse(file.readTextSync(manifest_json));
            // 调试日志：输出manifest内容
            // log.debug("manifest={key}", manifest);
            // 调试日志：输出manifest中的settings_ui配置
            log.debug("settings_ui={key}", manifest.settings_ui);
            log.info(`|脚本名称:{name},版本:{version}`, manifest.name, manifest.version);
            if (manifest.bgi_version) {
                log.info(`|最小可执行BGI版本:{bgi_version}`, manifest.bgi_version);
            }
            log.info(`|脚本作者:{authors}\n`, manifest.authors.map(a => a.name).join(","));
            // 更新settings_ui变量为manifest中指定的路径
            settings_ui = manifest.settings_ui
            settings_ui = prefix ? prefix + settings_ui : settings_ui
        } catch (error) {
            // 捕获并记录可能的错误
            log.warn("{error}", error.message);
        }
    }
    // 读取并解析设置文件
    const settingsJson = JSON.parse(file.readTextSync(settings_ui));
    // 如果configSettings未定义，则将其设置为解析后的设置对象
    if (!configSettings) {
        configSettings = settingsJson
    }
    // 调试日志：输出最终解析的设置对象
    log.debug("settingsJson={key}", settingsJson);
    // 返回设置对象
    return settingsJson
}

/**
 * 获取多复选框的映射表
 * 该函数会从初始化的设置中提取所有类型为"multi-checkbox"的条目，
 * 并将这些条目的名称和对应的选项值存储在一个Map对象中返回
 * @returns {Promise<Map>} 返回一个Promise对象，解析为包含多复选框配置的Map
 */
async function getMultiCheckboxMap() {
    // 如果configSettings存在则使用它，否则调用initSettings()函数获取
    const settingsJson = await initSettings();
    // 创建一个新的Map对象用于存储多复选框的配置
    // Map结构为: {名称: 选项数组}
    let multiCheckboxMap = new Map([]);
    // await debugKey("log-MultiCheckbox-settings.json",JSON.stringify(settingsJson))
    // 遍历设置JSON中的每个条目
    settingsJson.forEach((entry) => {
        // 如果条目没有name属性或者类型不是"multi-checkbox"，则跳过该条目
        if (!entry.name || entry.type !== "multi-checkbox") return;
        // 解构条目中的name和label属性，便于后续使用
        const {name, label} = entry;
        // 获取当前name对应的设置值，如果存在则转换为数组，否则使用空数组
        const options = settings[name] ? Array.from(settings[name]) : [];
        if (options.length > 0) {
            // 记录调试信息，包含名称、标签、选项和选项数量
            log.debug("name={key1},label={key2},options={key3},length={key4}", name, label, JSON.stringify(options), options.length);
            // 将名称和对应的选项数组存入Map
            // multiCheckboxMap.set(name, options);
            multiCheckboxMap.set(name, {label: `${label}`, options: options});
        }
    })
    log.debug("multiCheckboxMap={key}", JSON.stringify(Array.from(multiCheckboxMap)))
    // 返回包含多复选框配置的Map
    return multiCheckboxMap
}

function getTreeLevelIndex(name) {
    const match = `${name}`.match(/^treeLevel_(\d+)_/);
    return match ? parseInt(match[1]) : -1;
}

function getRunnableMultiCheckboxMap(multiCheckboxMap) {
    const runnableMap = new Map();
    const treeEntries = Array.from(multiCheckboxMap.entries())
        .filter(([name, config]) =>
            typeof name === "string" &&
            name.startsWith(levelName) &&
            config?.options?.length > 0
        )
        .map(([name, config]) => ({
            name,
            level: getTreeLevelIndex(name),
            labelParentName: getBracketContent(config.label || ""),
            config
        }));

    for (const [name, config] of multiCheckboxMap.entries()) {
        if (typeof name !== "string" || !name.startsWith(levelName) || !config?.options?.length) {
            runnableMap.set(name, config);
            continue;
        }

        const currentLevel = getTreeLevelIndex(name);
        const runnableOptions = config.options.filter(option =>
            !treeEntries.some(entry =>
                entry.name !== name &&
                entry.level > currentLevel &&
                entry.labelParentName === option
            )
        );

        if (runnableOptions.length > 0) {
            runnableMap.set(name, {...config, options: runnableOptions});
        }
    }

    return runnableMap;
}

/**
 * 根据多选框名称获取对应的JSON数据
 * 该函数是一个异步函数，用于从复选框映射表中获取指定名称的值
 * @param {string} name - 多选框的名称，用于在映射表中查找对应的值
 * @returns {Promise<any>} 返回一个Promise，解析后为找到的值，如果未找到则返回undefined
 */
async function getJsonByMultiCheckboxName(name) {
    // 获取复选框映射表，这是一个异步操作
    let multiCheckboxMap = await getMultiCheckboxMap()
    // 从映射表中获取并返回指定名称对应的值
    return multiCheckboxMap.get(name)
}

/**
 * 根据复选框组名称获取对应的值
 * 这是一个异步函数，用于从复选框映射中获取指定名称的值
 * @param {string} name - 复选框组的名称
 * @returns {Promise<any>} 返回一个Promise，解析为复选框组对应的值
 */
async function getValueByMultiCheckboxName(name) {
    // 获取复选框映射表，这是一个异步操作
    let multiCheckboxMap = await getMultiCheckboxMap()
    // 从映射表中获取并返回指定名称对应的值
    return multiCheckboxMap.get(name).options
}


/**
 * 获取字符串中第一个方括号对之间的内容
 * @param {string} str - 输入的字符串
 * @returns {string} 返回第一个方括号对之间的内容，如果没有找到则返回空字符串
 */
function getBracketContent(str) {
    // 查找第一个 [ 和最后一个 ] 的位置
    const firstBracketIndex = str.indexOf('[');
    const lastBracketIndex = str.lastIndexOf(']');

    // 检查是否找到了有效的括号对
    if (firstBracketIndex !== -1 && lastBracketIndex !== -1 && firstBracketIndex < lastBracketIndex) {
        // 提取第一个 [ 和最后一个 ] 之间的内容
        return str.substring(firstBracketIndex + 1, lastBracketIndex);
    }

    // 如果没有找到有效的括号对，返回空字符串
    return '';
}


/**
 * 调试按键函数，用于在开发者模式下暂停程序执行并等待特定按键
 * @param {string} key - 需要按下的键
 * @param {string} path - 调试信息保存的文件路径，默认为"debug.json"
 * @param {string} json - 需要写入调试文件的内容，默认为空数组
 * @returns {Promise<void>} - 异步函数，没有返回值
 */
async function debugKey(path = "debug.json", json = "", isText = false, key = dev.debug) {
    const p = "debug\\"
    // 检查是否处于调试模式
    if (dev.isDebug) {
        if (!isText) {
            log.warn("[{0}]正在写出{1}日志", '开发者模式', path)
            // 将调试信息同步写入指定文件
            file.writeTextSync(`${p}${path}`, json)
            log.warn("[{0}]写出完成", '开发者模式')
        } else {
            log.warn("[{0}]==>{1}", '开发者模式', json)
        }
        // 输出等待按键的提示信息
        log.warn("[{0}]请按下{1}继续执行", '开发者模式', key)
        // 等待用户按下指定按键
        await keyMousePressStart(key)
    }
}


/**
 * 监听指定按键的按下和释放事件
 * @param {string} key - 需要监听的按键代码
 * @param {boolean} enableSkip - 是否允许跳过监听，默认为false
 * @returns {Promise<Object>} 返回一个Promise对象，解析为包含按键状态的对象
 *   - ok: boolean - 按键是否被完整按下并释放
 *   - skip: boolean - 是否跳过监听（仅在enableSkip为true时有效）
 */
async function keyMousePress(key, enableSkip = false) {
    let press = {ok: false, skip: false}  // 初始化返回对象，记录按键状态
    const keyMouseHook = new KeyMouseHook()  // 创建按键鼠标钩子实例
    let keyDown = false    // 记录按键是否被按下
    let keyUp = false        // 记录按键是否被释放
    let down = false  //  记录按键按下事件是否触发
    let up = false    //  记录按键释放事件是否触发
    try {
        // 注册按键按下事件处理函数
        keyMouseHook.OnKeyDown(function (keyCode) {
            log.debug("{keyCode}被按下", keyCode)
            keyDown = (key === keyCode)  //  检查是否是目标按键被按下
            down = true  // 标记按键按下事件已触发
        });
        // 注册按键释放事件处理函数
        keyMouseHook.OnKeyUp(function (keyCode) {
            log.debug("{keyCode}被释放", keyCode)
            keyUp = (key === keyCode)    //  检查是否是目标按键被释放
            up = true  // 标记按键释放事件已触发
        });

        // 循环等待直到按键被按下并释放，或者跳过条件满足
        while (true) {
            if (enableSkip) {
                if (press.ok || press.skip) {
                    break;
                }
            } else if (press.ok) {
                break;
            }
            press.ok = keyDown && keyUp  // ，
            press.skip = down && up  //
            await sleep(200)  // 每次循环间隔200毫秒
        }
        return press
    } finally {
        //脚本结束前，记得释放资源！
        keyMouseHook.dispose()
    }  // 释放按键钩子资源

}

/**
 * 异步函数，用于检测特定按键的按下和释放事件
 * @param {string|number} key - 需要检测的按键代码
 * @returns {Promise<boolean>} 返回一个Promise，解析为布尔值，表示是否检测到按键的完整按下和释放过程
 */
async function keyMousePressStart(key) {
    return (await keyMousePress(key)).ok
}

/**
 * 根据索引切换队伍
 * @param {number} index - 要切换的队伍在SevenElements数组中的索引
 * @returns {Promise<void>}
 */
async function switchTeamByIndex(index, key) {
    // 获取指定索引的队伍名称，如果索引超出范围或小于0则返回undefined
    const teamName = team.SevenElements.length > index && index >= 0 ? team.SevenElements[index] : undefined;
    // 检查队伍名称是否有效
    if (!teamName || teamName === "") {
        // 如果没有设置队伍，记录调试日志并跳过切换
        log.debug(`[{mode}] 没有设置队伍: {teamName}，跳过切换`, settings.mode, teamName);
    } else if (team.current === teamName) {
        // 如果当前已经是目标队伍，记录调试日志并跳过切换
        log.debug(`[{mode}] 当前队伍为: {teamName}，无需切换`, settings.mode, teamName);
    } else {
        // 如果需要切换队伍，记录信息日志
        log.info(`[{mode}] 检测到需要: {key}，切换至{val}`, settings.mode, key, teamName);
        // 调用切换队伍的工具函数
        const teamSwitch = await switchUtil.SwitchPartyMain(teamName);
        // 如果切换成功，更新当前队伍
        if (teamSwitch) {
            team.current = teamSwitch;
        }
    }
}

async function switchTeamByName(teamName) {
    // 检查队伍名称是否有效
    if (!teamName || teamName === "") {
        // 如果没有设置队伍，记录调试日志并跳过切换
        log.debug(`[{mode}] 没有设置队伍: {teamName}，跳过切换`, settings.mode, teamName);
    } else if (team.current === teamName) {
        // 如果当前已经是目标队伍，记录调试日志并跳过切换
        log.debug(`[{mode}] 当前队伍为: {teamName}，无需切换`, settings.mode, teamName);
    } else {
        // 如果需要切换队伍，记录信息日志
        log.info(`[{mode}] 切换至{val}`, settings.mode, teamName);
        // 调用切换队伍的工具函数
        const teamSwitch = await switchUtil.SwitchPartyMain(teamName);
        // 如果切换成功，更新当前队伍
        if (teamSwitch) {
            team.current = teamSwitch;
        }
    }
}

/**
 * 执行指定路径的脚本文件
 * @param {string} path - 要执行的脚本路径
 */
async function runPath(path, root_name = "", parent_name = "", current_name = "") {
    // 参数验证
    if (!path || typeof path !== 'string') {
        log.warn('无效的路径参数: {path}', path)
        return
    }

    // 检查该路径是否已经在执行中
    if (Record.paths.has(path)) {
        log.info(`[{mode}] 路径已执行: {path}，跳过执行`, settings.mode, path)
        return
    }
    //检查战斗需求
    try {
        if (!team.fight) {
            //自动检测禁用
            // if (team.fightKeys.some(item => path.includes(`\\${item}\\`))) {
            //     team.fight = true
            // }
        }
    } catch (error) {
        log.error("检查战斗需求失败: {error}", error.message);
    }

    //切换队伍
    const hoeGroundKey = `${parent_name}->${current_name}`;
    const hoeGroundRootKey = `${root_name}->${parent_name}->${current_name}`;
    if (team.HoeGroundMap.has(hoeGroundRootKey) || team.HoeGroundMap.has(hoeGroundKey)) {
        const hoeGroundName = team.HoeGroundMap.get(hoeGroundRootKey) || team.HoeGroundMap.get(hoeGroundKey);
        await switchTeamByName(hoeGroundName);
    } else {
        const entry = [...SevenElement.SevenElementsMap.entries()].find(([key, val]) => {
            return val.some(item => path.includes(`\\${item}\\`));
        });
        if (entry) {
            const [key, val] = entry;
            const index = SevenElement.SevenElements.indexOf(key);
            await switchTeamByIndex(index, `路线需要${key}元素`);
        } else {
            if (path.includes("有草神")) {
                const idx = SevenElement.SevenElements.indexOf('草');
                await switchTeamByIndex(idx, "路线需要草神");
            }else if (team.current !== team.fightName) {
                log.info(`[{mode}] 未检测到队伍配置切换至行走位，切换至{teamName}`,settings.mode, team.fightName);
                const teamSwitch = await switchUtil.SwitchPartyMain(team.fightName);
                if (teamSwitch) {
                    team.current = teamSwitch;
                }
            }
            //自动检测已禁用
            else if (team.fight) {
                if (!team.fightName) {
                    log.error(`[{mode}] 路径需要配置好战斗策略: {path}`, settings.mode, path)
                    throw new Error(`路径需要配置好战斗策略: ` + path)
                } else if (team.current !== team.fightName) {
                    log.info(`[{mode}] 检测到需要战斗，切换至{teamName}`,settings.mode, team.fightName);
                    const teamSwitch = await switchUtil.SwitchPartyMain(team.fightName);
                    if (teamSwitch) {
                        team.current = teamSwitch;
                    }
                }
            }
        }
    }
    //切换队伍-end

    log.info("开始执行路径: {path}", path)
    await pathingScript.runFile(path)
    try {
        await sleep(1)
    } catch (e) {
        throw new Error(e.message)
    }
    if (team.fight) {
        //启用战斗
        // await dispatcher.runAutoFightTask(new AutoFightParam());
        // await realTimeMissions(false)
        // 重置战斗状态
        team.fight = false
    }
    log.debug("路径执行完成: {path}", path)

    if (auto.semi && auto.run) {
        log.warn(`[{mode}] 路径执行完成: {path}, 请按{key}继续`, settings.mode, path, auto.key)
        await keyMousePressStart(auto.key)
    }
}


/**
 * 执行给定的路径列表
 * @param {Array} list - 要执行的路径列表，默认为空数组
 * @returns {Promise<void>}
 */
async function runList(list = [], key = "", current_name = "", parent_name = "", group_key = "", group_value = "") {
    // 参数验证
    if (!Array.isArray(list)) {
        log.warn('无效的路径列表参数: {list}', list);
        return;
    }

    if (list.length === 0) {
        log.debug('路径列表为空，跳过执行');
        return;
    }
    log.info(`[{mode}] 开始执行 [{0}]组 路径列表，共{count}个路径`, settings.mode, key, list.length);
    // 遍历路径列表
    for (let i = 0; i < list.length; i++) {
        const onePath = list[i];
        const path = onePath.path;
        if (i === 0) {
            log.info(`[{mode}] 开始执行[{1}-{2}]列表`, settings.mode, parent_name, current_name);
        }
        log.info('任务组[{0}] ' + group_key + ',正在执行第{index}/{total}个路径: {path}', key, group_value, i + 1, list.length, path);
        if (auto.semi && auto.skip) {
            log.warn(`[{mode}] 按下{key}可跳过{0}执行，如不想跳过请按 空格 或 其他非功能键`, settings.mode, auto.key, path);
            const skip = await keyMousePress(auto.key, auto.skip);
            if (skip.skip) {
                log.warn(`[{mode}] 按下{key}跳过{0}执行`, settings.mode, auto.key, path);
                continue
            }
        }
        const now = Date.now();
        const value = {timestamp: now, path: path};
        try {
            // 执行单个路径，并传入停止标识
            await runPath(path, onePath.rootName, parent_name, current_name);
        } catch (error) {
            log.error('执行路径列表中的路径失败: {path}, 错误: {error}', path, error.message);
            Record.errorPaths.add(path)
            throw new Error(error.message)
            // continue; // 继续执行列表中的下一个路径
        }
        Record.paths.add(path)
        RecordPath.paths.add(value)
    }

    log.debug(`[{mode}] 路径列表执行完成`, settings.mode);
}


/**
 * 遍历并执行Map中的任务
 * @param {Map} map - 包含任务信息的Map对象，默认为新的Map实例
 * @returns {Promise<void>} - 异步执行，没有返回值
 */
async function runMap(map = new Map()) {
    // 参数验证
    if (!(map instanceof Map)) {
        log.warn('无效的Map参数: {map}', map);
        return;
    }

    if (map.size === 0) {
        log.debug('任务Map为空，跳过执行');
        return;
    }
    log.info(`========================================================`)
    log.info(`[{mode}] 开始执行任务，共{count}组任务`, settings.mode, map.size);
    //打印组执行顺序
    let index = 1
    for (const [key, one] of map.entries()) {
        log.info(`[{mode}] 任务组[{0}] 执行顺序[{1}] 执行路径数[{2}]`, settings.mode, key, index + "/" + map.size, one?.paths?.length || 0);
        index++
    }
    const group_prefix = "任务组执行顺序[{group_key}]"
    log.info(`========================================================`)
    // 遍历Map中的所有键
    index = 0
    for (const [key, one] of map.entries()) {
        index++
        if (one.paths.size <= 0) {
            continue
        }
        const group = {
            name: key,
            paths: new Set(one.paths)
        };
        try {
            // 记录开始执行任务的日志信息
            log.debug(`[{0}] {1}组 开始执行...`, settings.mode, key);
            // 执行当前任务关联的路径列表

            await runList(one.paths, key, one.current_name, one.parent_name, group_prefix, (index + "/" + map.size));

            log.debug(`[{0}] 任务[{1}]执行完成`, settings.mode, key);
        } catch (error) {
            log.error(`[{0}] 任务[{1}]执行失败: {error}`, settings.mode, key, error.message);
            // continue; // 继续执行下一个任务
            throw new Error(error.message)
        }
        Record.groupPaths.add(group)
    }

    log.debug(`[{mode}] 任务Map执行完成`, settings.mode);
}

/**
 * 获取上级文件夹名称（支持多级查找）
 * @param {string} path - 完整路径
 * @param {number} level - 向上查找的层级，默认为1（即直接上级）
 * @returns {string} 指定层级的文件夹名称，如果不存在则返回空字符串
 */
function getParentFolderName(path, level = 1) {
    if (!path || typeof path !== 'string' || level < 1) {
        return undefined;
    }

    // 统一处理路径分隔符
    const normalizedPath = path.replace(/\\/g, '/');

    // 移除末尾的斜杠
    const trimmedPath = normalizedPath.replace(/\/$/, '');

    // 按斜杠分割路径
    const pathParts = trimmedPath.split('/').filter(part => part !== '');

    // 检查是否有足够的层级
    if (level >= pathParts.length) {
        return undefined;
    }

    // 返回指定层级的上级目录名称
    return pathParts[pathParts.length - level - 1];
}

/**
 * 从根目录开始获取指定位置的文件夹名称（支持多级查找）
 * @param {string} path - 完整路径
 * @param {number} level - 从根开始向下的层级，默认为1（即第一个子目录）
 * @returns {string} 指定层级的文件夹名称，如果不存在则返回undefined
 */
function getChildFolderNameFromRoot(path, level = 1) {
    if (!path || typeof path !== 'string' || level < 1) {
        return undefined;
    }

    // 统一处理路径分隔符
    const normalizedPath = path.replace(/\\/g, '/');

    // 移除开头的斜杠（如果有）
    const trimmedPath = normalizedPath.replace(/^\/+/, '');

    // 按斜杠分割路径
    const pathParts = trimmedPath.split('/').filter(part => part !== '');

    // 检查是否有足够的层级
    if (level > pathParts.length) {
        return undefined;
    }

    // 返回从根开始指定层级的目录名称（level - 1 是因为数组索引从0开始）
    return pathParts[level - 1];
}

/**
 * 按层级对列表项进行分组
 * @param {Array} list - 包含层级信息的列表项数组
 * @returns {Array} 返回一个嵌套数组，每个子数组包含对应层级的所有项
 */
function groupByLevel(list) {
    // 找出最大层级数
    const maxLevel = Math.max(...list.map(item => item.level));

    // 创建嵌套数组结构
    const result = [];

    // 按层级分组
    for (let level = 0; level <= maxLevel; level++) {
        const levelItems = list.filter(item => item.level === level);
        result.push(levelItems);
    }

    return result;
}


/**
 * 递归读取指定路径下的文件和文件夹，构建树形结构
 * @param {string} path - 要读取的初始路径
 * @param {number} level - 当前层级（从 0 开始），默认为 0
 * @param {string} parentId - 父节点的 id（完整路径），根节点为 null
 * @param {string[]} fullPathNames - 从根到当前节点的名称路径数组
 * @param {string} isFileKey - 目标文件类型的后缀名，默认为 ".json"
 * @param {boolean} treeStructure - 是否返回树状结构（true: 树 / false: 只收集文件平铺）
 * @returns {Promise<Array>} 树形或平铺的文件/文件夹结构数组
 */
async function readPaths(
    path,
    level = 0,
    parentId = null,
    fullPathNames = [],
    isFileKey = ".json",
    treeStructure = true
) {
    const treeList = [];

    // 获取当前路径下所有文件/文件夹
    let pathSyncList = file.readPathSync(path);

    if (!Array.isArray(pathSyncList)) {
        pathSyncList = [...pathSyncList]
    }
    // log.error(JSON.stringify(...pathSyncList))
    // log.error(JSON.stringify(Array.isArray(pathSyncList)))
    //
    // 可选：按名称排序，保证同层顺序可预测（字母序或自定义规则）
    pathSyncList.sort((a, b) => a.localeCompare(b));

    for (const itemPath of pathSyncList) {
        const currentName = getFileOrFolderName(itemPath); // 请确保你有这个辅助函数，取路径最后一段
        const currentId = itemPath; // 使用完整路径作为全局唯一 id（最可靠）
        const currentFullPathNames = [...fullPathNames, currentName];

        if (itemPath.endsWith(isFileKey)) {
            // ── 是目标文件 ───────────────────────────────
            let displayName;
            let parentDisplayName;

            // 你原有的复杂名称处理逻辑（可继续保留或简化）
            const parentFolder = getParentFolderName(itemPath);

            if (!parentFolder) {
                throw new Error(`${itemPath} 没有上级目录`);
            }

            if (itemPath.includes("@")) {
                displayName = getParentFolderName(itemPath, 2);
                parentDisplayName = getParentFolderName(itemPath, 3);
            } else {
                displayName = parentFolder;
                parentDisplayName = getParentFolderName(itemPath, 2);
            }

            const fileNode = {
                id: currentId,
                name: displayName || currentName,
                parentName: parentDisplayName,
                parentId: parentId,
                path: itemPath,
                level: level + 1,
                fullPathNames: currentFullPathNames,
                levelName: "",
                isRoot: false,
                isFile: true,
                children: [] // 文件没有子节点
            };

            treeList.push(fileNode);
        } else if (file.IsFolder(itemPath)) {
            // ── 是文件夹 ───────────────────────────────
            const childNodes = await readPaths(
                itemPath,
                level + 1,
                currentId,                 // 把当前路径作为子节点的 parentId
                currentFullPathNames,      // 传递路径栈
                isFileKey,
                treeStructure
            );

            if (treeStructure) {
                // 树形结构：保留文件夹节点
                const folderNode = {
                    id: currentId,
                    name: currentName,
                    parentName: fullPathNames.length > 0 ? fullPathNames[fullPathNames.length - 1] : undefined,
                    parentId: parentId,
                    path: itemPath,
                    level: level + 1,
                    fullPathNames: currentFullPathNames,
                    levelName: "",
                    isRoot: level === 0,
                    isFile: false,
                    children: childNodes
                };
                treeList.push(folderNode);
            } else {
                // 非树形：只收集文件，文件夹本身不保留
                treeList.push(...childNodes);
            }
        }
    }

    return treeList;
}

// 辅助函数示例（请根据你的实际实现替换）
function getFileOrFolderName(fullPath) {
    // 返回路径最后一段名称
    const parts = fullPath.split(/[\\/]/);
    return parts[parts.length - 1];
}

async function treeToList(treeList = []) {
    let list = []
    for (const element of treeList) {
        // 如果是文件，添加到结果列表
        // if (element.isFile) {
        list.push(element);
        // }
        const child = element.children
        if (child && child.length > 0) {
            list.push(...await treeToList(child))
            // list = [...list, ...await treeToList(child)]
        }
    }
    return list
}
