// BetterGI 悬浮小窗：B 站站点适配（见 Docs/design/guide-window.md 7.3 节）。
// 只登记适配对象，由 guide-bridge.js 调用。选择器与页面结构以 2026-10 的 B 站页面为准，改版时只需改这个文件。
// 任何一项适配失败都返回 null / false，由播放桥回退到通用控制，不影响页面本身。
(() => {
    'use strict';
    if (window.top !== window) {
        return;
    }

    const sites = window.__bgiGuideSites = window.__bgiGuideSites || [];

    /** 查询单个元素 */
    const q = (selector, root) => (root || document).querySelector(selector);
    /** 查询元素列表 */
    const qa = (selector, root) => Array.from((root || document).querySelectorAll(selector));

    /** 页面初始数据；页内跳转后可能仍是上一个视频的数据，使用前要与网址核对 */
    const initialState = () => {
        try {
            return window.__INITIAL_STATE__ || null;
        } catch (_) {
            return null;
        }
    };

    /** 从网址解析 BV、av、番剧编号与分 P */
    const parseUrl = () => {
        const url = new URL(location.href);
        const bv = /\/video\/(BV[0-9A-Za-z]{10})/.exec(url.pathname);
        const av = /\/video\/av(\d+)/i.exec(url.pathname);
        const bangumi = /\/bangumi\/play\/(ep|ss)(\d+)/i.exec(url.pathname);
        const p = parseInt(url.searchParams.get('p') || '1', 10);
        return {
            bv: bv ? bv[1] : null,
            av: av ? av[1] : null,
            bangumiType: bangumi ? bangumi[1].toLowerCase() : null,
            bangumiId: bangumi ? bangumi[2] : null,
            p: Number.isFinite(p) && p > 0 ? p : 1,
        };
    };

    /** 当前视频的数据（与网址中的 BV 号一致时） */
    const videoData = (u) => {
        const s = initialState();
        const data = s && s.videoData;
        if (!data || !u.bv || data.bvid !== u.bv) {
            return null;
        }
        return data;
    };

    /** 去掉标题中的站点后缀 */
    const cleanTitle = (title) => (title || '')
        .replace(/_哔哩哔哩_bilibili$/, '')
        .replace(/_bilibili$/i, '')
        .replace(/-哔哩哔哩.*$/, '')
        .trim();

    /** 视频标识与标题 */
    const info = () => {
        const u = parseUrl();
        let key = null;
        if (u.bv) {
            key = `bilibili:${u.bv}:p${u.p}`;
        } else if (u.av) {
            key = `bilibili:av${u.av}:p${u.p}`;
        } else if (u.bangumiType) {
            key = `bilibili:${u.bangumiType}${u.bangumiId}`;
        }

        const data = videoData(u);
        let title = data && data.title ? data.title : cleanTitle(document.title);
        if (data && Array.isArray(data.pages) && data.pages.length > 1) {
            const page = data.pages[u.p - 1];
            if (page && page.part) {
                title = `${title} · ${page.part}`;
            }
        }
        return { videoKey: key, title };
    };

    /** 合集列表中的当前项：优先按 data-key 与 BV 号匹配 */
    const podItems = () => qa('.video-pod__list .video-pod__item');
    const activePodIndex = (items, bv) => {
        let index = items.findIndex((item) => bv && item.getAttribute('data-key') === bv);
        if (index < 0) {
            index = items.findIndex((item) => item.classList.contains('active'));
        }
        return index;
    };

    /** 分集信息：分 P、合集、番剧 */
    const episode = () => {
        const u = parseUrl();
        const data = videoData(u);
        if (data && Array.isArray(data.pages) && data.pages.length > 1) {
            return { kind: 'pages', index: u.p, count: data.pages.length, hasPrev: u.p > 1, hasNext: u.p < data.pages.length };
        }

        const items = podItems();
        if (items.length > 1) {
            const index = activePodIndex(items, u.bv);
            if (index >= 0) {
                return { kind: 'pod', index: index + 1, count: items.length, hasPrev: index > 0, hasNext: index < items.length - 1 };
            }
        }

        const next = q('.bpx-player-ctrl-next');
        if (next) {
            return { kind: 'player', index: 0, count: 0, hasPrev: false, hasNext: true };
        }
        return null;
    };

    /** 切换到相邻的一集，首尾不循环 */
    const switchEpisode = (offset) => {
        const u = parseUrl();
        const ep = episode();
        const missing = { ok: false, reason: offset > 0 ? 'noNext' : 'noPrev' };
        if (!ep) {
            return missing;
        }

        if (ep.kind === 'pages') {
            const target = ep.index + offset;
            if (target < 1 || target > ep.count) {
                return missing;
            }
            // 设置相邻的 p 参数正常导航，保留其他参数，移除旧的时间定位
            const url = new URL(location.href);
            url.searchParams.set('p', String(target));
            url.searchParams.delete('t');
            location.href = url.toString();
            return { ok: true };
        }

        if (ep.kind === 'pod') {
            const items = podItems();
            const index = activePodIndex(items, u.bv);
            const target = items[index + offset];
            if (!target) {
                return missing;
            }
            const key = target.getAttribute('data-key');
            if (key && /^BV[0-9A-Za-z]{10}$/.test(key)) {
                location.href = new URL(`/video/${key}/`, location.origin).toString();
                return { ok: true };
            }
            const clickable = q('.title, .simple-base-item, a', target) || target;
            clickable.click();
            return { ok: true };
        }

        if (offset > 0) {
            const next = q('.bpx-player-ctrl-next');
            if (next) {
                next.click();
                return { ok: true };
            }
        }
        return missing;
    };

    /** 播放器容器 */
    const container = () => q('.bpx-player-container');

    /** 是否处于网页全屏或全屏；播放器尚未加载时返回 null */
    const isImmersive = () => {
        const c = container();
        if (!c) {
            return null;
        }
        const mode = c.getAttribute('data-screen');
        return mode === 'web' || mode === 'full';
    };

    /** 进入或退出网页全屏；播放器尚未加载时返回 false，由播放桥稍后重试 */
    const setImmersive = (on) => {
        const button = q('.bpx-player-ctrl-web');
        const current = isImmersive();
        if (!button || current === null) {
            return false;
        }
        if (current !== on) {
            button.click();
        }
        return true;
    };

    /** 播放器字幕层文本；没有字幕层时返回 null，由播放桥读取 HTML5 字幕轨道 */
    const readSubtitle = () => {
        let nodes = qa('.bili-subtitle-x-subtitle-panel-text');
        if (!nodes.length) {
            nodes = qa('.bpx-player-subtitle-panel-text');
        }
        if (!nodes.length) {
            return container() ? '' : null;
        }
        return nodes.map((node) => (node.textContent || '').trim()).filter((text) => text).join('\n');
    };

    /** 开启字幕浮窗时隐藏播放器内的字幕：只改可见性，文本仍可读取 */
    const subtitleHideCss = '.bili-subtitle-x-subtitle-panel, .bpx-player-subtitle-panel-wrap, .bpx-player-subtitle-wrap { opacity: 0 !important; }';

    /** 已处理过自动开启字幕的视频，本集内用户手动关闭或改语言后不再干预 */
    let subtitleHandledKey = null;
    let subtitleTimer = 0;

    /** 自动开启中文字幕：普通中文优先，其次 AI 中文；最多等待约 30 秒 */
    const enableChineseSubtitle = () => {
        const key = info().videoKey;
        if (!key || subtitleHandledKey === key) {
            return;
        }
        subtitleHandledKey = key;
        if (subtitleTimer) {
            clearInterval(subtitleTimer);
        }

        let tries = 0;
        subtitleTimer = setInterval(() => {
            tries++;
            if (tries > 30 || info().videoKey !== key) {
                clearInterval(subtitleTimer);
                subtitleTimer = 0;
                return;
            }

            const items = qa('.bpx-player-ctrl-subtitle-language-item');
            if (!items.length) {
                return;
            }

            const lan = (item) => (item.getAttribute('data-lan') || '').toLowerCase();
            const pick = items.find((item) => /^zh/.test(lan(item)))
                || items.find((item) => /^ai-zh/.test(lan(item)))
                || items.find((item) => /中文/.test(item.textContent || ''));
            clearInterval(subtitleTimer);
            subtitleTimer = 0;
            if (pick && !pick.classList.contains('bpx-state-active')) {
                pick.click();
            }
        }, 1000);
    };

    /** 弹幕开关的复选框 */
    const danmakuInput = () => q('.bui-danmaku-switch-input') || q('.bpx-player-dm-switch input');

    /** 弹幕是否开启；没有开关时返回 null */
    const getDanmaku = () => {
        const input = danmakuInput();
        return input ? !!input.checked : null;
    };

    /** 开关弹幕 */
    const setDanmaku = (on) => {
        const input = danmakuInput();
        if (!input) {
            return null;
        }
        if (!!input.checked !== on) {
            input.click();
        }
        return on;
    };

    /** 播放器中的视频元素（部分视频使用 bwp-video 自定义元素） */
    const findVideo = () => q('.bpx-player-video-wrap video, .bpx-player-video-wrap bwp-video');

    sites.push({
        name: 'bilibili',
        match: (loc) => /(^|\.)bilibili\.com$/i.test(loc.hostname),
        info,
        episode,
        switchEpisode,
        isImmersive,
        setImmersive,
        readSubtitle,
        subtitleHideCss,
        enableChineseSubtitle,
        getDanmaku,
        setDanmaku,
        findVideo,
    });
})();
