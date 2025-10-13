/**
 * BetterGenshinImpact JavaScript API 类型声明文件
 * 此文件定义了在脚本中可用的所有全局对象和方法
 * 注意: ClearScript 绑定后类型和方法的首字母会变为小写
 */

// ==================== 全局方法 ====================

/**
 * 延迟执行（异步）
 * @param millisecondsTimeout 延迟时间（毫秒）
 */
declare function sleep(millisecondsTimeout: number): Promise<void>;

/**
 * 按下键盘按键
 * @param key 按键名称，例如 "VK_W", "VK_SPACE", "VK_LBUTTON" 等
 */
declare function keyDown(key: string): void;

/**
 * 释放键盘按键
 * @param key 按键名称
 */
declare function keyUp(key: string): void;

/**
 * 按下并释放键盘按键
 * @param key 按键名称
 */
declare function keyPress(key: string): void;

/**
 * 设置游戏窗口尺寸和 DPI
 * @param width 游戏窗口宽度（必须是16:9分辨率）
 * @param height 游戏窗口高度（必须是16:9分辨率）
 * @param dpi DPI 缩放比例，默认为 1
 */
declare function setGameMetrics(width: number, height: number, dpi?: number): void;

/**
 * 相对移动鼠标
 * @param x X 轴偏移量
 * @param y Y 轴偏移量
 */
declare function moveMouseBy(x: number, y: number): void;

/**
 * 移动鼠标到指定游戏坐标
 * @param x X 坐标（游戏窗口内）
 * @param y Y 坐标（游戏窗口内）
 */
declare function moveMouseTo(x: number, y: number): void;

/**
 * 点击指定游戏坐标
 * @param x X 坐标
 * @param y Y 坐标
 */
declare function click(x: number, y: number): void;

/**
 * 左键单击
 */
declare function leftButtonClick(): void;

/**
 * 按下左键
 */
declare function leftButtonDown(): void;

/**
 * 释放左键
 */
declare function leftButtonUp(): void;

/**
 * 右键单击
 */
declare function rightButtonClick(): void;

/**
 * 按下右键
 */
declare function rightButtonDown(): void;

/**
 * 释放右键
 */
declare function rightButtonUp(): void;

/**
 * 中键单击
 */
declare function middleButtonClick(): void;

/**
 * 按下中键
 */
declare function middleButtonDown(): void;

/**
 * 释放中键
 */
declare function middleButtonUp(): void;

/**
 * 鼠标垂直滚动
 * @param scrollAmountInClicks 滚动量（正数向上，负数向下）
 */
declare function verticalScroll(scrollAmountInClicks: number): void;

/**
 * 捕获游戏区域的图像
 * @returns 图像区域对象
 */
declare function captureGameRegion(): ImageRegion;

/**
 * 获取当前队伍中的角色名称列表
 * @returns 角色名称数组
 */
declare function getAvatars(): string[];

/**
 * 输入文本（通过剪贴板粘贴）
 * @param text 要输入的文本
 */
declare function inputText(text: string): void;

// ==================== 全局对象 ====================

/**
 * 键鼠脚本执行器
 */
declare const keyMouseScript: {
    /**
     * 执行键鼠宏 JSON
     * @param json 键鼠宏 JSON 字符串
     */
    run(json: string): Promise<void>;

    /**
     * 执行键鼠宏文件
     * @param path 文件路径（相对于脚本根目录）
     */
    runFile(path: string): Promise<void>;
};

/**
 * 自动路径追踪脚本
 */
declare const pathingScript: {
    /**
     * 执行路径追踪 JSON
     * @param json 路径追踪 JSON 字符串
     */
    run(json: string): Promise<void>;

    /**
     * 执行路径追踪文件
     * @param path 文件路径（相对于脚本根目录）
     */
    runFile(path: string): Promise<void>;

    /**
     * 从已订阅的内容中运行文件
     * @param path 在 User\AutoPathing 目录下的文件路径
     */
    runFileFromUser(path: string): Promise<void>;
};

/**
 * 原神游戏操作相关接口
 */
declare const genshin: {
    /** 游戏窗口宽度 */
    readonly width: number;
    /** 游戏窗口高度 */
    readonly height: number;
    /** 相对于 1080P 的缩放比例 */
    readonly scaleTo1080PRatio: number;
    /** 系统 DPI 缩放比例 */
    readonly screenDpiScale: number;
    /** 导航相关Instance，仅内部使用 */
    readonly lazyNavigationInstance: any;

    /**
     * 传送到指定坐标
     * @param x X 坐标
     * @param y Y 坐标
     */
    tp(x: number | string, y: number | string): Promise<void>;

    /**
     * 传送到指定坐标（强制传送）
     * @param x X 坐标
     * @param y Y 坐标
     * @param force 是否强制传送
     */
    tp(x: number | string, y: number | string, force: boolean): Promise<void>;

    /**
     * 传送到指定坐标（指定地图）
     * @param x X 坐标
     * @param y Y 坐标
     * @param mapName 地图名称
     * @param force 是否强制传送
     */
    tp(x: number, y: number, mapName: string, force: boolean): Promise<void>;

    /**
     * 移动大地图到指定坐标
     * @param x X 坐标
     * @param y Y 坐标
     * @param forceCountry 强制指定的国家（可选）
     */
    moveMapTo(x: number, y: number, forceCountry?: string | null): Promise<void>;

    /**
     * 移动独立地图到指定坐标
     * @param x X 坐标
     * @param y Y 坐标
     * @param mapName 地图名称
     * @param forceCountry 强制指定的国家（可选）
     */
    moveIndependentMapTo(x: number, y: number, mapName: string, forceCountry?: string | null): Promise<void>;

    /**
     * 获取当前大地图缩放等级
     * @returns 缩放等级（1.0-6.0）
     */
    getBigMapZoomLevel(): number;

    /**
     * 设置大地图缩放等级
     * @param zoomLevel 缩放等级（1.0-6.0，1.0 为最大地图，6.0 为最小地图）
     */
    setBigMapZoomLevel(zoomLevel: number): Promise<void>;

    /**
     * 传送到七天神像
     */
    tpToStatueOfTheSeven(): Promise<void>;

    /**
     * 从大地图获取当前位置
     * @returns 位置坐标
     */
    getPositionFromBigMap(): Point2f;

    /**
     * 从大地图获取当前位置（指定地图）
     * @param mapName 地图名称
     * @returns 位置坐标
     */
    getPositionFromBigMap(mapName: string): Point2f;

    /**
     * 从小地图获取当前位置
     * @param mapName 地图名称（可选，默认为提瓦特大陆）
     * @param cacheTimeMs 缓存时间（毫秒，默认 900ms）
     * @returns 位置坐标
     */
    getPositionFromMap(mapName?: string, cacheTimeMs?: number): Point2f;

    /**
     * 从小地图获取当前位置（局部匹配）
     * @param mapName 地图名称
     * @param x 参考世界坐标 X
     * @param y 参考世界坐标 Y
     * @returns 位置坐标
     */
    getPositionFromMap(mapName: string, x: number, y: number): Point2f;

    /**
     * 获取摄像机朝向
     * @returns 朝向角度
     */
    getCameraOrientation(): number;

    /**
     * 切换队伍
     * @param partyName 队伍名称
     * @returns 是否成功
     */
    switchParty(partyName: string): Promise<boolean>;

    /**
     * 清除当前调度器的队伍缓存
     */
    clearPartyCache(): void;

    /**
     * 自动点击空月祝福
     */
    blessingOfTheWelkinMoon(): Promise<void>;

    /**
     * 持续对话并选择目标选项
     * @param option 选项文本
     * @param skipTimes 跳过次数（默认 10）
     * @param isOrange 是否为橙色选项（默认 false）
     */
    chooseTalkOption(option: string, skipTimes?: number, isOrange?: boolean): Promise<void>;

    /**
     * 一键领取纪行奖励
     */
    claimBattlePassRewards(): Promise<void>;

    /**
     * 领取长效历练点奖励
     */
    claimEncounterPointsRewards(): Promise<void>;

    /**
     * 前往冒险家协会领取奖励
     * @param country 国家名称
     */
    goToAdventurersGuild(country: string): Promise<void>;

    /**
     * 前往合成台
     * @param country 国家名称
     */
    goToCraftingBench(country: string): Promise<void>;

    /**
     * 返回主界面
     */
    returnMainUi(): Promise<void>;

    /**
     * 自动钓鱼
     * @param fishingTimePolicy 钓鱼时间策略（默认 0）
     */
    autoFishing(fishingTimePolicy?: number): Promise<void>;

    /**
     * 重新登录原神
     */
    relogin(): Promise<void>;
};

/**
 * 日志输出
 */
declare const log: {
    /**
     * 输出调试日志
     * @param message 消息
     * @param args 参数
     */
    debug(message?: string, ...args: any[]): void;

    /**
     * 输出信息日志
     * @param message 消息
     * @param args 参数
     */
    info(message?: string, ...args: any[]): void;

    /**
     * 输出警告日志
     * @param message 消息
     * @param args 参数
     */
    warn(message?: string, ...args: any[]): void;

    /**
     * 输出错误日志
     * @param message 消息
     * @param args 参数
     */
    error(message?: string, ...args: any[]): void;
};

/**
 * 受限文件操作
 */
declare const file: {
    /**
     * 读取指定文件夹内所有文件和文件夹的路径（非递归）
     * @param folderPath 文件夹路径（相对于根目录）
     * @returns 文件和文件夹路径数组
     */
    readPathSync(folderPath: string): string[];

    /**
     * 判断路径是否为文件夹
     * @param path 文件或文件夹路径
     * @returns 是否为文件夹
     */
    isFolder(path: string): boolean;

    /**
     * 同步读取文本文件
     * @param path 文件路径
     * @returns 文件内容
     */
    readTextSync(path: string): string;

    /**
     * 异步读取文本文件
     * @param path 文件路径
     * @returns 文件内容
     */
    readText(path: string): Promise<string>;

    /**
     * 异步读取文本文件（带回调）
     * @param path 文件路径
     * @param callbackFunc 回调函数
     * @returns 文件内容
     */
    readText(path: string, callbackFunc: (error: string | null, data: string | null) => void): Promise<string>;

    /**
     * 同步读取图像文件为 Mat 对象
     * @param path 图像文件路径
     * @returns Mat 对象
     */
    readImageMatSync(path: string): Mat;

    /**
     * 同步写入文本到文件
     * @param path 文件路径
     * @param content 要写入的内容
     * @param append 是否追加到文件末尾（默认 false）
     * @returns 是否写入成功
     */
    writeTextSync(path: string, content: string, append?: boolean): boolean;

    /**
     * 异步写入文本到文件
     * @param path 文件路径
     * @param content 要写入的内容
     * @param append 是否追加到文件末尾（默认 false）
     * @returns 是否写入成功
     */
    writeText(path: string, content: string, append?: boolean): Promise<boolean>;

    /**
     * 异步写入文本到文件（带回调）
     * @param path 文件路径
     * @param content 要写入的内容
     * @param callbackFunc 回调函数
     * @param append 是否追加到文件末尾（默认 false）
     * @returns 是否写入成功
     */
    writeText(path: string, content: string, callbackFunc: (error: string | null, success: boolean | null) => void, append?: boolean): Promise<boolean>;

    /**
     * 同步写入图像到文件（默认 PNG 格式）
     * @param path 文件路径
     * @param mat Mat 对象
     * @returns 是否写入成功
     */
    writeImageSync(path: string, mat: Mat): boolean;
};

/**
 * HTTP 请求
 */
declare const http: {
    /**
     * 发送 HTTP 请求
     * @param method HTTP 方法（GET, POST, PUT, DELETE 等）
     * @param url 请求 URL
     * @param body 请求体（可选）
     * @param headersJson 请求头 JSON 字符串（可选）
     * @returns HTTP 响应
     */
    request(method: string, url: string, body?: string | null, headersJson?: string | null): Promise<HttpResponse>;
};

/**
 * HTTP 响应
 */
interface HttpResponse {
    /** HTTP 状态码 */
    status_code: number;
    /** 响应头 */
    headers: { [key: string]: string };
    /** 响应体 */
    body: string;
}

/**
 * 通知
 */
declare const notification: {
    /**
     * 发送通知
     * @param message 通知消息
     */
    send(message: string): void;

    /**
     * 发送错误通知
     * @param message 通知消息
     */
    error(message: string): void;
};

/**
 * 任务调度器
 */
declare const dispatcher: {
    /**
     * 添加实时任务（会清理之前的所有任务）
     * @param timer 实时任务触发器
     */
    addTimer(timer: RealtimeTimer): void;

    /**
     * 添加触发器（不会清理之前的任务）
     * @param timer 实时任务触发器
     */
    addTrigger(timer: RealtimeTimer): void;

    /**
     * 清理所有触发器
     */
    clearAllTriggers(): void;

    /**
     * 运行独立任务
     * @param soloTask 独立任务
     * @param customCt 自定义取消令牌（可选）
     * @returns 任务结果
     */
    runTask(soloTask: SoloTask, customCt?: CancellationToken | null): Promise<any>;

    /**
     * 运行独立任务（带取消令牌源）
     * @param soloTask 独立任务
     * @param customCts 自定义取消令牌源
     * @returns 任务结果
     */
    runTask(soloTask: SoloTask, customCts: CancellationTokenSource): Promise<any>;

    /**
     * 获取链接的取消令牌源
     * @returns 取消令牌源
     */
    getLinkedCancellationTokenSource(): CancellationTokenSource;

    /**
     * 获取链接的取消令牌
     * @returns 取消令牌
     */
    getLinkedCancellationToken(): CancellationToken;

    /**
     * 运行自动秘境任务
     * @param param 秘境任务参数
     * @param customCt 自定义取消令牌（可选）
     */
    runAutoDomainTask(param: AutoDomainParam, customCt?: CancellationToken | null): Promise<void>;

    /**
     * 运行自动战斗任务
     * @param param 战斗任务参数
     * @param customCt 自定义取消令牌（可选）
     */
    runAutoFightTask(param: AutoFightParam, customCt?: CancellationToken | null): Promise<void>;
};

// ==================== 类型定义 ====================

/**
 * 实时任务触发器
 */
declare class RealtimeTimer {
    /** 任务名称 */
    name?: string;
    /** 触发间隔（毫秒，默认 50ms） */
    interval?: number;
    /** 任务配置 */
    config?: any;

    constructor();
    constructor(name: string);
    constructor(name: string, config: any);
}

/**
 * 独立任务
 */
declare class SoloTask {
    /** 任务名称 */
    name: string;
    /** 任务配置 */
    config?: any;

    constructor(name: string);
    constructor(name: string, config: any);
}

/**
 * 取消令牌源
 */
declare class CancellationTokenSource {
    /** 取消令牌 */
    readonly token: CancellationToken;

    constructor();

    /**
     * 取消操作
     */
    cancel(): void;

    /**
     * 在指定延迟后取消
     * @param millisecondsDelay 延迟时间（毫秒）
     */
    cancelAfter(millisecondsDelay: number): void;

    /**
     * 释放资源
     */
    dispose(): void;
}

/**
 * 取消令牌
 */
declare class CancellationToken {
    /** 是否已请求取消 */
    readonly isCancellationRequested: boolean;

    /** 是否可以被取消 */
    readonly canBeCanceled: boolean;

    static readonly none: any;
}

/**
 * PostMessage 模拟器
 */
declare class PostMessage {
    constructor();
}

/**
 * 服务器时间
 */
declare class ServerTime {
    /**
     * 获取服务器时区偏移量（毫秒）
     * @returns 偏移量（毫秒）
     */
    static getServerTimeZoneOffset(): number;
}

/**
 * 自动秘境任务参数
 */
declare class AutoDomainParam {
    constructor(fightCount: number, fightStrategyPath: string);
    
    /** 副本轮数 */
    domainRoundNum: number;
    /** 战斗策略路径 */
    combatStrategyPath: string;
    /** 队伍名称 */
    partyName: string;
    /** 副本名称 */
    domainName: string;
    /** 周日选择的值 */
    sundaySelectedValue: string;
    /** 结束后是否自动分解圣遗物 */
    autoArtifactSalvage: boolean;
    /** 分解圣遗物的最大星级 (1-4) */
    maxArtifactStar: string;
    /** 是否指定树脂使用 */
    specifyResinUse: boolean;
    /** 树脂使用优先级列表 */
    resinPriorityList: string[];
    /** 使用原粹树脂次数 */
    originalResinUseCount: number;
    /** 使用浓缩树脂次数 */
    condensedResinUseCount: number;
    /** 使用须臾树脂次数 */
    transientResinUseCount: number;
    /** 使用脆弱树脂次数 */
    fragileResinUseCount: number;
}

/**
 * 自动战斗任务参数
 */
declare class AutoFightParam {
    constructor(fightStrategyPath: string);
    
    /** 战斗策略路径 */
    combatStrategyPath: string;
    /** 超时时间 */
    timeout: number;
    /** 是否启用战斗结束检测 */
    fightFinishDetectEnabled: boolean;
    /** 战斗后是否拾取掉落物 */
    pickDropsAfterFightEnabled: boolean;
    /** 战斗后拾取掉落物的秒数 */
    pickDropsAfterFightSeconds: number;
    /** 是否启用万叶拾取 */
    kazuhaPickupEnabled: boolean;
    /** 万叶队伍名称 */
    kazuhaPartyName: string;
    /** 按CD调度动作 */
    actionSchedulerByCd: string;
    /** 仅拾取精英掉落模式 */
    onlyPickEliteDropsMode: boolean;
    /** 战斗战利品阈值 */
    battleThresholdForLoot: number;
    /** 守护角色 */
    guardianAvatar: string;
    /** 守护战斗跳过 */
    guardianCombatSkip: boolean;

    /** 是否启用游泳检测 */
    static swimmingEnabled: boolean;
}

// ==================== 识图相关类型 ====================

/**
 * OpenCV Mat 图像矩阵
 */
declare class Mat {
    /** 图像宽度 */
    readonly width: number;
    /** 图像高度 */
    readonly height: number;

    /**
     * 释放资源
     */
    dispose(): void;
}

/**
 * 二维点坐标（浮点数）
 */
declare class Point2f {
    /** X 坐标 */
    x: number;
    /** Y 坐标 */
    y: number;

    constructor();
    constructor(x: number, y: number);
}

/**
 * 识别对象
 */
declare class RecognitionObject {
    /** 识别类型 */
    recognitionType: number;
    /** 感兴趣的区域 (ROI) */
    regionOfInterest: any; // OpenCV Rect
    /** 识别对象名称 */
    name: string | null;
    /** 模板匹配的对象(彩色) */
    templateImageMat: Mat | null;
    /** 模板匹配的对象(灰度) */
    templateImageGreyMat: Mat | null;
    /** 模板匹配阈值 (默认 0.8) */
    threshold: number;
    /** 是否使用 3 通道匹配 (默认 false) */
    use3Channels: boolean;
    /** 模板匹配算法 */
    templateMatchMode: number;
    /** 是否使用遮罩 */
    useMask: boolean;
    /** 遮罩颜色 */
    maskColor: any;
    /** 遮罩矩阵 */
    maskMat: Mat | null;
    /** 是否在窗口上绘制 */
    drawOnWindow: boolean;
    /** 绘制时使用的画笔 */
    drawOnWindowPen: any;
    /** 最大匹配数量 (-1 表示不限制) */
    maxMatchCount: number;
    /** 是否启用二值化匹配 */
    useBinaryMatch: boolean;
    /** 二值化阈值 (默认 128) */
    binaryThreshold: number;
    
    /**
     * 初始化模板
     */
    initTemplate(): this;

    static readonly ocrThis: RecognitionObject;
}

/**
 * 桌面区域
 */
declare class DesktopRegion extends Region {
    /**
     * 在桌面区域点击
     * @param x X 坐标
     * @param y Y 坐标
     * @param w 宽度
     * @param h 高度
     */
    desktopRegionClick(x: number, y: number, w: number, h: number): void;
    
    /**
     * 在桌面区域移动鼠标
     * @param x X 坐标
     * @param y Y 坐标
     * @param w 宽度
     * @param h 高度
     */
    desktopRegionMove(x: number, y: number, w: number, h: number): void;
    
    /**
     * 静态方法：在桌面指定位置点击
     * @param cx X 坐标
     * @param cy Y 坐标
     */
    static desktopRegionClick(cx: number, cy: number): void;
    
    /**
     * 静态方法：在桌面指定位置移动鼠标
     * @param cx X 坐标
     * @param cy Y 坐标
     */
    static desktopRegionMove(cx: number, cy: number): void;
    
    /**
     * 静态方法：相对移动鼠标
     * @param dx X 偏移量
     * @param dy Y 偏移量
     */
    static desktopRegionMoveBy(dx: number, dy: number): void;
}

/**
 * 游戏捕获区域
 */
declare class GameCaptureRegion extends ImageRegion {
    /**
     * 静态方法：在游戏区域点击
     * @param posFunc 位置计算函数
     */
    static gameRegionClick(posFunc: (size: any, scale: number) => [number, number]): void;
    
    /**
     * 静态方法：在游戏区域移动鼠标
     * @param posFunc 位置计算函数
     */
    static gameRegionMove(posFunc: (size: any, scale: number) => [number, number]): void;
    
    /**
     * 静态方法：在游戏区域相对移动鼠标
     * @param deltaFunc 偏移量计算函数
     */
    static gameRegionMoveBy(deltaFunc: (size: any, scale: number) => [number, number]): void;
    
    /**
     * 静态方法：点击 1080P 坐标
     * @param x X 坐标 (1080P)
     * @param y Y 坐标 (1080P)
     */
    static click1080P(x: number, y: number): void;
    
    /**
     * 静态方法：移动到 1080P 坐标
     * @param x X 坐标 (1080P)
     * @param y Y 坐标 (1080P)
     */
    static move1080P(x: number, y: number): void;
}

/**
 * 图像区域
 */
declare class ImageRegion extends Region {
    /** 源图像矩阵 */
    readonly srcMat: Mat;
    
    /** 缓存的灰度图像 */
    readonly cacheGreyMat: Mat;
    
    /** 缓存的 Image 对象 */
    readonly cacheImage: any;
    
    /**
     * 裁剪派生新区域
     * @param x X 坐标
     * @param y Y 坐标
     * @param w 宽度
     * @param h 高度
     */
    deriveCrop(x: number, y: number, w: number, h: number): ImageRegion;
    
    /**
     * 裁剪派生新区域（使用 Rect）
     * @param rect 矩形区域
     */
    deriveCrop(rect: any): ImageRegion;
    
    /**
     * 在区域内查找识别对象
     * @param ro 识别对象
     */
    find(ro: RecognitionObject): Region;
    
    /**
     * 在区域内查找所有匹配的识别对象
     * @param ro 识别对象
     */
    findMulti(ro: RecognitionObject): Region[];
}

/**
 * 区域
 */
declare class Region {
    /** X 坐标 */
    x: number;
    /** Y 坐标 */
    y: number;
    /** 宽度 */
    width: number;
    /** 高度 */
    height: number;
    /** 顶部坐标 */
    top: number;
    /** 底部坐标 */
    readonly bottom: number;
    /** 左边坐标 */
    left: number;
    /** 右边坐标 */
    readonly right: number;
    /** OCR 识别的文本 */
    text: string;
    
    /**
     * 点击区域中心
     */
    click(): this;
    
    /**
     * 双击区域中心
     */
    doubleClick(): this;
    
    /**
     * 点击区域内指定位置
     * @param x X 坐标
     * @param y Y 坐标
     */
    clickTo(x: number, y: number): void;
    
    /**
     * 点击区域内指定矩形中心
     * @param x X 坐标
     * @param y Y 坐标
     * @param w 宽度
     * @param h 高度
     */
    clickTo(x: number, y: number, w: number, h: number): void;
    
    /**
     * 移动到区域中心
     */
    move(): void;
    
    /**
     * 移动到区域内指定位置
     * @param x X 坐标
     * @param y Y 坐标
     */
    moveTo(x: number, y: number): void;
    
    /**
     * 移动到区域内指定矩形中心
     * @param x X 坐标
     * @param y Y 坐标
     * @param w 宽度
     * @param h 高度
     */
    moveTo(x: number, y: number, w: number, h: number): void;
    
    /**
     * 在窗口绘制自身
     * @param name 绘制名称
     * @param pen 画笔（可选）
     */
    drawSelf(name: string, pen?: any): void;
    
    /**
     * 在窗口绘制指定区域
     * @param x X 坐标
     * @param y Y 坐标
     * @param w 宽度
     * @param h 高度
     * @param name 绘制名称
     * @param pen 画笔（可选）
     */
    drawRect(x: number, y: number, w: number, h: number, name: string, pen?: any): void;
    
    /**
     * 转换为 ImageRegion
     */
    toImageRegion(): ImageRegion;
    
    /**
     * 转换为 Rect
     */
    toRect(): any;
    
    /**
     * 检查区域是否为空
     */
    isEmpty(): boolean;
    
    /**
     * 检查区域是否存在（语义化）
     */
    isExist(): boolean;
    
    /**
     * 释放资源
     */
    dispose(): void;
}

/**
 * 战斗场景
 */
declare class CombatScenes {
    /** 角色数量 */
    readonly avatarCount: number;
    /** 最近一次识别出的出战角色编号 (从 1 开始，-1 表示未识别) */
    lastActiveAvatarIndex: number;
    /** 当前多人游戏状态 */
    currentMultiGameStatus: any;
    /** 预期队伍角色数量 */
    expectedTeamAvatarNum: number;
    
    /**
     * 初始化队伍
     * @param imageRegion 图像区域
     * @returns 当前实例
     */
    initializeTeam(imageRegion: ImageRegion): this;

    /**
     * 获取角色列表（只读）
     * @returns 角色数组
     */
    getAvatars(): Avatar[];
    
    /**
     * 检查队伍是否已初始化
     */
    checkTeamInitialized(): boolean;
    
    /**
     * 通过名称选择角色
     * @param name 角色名称
     */
    selectAvatar(name: string): Avatar | null;
    
    /**
     * 通过编号选择角色
     * @param avatarIndex 角色编号（从 1 开始）
     */
    selectAvatar(avatarIndex: number): Avatar;
    
    /**
     * 获取当前出战角色名称
     * @param force 是否强制重新识别
     * @param region 图像区域（可选）
     * @param ct 取消令牌（可选）
     */
    currentAvatar(force?: boolean, region?: ImageRegion, ct?: any): string | null;
    
    /**
     * 任务前准备
     * @param ct 取消令牌
     */
    beforeTask(ct: any): void;
    
    /**
     * 任务后清理
     */
    afterTask(): void;
    
    /**
     * 释放资源
     */
    dispose(): void;
}

/**
 * 角色
 */
declare class Avatar {
    /** 角色名称 (中文) */
    readonly name: string;
    /** 队伍内序号 (从 1 开始) */
    readonly index: number;
    /** 配置文件中的角色信息 */
    readonly combatAvatar: any;
    /** 元素爆发是否就绪 */
    isBurstReady: boolean;
    /** 名字所在矩形位置 */
    nameRect: any;
    /** 编号所在矩形位置 */
    indexRect: any;
    /** 战斗场景引用 */
    readonly combatScenes: CombatScenes;
    
    /**
     * 切换到该角色
     */
    switch(): void;
    
    /**
     * 尝试切换到该角色
     * @param maxRetry 最大重试次数（可选）
     */
    trySwitch(maxRetry?: number): boolean;
    
    /**
     * 使用元素战技
     * @param holdPress 是否长按（可选）
     */
    useSkill(holdPress?: boolean): void;
    
    /**
     * 使用元素爆发
     */
    useBurst(): void;
    
    /**
     * 普通攻击
     * @param ms 持续时间（毫秒，可选）
     */
    attack(ms?: number): void;
    
    /**
     * 蓄力攻击
     * @param ms 持续时间（毫秒，可选）
     */
    charge(ms?: number): void;
    
    /**
     * 冲刺
     * @param ms 持续时间（毫秒，可选）
     */
    dash(ms?: number): void;
    
    /**
     * 跳跃
     */
    jump(): void;
    
    /**
     * 移动
     * @param direction 方向 ("w"/"a"/"s"/"d")
     * @param ms 持续时间（毫秒）
     */
    walk(direction: string, ms: number): void;
    
    /**
     * 等待
     * @param ms 等待时间（毫秒）
     */
    wait(ms: number): void;
    
    /**
     * 检查技能是否就绪
     */
    isSkillReady(): boolean;
    
    /**
     * 检查是否为出战状态
     * @param region 图像区域
     */
    isActive(region: ImageRegion): boolean;
    
    /**
     * 等待技能冷却
     * @param ct 取消令牌
     */
    waitSkillCd(ct: any): Promise<void>;
}

// /**
//  * OpenCvSharp 命名空间
//  */
// 为了避免与其他package中的类型冲突，这里不声明全局变量
// declare const OpenCvSharp: any;

/**
 * 脚本自定义设置（settings.json）
 */
declare const settings: any;


/**
 * Task 类型（C# Task）
 */
declare class Task<T = void> {
    /**
     * 等待任务完成
     * @returns 任务结果
     */
    then<TResult>(onfulfilled?: (value: T) => TResult | PromiseLike<TResult>): Promise<TResult>;
}
