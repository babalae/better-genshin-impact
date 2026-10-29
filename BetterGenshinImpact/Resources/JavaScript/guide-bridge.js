// BetterGI 悬浮小窗播放桥（见 Docs/design/guide-window.md 7.1、7.2 节）。
// 上报播放状态、字幕与网页事件，执行宿主命令。只在顶层文档运行，不接收网页传来的脚本。
(() => {
    'use strict';
    if (window.top !== window || window.__bgiGuide) {
        return;
    }

    const sites = window.__bgiGuideSites || [];
    const site = sites.find((candidate) => {
        try {
            return candidate.match(location);
        } catch (_) {
            return false;
        }
    }) || null;

    /** 发送消息给宿主 */
    const post = (message) => {
        try {
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage(message);
            }
        } catch (_) {
            // 页面卸载期间发送失败可以忽略
        }
    };

    /** 调用站点适配，异常时返回默认值 */
    const call = (name, fallback, ...args) => {
        if (!site || typeof site[name] !== 'function') {
            return fallback;
        }
        try {
            const result = site[name](...args);
            return result === undefined ? fallback : result;
        } catch (_) {
            return fallback;
        }
    };

    /** 有限数值，否则返回默认值 */
    const num = (value, fallback = 0) => (typeof value === 'number' && Number.isFinite(value) ? value : fallback);
    const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
    const ok = (extra) => Object.assign({ ok: true }, extra || {});

    // ===== 视频查找 =====

    /** 元素在视口内的可见面积 */
    const visibleArea = (element) => {
        const rect = element.getBoundingClientRect();
        if (rect.width < 40 || rect.height < 30) {
            return 0;
        }
        const style = getComputedStyle(element);
        if (style.visibility === 'hidden' || style.display === 'none' || Number(style.opacity) === 0) {
            return 0;
        }
        const width = Math.min(rect.right, window.innerWidth) - Math.max(rect.left, 0);
        const height = Math.min(rect.bottom, window.innerHeight) - Math.max(rect.top, 0);
        return width > 0 && height > 0 ? width * height : 0;
    };

    /** 当前要控制的视频：站点适配优先，否则选显示面积最大的可见视频 */
    const findVideo = () => {
        const fromSite = call('findVideo', null);
        if (fromSite) {
            return fromSite;
        }
        let best = null;
        let bestArea = 0;
        for (const video of document.querySelectorAll('video, bwp-video')) {
            const area = visibleArea(video);
            if (area > bestArea) {
                best = video;
                bestArea = area;
            }
        }
        return best;
    };

    let currentVideo = null;
    const mediaEvents = ['play', 'playing', 'pause', 'ratechange', 'seeked', 'volumechange', 'loadedmetadata', 'durationchange', 'emptied'];
    const onMediaEvent = () => scheduleReport();
    const onEnded = () => {
        post({ type: 'event', name: 'ended' });
        scheduleReport();
    };

    /** 切换监听的视频元素 */
    const bindVideo = (video) => {
        if (video === currentVideo) {
            return;
        }
        if (currentVideo) {
            for (const name of mediaEvents) {
                currentVideo.removeEventListener(name, onMediaEvent);
            }
            currentVideo.removeEventListener('ended', onEnded);
            currentVideo.removeEventListener('play', onPlay);
        }
        currentVideo = video || null;
        if (currentVideo) {
            for (const name of mediaEvents) {
                currentVideo.addEventListener(name, onMediaEvent);
            }
            currentVideo.addEventListener('ended', onEnded);
            currentVideo.addEventListener('play', onPlay);
        }
        scheduleReport();
    };

    /** 宿主要求时，切集或新视频开始播放后重新应用沉浸模式 */
    const onPlay = () => {
        if (immersiveWanted && !isImmersive()) {
            setTimeout(() => applyImmersive(), 300);
        }
    };

    // ===== 状态上报 =====

    /** 去掉常见的标题后缀 */
    const cleanTitle = (title) => (title || '').replace(/_哔哩哔哩_bilibili$/, '').trim();

    /** 构造状态消息 */
    const buildState = () => {
        const video = currentVideo;
        const information = call('info', {}) || {};
        const ep = call('episode', null);
        const duration = video ? num(video.duration) : 0;
        return {
            type: 'state',
            href: location.href,
            title: information.title || cleanTitle(document.title),
            site: site ? site.name : 'generic',
            videoKey: information.videoKey || (video ? location.origin + location.pathname + location.search : null),
            hasVideo: !!video,
            paused: video ? !!video.paused : true,
            currentTime: video ? num(video.currentTime) : 0,
            duration,
            playbackRate: video ? num(video.playbackRate, 1) : 1,
            volume: video ? num(video.volume, 1) : 1,
            videoWidth: video ? num(video.videoWidth) : 0,
            videoHeight: video ? num(video.videoHeight) : 0,
            immersive: isImmersive(),
            episodeIndex: ep ? num(ep.index) : 0,
            episodeCount: ep ? num(ep.count) : 0,
            hasPrev: ep ? !!ep.hasPrev : false,
            hasNext: ep ? !!ep.hasNext : false,
            danmaku: call('getDanmaku', null),
        };
    };

    let reportTimer = 0;
    let lastReportAt = 0;

    /** 合并上报：每秒最多 4 条 */
    const scheduleReport = () => {
        if (reportTimer) {
            return;
        }
        const wait = Math.max(0, 250 - (Date.now() - lastReportAt));
        reportTimer = setTimeout(() => {
            reportTimer = 0;
            lastReportAt = Date.now();
            post(buildState());
        }, wait);
    };

    // ===== 网址与视频切换 =====

    let lastHref = location.href;
    let lastKey = null;
    let autoSubtitle = false;

    /** 网址或视频标识变化：页内跳转、切集 */
    const checkNavigation = () => {
        if (location.href === lastHref) {
            return;
        }
        lastHref = location.href;
        const key = (call('info', {}) || {}).videoKey || null;
        if (key !== lastKey) {
            lastKey = key;
            lastSubtitle = null;
            post({ type: 'event', name: 'videoChanged' });
            if (autoSubtitle) {
                setTimeout(() => call('enableChineseSubtitle', null), 1500);
            }
            if (immersiveWanted) {
                // 切换视频时播放器可能自己退出网页全屏，不能当作用户手动退出
                immersiveConfirmed = false;
                setTimeout(() => applyImmersive(), 800);
            }
        }
        scheduleReport();
    };

    for (const method of ['pushState', 'replaceState']) {
        const original = history[method];
        history[method] = function (...args) {
            const result = original.apply(this, args);
            setTimeout(checkNavigation, 0);
            return result;
        };
    }
    window.addEventListener('popstate', () => setTimeout(checkNavigation, 0));

    // ===== 字幕 =====

    let lastSubtitle = null;

    /** 当前字幕：站点字幕层优先，否则读取显示中的 HTML5 字幕轨道 */
    const readSubtitle = () => {
        const fromSite = call('readSubtitle', null);
        if (typeof fromSite === 'string') {
            return fromSite;
        }
        const video = currentVideo;
        if (!video || !video.textTracks) {
            return '';
        }
        for (const track of Array.from(video.textTracks)) {
            if (track.mode === 'showing' && (track.kind === 'subtitles' || track.kind === 'captions') && track.activeCues) {
                return Array.from(track.activeCues)
                    .map((cue) => (cue.text || '').replace(/<[^>]+>/g, ''))
                    .join('\n')
                    .trim();
            }
        }
        return '';
    };

    setInterval(() => {
        const text = readSubtitle();
        if (text !== lastSubtitle) {
            lastSubtitle = text;
            post({ type: 'subtitle', text });
        }
    }, 250);

    const SUBTITLE_STYLE_ID = '__bgi_guide_subtitle_style';

    /** 隐藏网页内字幕：只改可见性，文本仍可读取 */
    const setPageSubtitleHidden = (hidden) => {
        const existing = document.getElementById(SUBTITLE_STYLE_ID);
        if (existing) {
            existing.remove();
        }
        if (!hidden) {
            return;
        }
        const style = document.createElement('style');
        style.id = SUBTITLE_STYLE_ID;
        style.textContent = `${site && site.subtitleHideCss ? site.subtitleHideCss : ''}
video::cue { color: transparent !important; background: transparent !important; text-shadow: none !important; }`;
        (document.head || document.documentElement).appendChild(style);
    };

    // ===== 沉浸模式 =====

    const FOCUS_STYLE_ID = '__bgi_guide_focus_style';
    const FOCUS_ATTRIBUTE = 'data-bgi-guide-focus';
    let immersiveWanted = false;
    let immersiveConfirmed = false;

    /** 当前是否处于沉浸状态 */
    const isImmersive = () => {
        const fromSite = call('isImmersive', null);
        if (typeof fromSite === 'boolean') {
            return fromSite;
        }
        return !!document.getElementById(FOCUS_STYLE_ID);
    };

    /** 通用聚焦：把视频固定铺满视口 */
    const genericFocus = (on) => {
        const existing = document.getElementById(FOCUS_STYLE_ID);
        if (existing) {
            existing.remove();
        }
        for (const element of document.querySelectorAll(`[${FOCUS_ATTRIBUTE}]`)) {
            element.removeAttribute(FOCUS_ATTRIBUTE);
        }
        if (!on) {
            return true;
        }
        const video = currentVideo || findVideo();
        if (!video) {
            return false;
        }
        video.setAttribute(FOCUS_ATTRIBUTE, '1');
        const style = document.createElement('style');
        style.id = FOCUS_STYLE_ID;
        style.textContent = `html, body { overflow: hidden !important; }
[${FOCUS_ATTRIBUTE}] { position: fixed !important; left: 0 !important; top: 0 !important; width: 100vw !important; height: 100vh !important;
max-width: none !important; max-height: none !important; margin: 0 !important; transform: none !important;
z-index: 2147483647 !important; background: #000 !important; object-fit: contain !important; }`;
        (document.head || document.documentElement).appendChild(style);
        return true;
    };

    /** 按 immersiveWanted 应用沉浸状态；站点播放器尚未加载时等待下一次重试 */
    const applyImmersive = () => {
        if (site && typeof site.setImmersive === 'function') {
            return call('setImmersive', false, immersiveWanted);
        }
        return genericFocus(immersiveWanted);
    };

    // ===== 心跳：重新查找视频、补报状态、重试沉浸、发现用户手动退出网页全屏 =====

    setInterval(() => {
        const video = findVideo();
        if (video !== currentVideo) {
            bindVideo(video);
        }
        if (currentVideo && !currentVideo.paused) {
            scheduleReport();
        }
        checkNavigation();

        if (immersiveWanted && site && typeof site.setImmersive === 'function') {
            const now = isImmersive();
            if (now === true) {
                immersiveConfirmed = true;
            } else if (now === false && immersiveConfirmed) {
                // 进入过网页全屏后又退出了：用户在网页里按了 Esc 或点了按钮，尊重用户的选择
                immersiveWanted = false;
                immersiveConfirmed = false;
                post({ type: 'event', name: 'immersiveExited' });
            } else if (now === false) {
                applyImmersive();
            }
        }
    }, 1000);

    // ===== 输入框焦点：宿主据此进入交互模式，允许键盘输入 =====

    const isEditable = (element) => !!element && (element.isContentEditable
        || element.tagName === 'TEXTAREA'
        || element.tagName === 'INPUT' && !['button', 'checkbox', 'radio', 'range', 'submit', 'reset', 'file', 'image', 'color']
            .includes((element.type || '').toLowerCase()));

    document.addEventListener('focusin', (event) => {
        if (isEditable(event.target)) {
            post({ type: 'event', name: 'editFocus' });
        }
    }, true);
    document.addEventListener('focusout', (event) => {
        if (isEditable(event.target)) {
            post({ type: 'event', name: 'editBlur' });
        }
    }, true);

    // ===== 命令 =====

    /** 播放；被网站或自动播放策略拒绝时通知宿主 */
    const playVideo = (video) => {
        try {
            const promise = video.play();
            if (promise && typeof promise.catch === 'function') {
                promise.catch(() => post({ type: 'event', name: 'playRejected' }));
            }
        } catch (_) {
            post({ type: 'event', name: 'playRejected' });
        }
        return ok();
    };

    /** 切集：站点适配，否则使用页面的 rel=prev/next 链接 */
    const switchEpisode = (offset) => {
        const fromSite = call('switchEpisode', null, offset);
        if (fromSite) {
            return fromSite;
        }
        const link = document.querySelector(offset > 0 ? 'a[rel="next"]' : 'a[rel="prev"]');
        if (link && link.href) {
            location.href = link.href;
            return ok();
        }
        return { ok: false, reason: offset > 0 ? 'noNext' : 'noPrev' };
    };

    const videoCommands = ['play', 'pause', 'toggle', 'seekBy', 'seekTo', 'setRate', 'setVolume'];

    /** 执行宿主命令，返回 { ok, reason } */
    const exec = (command) => {
        const cmd = command && command.cmd;
        const value = Number(command && command.value) || 0;
        const video = currentVideo || findVideo();
        if (video && video !== currentVideo) {
            bindVideo(video);
        }
        if (videoCommands.includes(cmd) && !video) {
            return { ok: false, reason: 'noVideo' };
        }

        switch (cmd) {
            case 'play':
                return playVideo(video);
            case 'pause':
                video.pause();
                return ok();
            case 'toggle':
                if (video.paused) {
                    return playVideo(video);
                }
                video.pause();
                return ok();
            case 'seekBy': {
                const duration = num(video.duration);
                let target = num(video.currentTime) + value;
                if (duration > 0) {
                    target = Math.min(target, Math.max(0, duration - 0.5));
                }
                video.currentTime = Math.max(0, target);
                return ok({ currentTime: video.currentTime });
            }
            case 'seekTo': {
                const duration = num(video.duration);
                video.currentTime = duration > 0 ? clamp(value, 0, Math.max(0, duration - 0.5)) : Math.max(0, value);
                return ok({ currentTime: video.currentTime });
            }
            case 'setRate':
                video.playbackRate = clamp(value, 0.1, 16);
                return ok({ playbackRate: video.playbackRate });
            case 'setVolume':
                video.volume = clamp(value, 0, 1);
                if (video.muted && value > 0) {
                    video.muted = false;
                }
                return ok({ volume: video.volume });
            case 'prevEpisode':
                return switchEpisode(-1);
            case 'nextEpisode':
                return switchEpisode(1);
            case 'setImmersive':
                immersiveWanted = value > 0;
                immersiveConfirmed = false;
                applyImmersive();
                scheduleReport();
                return ok();
            case 'setAutoSubtitle':
                autoSubtitle = value > 0;
                if (autoSubtitle) {
                    call('enableChineseSubtitle', null);
                }
                return ok();
            case 'setPageSubtitleHidden':
                setPageSubtitleHidden(value > 0);
                return ok();
            case 'setDanmaku': {
                const result = call('setDanmaku', null, value > 0);
                scheduleReport();
                return result === null ? { ok: false, reason: 'unsupported' } : ok({ danmaku: result });
            }
            case 'requestState':
                scheduleReport();
                return ok();
            default:
                return { ok: false, reason: 'unknownCommand' };
        }
    };

    Object.defineProperty(window, '__bgiGuide', {
        value: Object.freeze({
            exec: (command) => {
                try {
                    return exec(command);
                } catch (error) {
                    return { ok: false, reason: String((error && error.message) || error) };
                }
            },
        }),
        configurable: false,
        enumerable: false,
        writable: false,
    });

    // ===== 初始化 =====

    const init = () => {
        lastKey = (call('info', {}) || {}).videoKey || null;
        bindVideo(findVideo());
        post({ type: 'event', name: 'ready' });
        scheduleReport();
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
