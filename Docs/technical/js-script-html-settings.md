# JS 脚本 HTML 设置页开发指南

面向 BetterGI JS 脚本作者。说明如何为脚本编写自己的 HTML 设置界面，以及 BetterGI 提供的接口和页面运行环境。

- 适用版本：包含 HTML 设置页功能的 BetterGI（首个正式版本以发布说明为准）。旧版本会忽略 `settings_html`，见[第 7 节](#7-与-settings_ui-共存兼容旧版本)。
- 实现与设计取舍见[设计文档](../design/js-script-html-settings.md)，本文只写作者需要知道的内容。

## 1. 概述

以前，脚本只能用 `settings_ui`（通常是 `settings.json`）声明一组固定类型的控件，由 BetterGI 生成表单，下文称为“基础表单”。HTML 设置页让你用 HTML、CSS 和 JS 自己实现整个设置界面：分组、标签页、按条件显示、输入校验、列表编辑、说明图片都可以做。

BetterGI 只负责三件事：

1. 在一个窗口里显示你的页面；
2. 通过 `/__bgi/settings` 把已保存的设置交给页面，并接收页面提交的新设置；
3. 把设置保存到原来的位置。脚本运行时仍然通过全局变量 `settings` 读取，`main.js` 不用改。

你只需要会用 `fetch`，不需要引入 SDK、配置令牌或处理跨域。

## 2. 快速开始

### 2.1 目录结构

```text
MyScript/
├─ manifest.json
├─ main.js
├─ settings.json          可选：基础表单，旧版本 BetterGI 使用
└─ settings/
   └─ index.html          HTML 设置页
```

### 2.2 manifest.json

```json
{
  "manifest_version": 1,
  "name": "自动采集示例",
  "version": "1.0.0",
  "main": "main.js",
  "settings_html": "settings/index.html"
}
```

### 2.3 settings/index.html

下面是一个完整可用的设置页，可以直接复制后修改：

```html
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <title>自动采集示例设置</title>
  <style>
    :root { color-scheme: light dark; font-family: "Microsoft YaHei UI", sans-serif; }
    body { margin: 24px; }
    label { display: block; margin-bottom: 12px; }
    #message { margin-left: 8px; }
  </style>
</head>
<body>
  <label>采集轮数 <input id="rounds" type="number" min="1" max="99"></label>
  <label><input id="skipBoss" type="checkbox"> 跳过首领</label>
  <button id="save">保存</button>
  <button id="saveAndClose">保存并关闭</button>
  <span id="message"></span>

  <script>
    const $ = id => document.getElementById(id);
    let settings = {};

    // 读取已保存的设置；从未保存时是 {}，默认值由页面自己决定
    async function load() {
      const res = await fetch("/__bgi/settings");
      settings = await res.json();
      $("rounds").value = settings.rounds ?? 3;
      $("skipBoss").checked = settings.skipBoss ?? false;
    }

    // 提交完整的设置对象；以读取到的对象为基础修改，不会删掉页面没有展示的键
    async function save() {
      const rounds = Number($("rounds").value);
      if (!Number.isInteger(rounds) || rounds < 1 || rounds > 99) {
        $("message").textContent = "采集轮数必须是 1 到 99 的整数";
        return false;
      }
      const next = { ...settings, rounds, skipBoss: $("skipBoss").checked };
      const res = await fetch("/__bgi/settings", { method: "POST", body: JSON.stringify(next) });
      if (!res.ok) {
        $("message").textContent = (await res.json()).error;
        return false;
      }
      settings = next;
      $("message").textContent = "已保存";
      return true;
    }

    $("save").onclick = save;
    $("saveAndClose").onclick = async () => {
      if (await save()) await fetch("/__bgi/close", { method: "POST" });
    };
    load();
  </script>
</body>
</html>
```

### 2.4 main.js

读取方式与基础表单相同：

```js
const rounds = Number(settings.rounds ?? 3);
const skipBoss = settings.skipBoss === true;
```

把脚本放进 `User/JsScript/MyScript`，加入调度器的配置组，右键选择“修改JS脚本自定义配置”，就会打开这个页面。

## 3. manifest 字段 `settings_html`

`settings_html` 是项目根目录下的相对路径，指向入口 HTML 文件。入口所在的目录叫“设置页目录”，它就是页面网站的根目录：

| 写法 | 设置页目录 | 页面里的 `/style.css` 对应 |
| --- | --- | --- |
| `"settings/index.html"`（推荐） | `MyScript/settings/` | `MyScript/settings/style.css` |
| `"settings.html"` | `MyScript/`（整个项目） | `MyScript/style.css` |

路径规则（不满足时设置页打不开，并提示原因）：

- 只能是相对路径，分隔符可以是 `/` 或 `\`；
- 不能包含 `..`、`.` 段，不能以 `.` 或空格结尾，不能使用 `CON`、`NUL`、`COM1` 等 Windows 保留名；
- 扩展名必须是 `.html` 或 `.htm`；
- 路径上不能有符号链接或目录联接；
- 入口文件必须存在。

这些规则只在打开设置和从仓库添加脚本时检查，写错不会影响脚本运行。

页面地址形如 `https://ss-xxxxxxxxxxxxxxxx.bettergi.local/index.html`。主机名由项目位置决定，不要在代码里写死，统一使用以 `/` 开头的绝对路径或相对路径。

## 4. 接口参考

所有接口都在保留路径 `/__bgi/` 下，由 BetterGI 在窗口内部处理，不经过网络。设置页目录中名为 `__bgi` 的子目录不会提供给页面。

| 方法与路径 | 用途 |
| --- | --- |
| `GET /__bgi/settings` | 读取已保存的设置 |
| `POST /__bgi/settings` | 保存设置 |
| `GET /__bgi/context` | 读取上下文（可选） |
| `POST /__bgi/close` | 关闭设置窗口 |

其他 `/__bgi/` 地址返回 404；上表路径使用其他方法时返回 405。

### 4.1 读取设置

```http
GET /__bgi/settings
```

返回已保存的设置对象本身（不包一层），从未保存时返回 `{}`：

```json
{ "rounds": 5, "skipBoss": true }
```

每次请求都读取最新值，保存后刷新页面能看到新值。BetterGI 不会替你填入 `settings_ui` 里的默认值。

### 4.2 保存设置

```http
POST /__bgi/settings

{ "rounds": 5, "skipBoss": true }
```

- 请求体就是完整的设置对象，会**整体替换**已保存的值，请求里没有的键会被删除。
- 按 UTF-8 JSON 解析，不检查 `Content-Type`，`fetch` 不用写请求头。
- 请求体必须是一个 JSON 对象，不超过 1 MB，嵌套不超过 32 层，对象后面不能有多余内容。
- 字符串原样保存，ISO 时间格式的字符串不会被转成日期。
- 每次保存成功都立即生效，可以保存多次，也可以在每次修改后自动保存。
- 不按 `settings_ui` 校验，键和值的类型由你负责。

响应都是 JSON：

| 状态码 | 情况 | 响应体 |
| --- | --- | --- |
| 200 | 已保存 | `{ "ok": true }` |
| 400 | 不是合法 JSON、根不是对象、嵌套过深、有多余内容 | `{ "ok": false, "error": "原因" }` |
| 409 | BetterGI 拒绝写入，例如找不到脚本所在的配置组、任务计划校验失败 | 同上 |
| 413 | 请求体超过 1 MB | 同上 |
| 500 | 写入时发生其他错误，例如配置文件被占用 | 同上，详细原因在 BetterGI 日志中 |

`error` 是给用户看的中文说明，可以直接显示在页面上。

### 4.3 读取上下文

```http
GET /__bgi/context
```

```json
{
  "apiVersion": 1,
  "mode": "edit",
  "host": "scheduler",
  "script": { "name": "自动采集示例", "version": "1.0.0" },
  "bgi": { "version": "0.67.0" },
  "theme": "dark",
  "language": "zh-Hans"
}
```

| 字段 | 含义 |
| --- | --- |
| `apiVersion` | 接口版本，当前为 `1`。有不兼容变化时会提高 |
| `mode` | `create`：在 Pulonia 中添加任务、任务还没加入计划时；其他情况为 `edit` |
| `host` | 打开设置的入口：`scheduler`（调度器）或 `pulonia`（任务计划、新一条龙） |
| `script` | manifest 中的 `name` 和 `version` |
| `bgi.version` | BetterGI 版本 |
| `theme` | 打开窗口时 BetterGI 的主题，`light` 或 `dark` |
| `language` | BetterGI 界面语言，例如 `zh-Hans`、`zh-Hant`、`en`、`ja` |

不需要这些信息时可以不调用。

### 4.4 关闭窗口

```http
POST /__bgi/close
```

返回 `{ "ok": true }`，窗口随后关闭。

`window.close()` 也能关闭窗口，但浏览器只允许没有跳转历史的页面关闭自己：页面一旦跳转过（例如从 `index.html` 点链接到 `advanced.html`），`window.close()` 会被忽略。**建议统一使用 `POST /__bgi/close`。**

### 4.5 可复用的封装

可以把下面的代码放进设置页目录，例如 `settings/bgi.js`，用 `<script src="bgi.js"></script>` 引入：

```js
// BetterGI HTML 设置页接口的简单封装
window.bgiSettings = {
  // 读取已保存的设置
  async load() {
    const res = await fetch("/__bgi/settings");
    if (!res.ok) throw new Error(`读取设置失败（HTTP ${res.status}）`);
    return res.json();
  },
  // 保存完整的设置对象；失败时抛出带中文原因的错误
  async save(settings) {
    const res = await fetch("/__bgi/settings", { method: "POST", body: JSON.stringify(settings) });
    if (res.ok) return;
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error ?? `保存失败（HTTP ${res.status}）`);
  },
  // 读取上下文
  async context() {
    const res = await fetch("/__bgi/context");
    return res.ok ? res.json() : null;
  },
  // 关闭设置窗口
  close() {
    return fetch("/__bgi/close", { method: "POST" });
  },
};
```

## 5. 页面运行环境

设置页运行在 BetterGI 窗口内的 WebView2（Chromium 内核）中，按不受信任的第三方页面处理。

### 5.1 窗口

- 模态窗口，默认 760×640，最小 480×360，用户可以调整大小，页面需要适应不同宽度。
- 窗口里只有你的页面，BetterGI 不加“确定”“取消”按钮。用户可以随时点标题栏的关闭按钮。
- 每次保存成功后，标题栏显示“已保存 HH:mm:ss”。
- 关闭窗口时 BetterGI 不知道页面有没有未保存的修改，不会提示。请在页面上显示保存状态，或者在修改后自动保存。
- 入口加载失败（例如文件不存在）时，窗口顶部显示错误提示。

### 5.2 可以用的

| 能力 | 说明 |
| --- | --- |
| 设置页目录中的文件 | 用相对路径或以 `/` 开头的路径引用 |
| 内联 `<script>`、`<style>`、`eval` | 允许，打包后的 Vue、React 等框架也能用 |
| `data:`、`blob:` | 可用于图片、字体、媒体，`blob:` 可用于 Worker |
| `localStorage`、`IndexedDB` | 每个脚本单独一份，同一脚本下次打开仍在。只适合保存界面状态（例如上次打开的标签页），设置值必须通过接口保存，脚本读不到网页存储 |
| `alert`、`confirm`、`prompt` | 可用 |
| 深色、浅色主题 | 用 CSS `@media (prefers-color-scheme: dark)`，BetterGI 按当前主题设置；也可以读上下文中的 `theme` |
| 带 `target="_blank"` 的 http(s) 链接 | 在系统浏览器中打开，适合放教程链接 |
| F5、F12 | 重新加载页面、打开开发者工具 |

### 5.3 不能用的

| 限制 | 表现 |
| --- | --- |
| 访问外网（CDN、在线字体、在线 API） | 请求失败（`Failed to fetch`），资源加载不出来。所有文件必须放在设置页目录内 |
| 访问本机服务（`127.0.0.1`、`localhost`，包括 BetterGI 的外部访问服务） | 同上 |
| WebSocket | 无法连接 |
| `<iframe>`、`<object>`、`<embed>` | 不加载 |
| 跳转到其他网站（不带 `target="_blank"` 的外链、`location.href = "https://..."`） | 被取消，页面保持原样 |
| 下载文件 | 被取消 |
| `<form method="post">` 提交到 `/__bgi/settings` | 表单编码不是 JSON，返回 400。请用 `fetch` |
| `chrome.webview.postMessage`、`chrome.webview.hostObjects` | 不可用 |
| `manifest` 中的 `http_allowed_urls` | 不作用于设置页 |

### 5.4 可提供的文件类型

扩展名不在下表中的文件返回 404：

| 类型 | 扩展名 |
| --- | --- |
| 页面、脚本、样式 | `.html` `.htm` `.js` `.mjs` `.css` |
| 数据 | `.json` `.map` `.txt` |
| 图片 | `.svg` `.png` `.jpg` `.jpeg` `.gif` `.webp` `.ico` |
| 字体 | `.woff` `.woff2` `.ttf` `.otf` |
| 其他 | `.wasm` `.mp3` `.ogg` `.wav` `.mp4` `.webm` |

### 5.5 大小限制

| 限制 | 值 | 适用 |
| --- | --- | --- |
| 单个文件 | 10 MB，超过返回 404 | 所有脚本 |
| 设置页目录文件数 | 500 个 | 从脚本仓库添加到 Pulonia 的脚本 |
| 设置页目录总大小 | 20 MB | 同上 |

仓库脚本在打开设置时要把设置页目录提取出来，所以有整体限制；超过时添加脚本会被拒绝。入口直接放在项目根目录时，整个项目（包括识别素材）都算设置页目录，很容易超限，**建议把设置页放进单独的子目录**。

## 6. 设置值在脚本中的读取

### 6.1 保存位置

| 入口 | 保存位置 |
| --- | --- |
| 调度器 | 配置组文件 `User/ScriptGroup/{配置组}.json`，每个配置组里的每个脚本各一份 |
| Pulonia（任务计划、新一条龙） | 任务节点的参数 `settings`，每个任务各一份 |

同一个脚本在不同配置组或不同任务中有各自的设置。脚本运行时读取的始终是本次运行对应的那一份。

### 6.2 值的类型

| 设置值 | 在 `main.js` 中 |
| --- | --- |
| 字符串、布尔 | 直接使用 |
| 数字 | 可以直接使用；同时提供基础表单时，`input-text` 会存成字符串，建议统一用 `Number(settings.xxx)` 读取 |
| 缺少的键 | `undefined`，用 `??` 提供默认值 |
| 数组、嵌套对象 | 是 .NET 对象，不是 JS 数组或对象，`length`、`map`、`JSON.stringify` 等写法不可用；不同入口的具体类型也不同 |

需要保存列表或结构化数据时，**存成 JSON 字符串**：

```js
// 设置页
next.routes = JSON.stringify([{ name: "蒙德", count: 3 }, { name: "璃月", count: 2 }]);

// main.js
const routes = JSON.parse(settings.routes ?? "[]");
```

另外：

- 脚本读取 `settings.xxx` 时不区分大小写，不要使用只有大小写不同的键。
- 脚本运行期间保存的新设置，下次运行才生效。

## 7. 与 `settings_ui` 共存，兼容旧版本

两者可以同时声明：

```json
{
  "settings_ui": "settings.json",
  "settings_html": "settings/index.html"
}
```

| BetterGI | `settings_ui` | `settings_html` | 打开设置时 |
| --- | --- | --- | --- |
| 支持 HTML 设置页 | 有 | 有 | 打开 HTML 设置页；打不开时提示原因，改用基础表单 |
| 支持 HTML 设置页 | 无 | 有 | 打开 HTML 设置页；打不开时只提示原因 |
| 支持 HTML 设置页 | 有 | 无 | 基础表单（与以前相同） |
| 旧版本 | 有 | 有 | 基础表单 |
| 旧版本 | 无 | 有 | 提示“此脚本未提供自定义配置” |

HTML 设置页打不开的情况：未安装 Microsoft Edge WebView2 运行时、`settings_html` 路径不合法、入口文件不存在、仓库脚本的设置页目录超出限制。

两者读写的是同一份设置值。同时提供时，请让键名和值类型一致，否则基础表单可能显示不正确：

| `settings_ui` 类型 | HTML 设置页应保存的值 |
| --- | --- |
| `input-text` | 字符串（数字也存成字符串，或者在 `main.js` 中用 `Number()` 读取） |
| `select` | 字符串，取值为 `options` 之一 |
| `cascade-select` | 字符串，取值为第二级选项 |
| `checkbox` | 布尔 |
| `multi-checkbox` | 字符串数组，元素为 `options` 中的值（调度器运行前会删掉不在 `options` 中的值） |

基础表单打开时会把 `settings_ui` 的默认值写入设置，HTML 设置页不会，所以页面和 `main.js` 都要处理“键不存在”的情况。

只想支持新版本时，可以在 manifest 中用 `bgi_version` 声明最低版本，低于该版本的 BetterGI 会在加载脚本时记录错误日志，提醒用户更新。

## 8. 用户在哪里打开设置

| 入口 | 操作 | 说明 |
| --- | --- | --- |
| 调度器 | 配置组中右键脚本 →“修改JS脚本自定义配置” | 每次保存立即写入配置组文件 |
| 任务计划 | 节点的“编辑配置”→“脚本自定义设置” | 每次保存是一次可撤销的编辑 |
| 任务计划、新一条龙 | 添加 JS 任务，在添加弹窗中确认后 | `mode` 为 `create`。关闭页面时用最后一次保存的值添加任务；一次都没保存时，BetterGI 询问用户是否仍然添加 |

在“JS 脚本”列表页直接运行脚本时没有设置入口，设置为空。声明了设置的脚本会提示用户添加到调度器后再修改设置。

## 9. 调试

### 9.1 在 BetterGI 中调试

- 把脚本放在 `User/JsScript/{目录}` 中，通过调度器打开设置。页面文件每次都直接从磁盘读取，不缓存，修改后在设置窗口里按 **F5** 就能看到效果。
- 按 **F12** 打开开发者工具，可以查看控制台、网络请求和接口返回的 `error`。
- 从脚本仓库添加到 Pulonia 的脚本，页面来自添加时固定的版本，修改仓库中的文件不会立即生效，需要更新仓库并在任务计划中确认更新资源版本。

### 9.2 在普通浏览器中调试

可以用任意静态服务器把设置页目录作为根目录打开，再放两个模拟文件：

```text
settings/
├─ index.html
└─ __bgi/
   ├─ settings      内容为一个 JSON 对象，例如 {"rounds":5}
   └─ context       可选，内容参考 4.3 节
```

```powershell
# 在设置页目录中启动静态服务器，然后访问 http://localhost:8000/index.html
cd MyScript/settings
python -m http.server 8000
```

BetterGI 不会把 `__bgi` 子目录中的文件提供给页面，这两个文件可以留在发布包里。

静态服务器不支持 `POST`，保存会失败。可以按主机名判断是否在 BetterGI 中，在浏览器里改为打印：

```js
const inBetterGI = location.hostname.endsWith(".bettergi.local");
if (!inBetterGI) {
  bgiSettings.save = async settings => console.log("模拟保存", settings);
  bgiSettings.close = async () => console.log("模拟关闭");
}
```

## 10. 发布前检查

- [ ] `settings_html` 指向子目录中的入口，例如 `settings/index.html`
- [ ] 所有脚本、样式、图片、字体都在设置页目录内，没有引用 CDN 或在线资源
- [ ] 设置页目录不超过 500 个文件、20 MB，单个文件不超过 10 MB
- [ ] 保存时以读取到的设置为基础修改，提交完整对象
- [ ] 页面和 `main.js` 都处理了键不存在的情况
- [ ] 列表和结构化数据存成 JSON 字符串
- [ ] 保存失败时把 `error` 显示给用户；页面上能看出是否已保存
- [ ] 关闭窗口使用 `POST /__bgi/close`
- [ ] 在深色、浅色主题和较小的窗口尺寸下检查过布局
- [ ] 需要兼容旧版本时同时提供 `settings_ui`，键名和类型一致
- [ ] 在调度器和任务计划中各打开、保存一次，并实际运行脚本确认读到的值

## 11. 常见问题

**打开设置时提示“manifest 指定的设置页不存在”。**
检查 `settings_html` 的路径和大小写，路径相对项目根目录（`manifest.json` 所在目录），不是相对设置页目录。

**页面是空白的，或样式、图片没有加载。**
按 F12 查看控制台。常见原因：引用了 CDN 或在线字体；文件扩展名不在 5.4 节的列表中；文件在设置页目录之外（例如入口在 `settings/`，却引用了 `../assets/a.png`）。

**保存返回 400。**
请求体必须是 JSON 对象。检查是否用了 `JSON.stringify`，是否提交了数组或字符串，是否用 `<form>` 直接提交。

**保存成功了，但脚本读到的还是旧值。**
确认运行的是同一个配置组或同一个任务中的脚本；脚本运行期间保存的值下次运行才生效；键名是否与 `main.js` 中一致。

**`window.close()` 没反应。**
页面跳转过之后浏览器会忽略 `window.close()`，改用 `POST /__bgi/close`。

**保存后有些设置不见了。**
保存是整体替换。提交前请以 `GET /__bgi/settings` 读到的对象为基础修改，而不是只提交页面上展示的字段。

**能不能在设置页里请求网络数据，例如在线读取活动列表？**
目前不能，设置页禁止访问外网。可以把数据随脚本一起发布，或者由 `main.js` 在运行时获取。
