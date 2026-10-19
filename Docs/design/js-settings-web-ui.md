# JS 脚本 HTML 配置入口

`manifest.json` 可以同时保留旧的 `settings_ui`，并新增独立配置会话：

```json
{
  "settings_web_ui": {
    "html": "web/index.html",
    "script": "settings-web.js"
  }
}
```

调度器中的 JS 条目会在“修改JS脚本自定义配置”下方显示“打开JS脚本HTML配置”。点击后打开普通 WebView，并只执行 `settings-web.js`，不会执行或导入 `main.js`。

配置后端可使用 `log`、`file`、`strategyFile`、`http`、`sleep`、`settings`、`settingsContext` 和只绑定当前窗口的 `settingsUi`。消息 envelope 仍是 `url + data + requestId`，但配置接口不是 `htmlMask` 的别名。`settingsContext` 提供 `cancellationToken`、`loadSettings()` 和 `saveSettings(json)`。读取页面回填数据使用 `loadSettings()` 返回的 JSON 字符串；保存仍整体替换当前调度器条目。脚本自有文件继续通过 `file` 保存。

后端方法是 `isOpen`、`close`、`post`、`ask`、`reply`、`receive`、`poll`、`pollAll`，全部不接受窗口 ID。`post`、`ask`、`reply` 直接接收脚本对象，由宿主序列化。页面桥是 `window.settingsUi`，提供 `post(url, data)`、`request(url, data)` 和 `onMessage`；监听器注册前的推送会排队。迁移时对照替换即可：`exists -> isOpen`、`send -> post`、`request -> ask`、`respond -> reply`，并删除每一个窗口 ID。旧的 `htmlMask` 调用会直接失败，不提供兼容重载。

配置会话不提供游戏窗口、截图、识图、键鼠、`KeyMouseHook`、`dispatcher` 或自动任务能力。原遮罩中的定位、点击和播放控制需要删除，或留在 `main.js`。

## 主流程与配置页的数据边界

配置页和主流程读取的是同一份调度器配置，但两边拿到的数据类型不同，不能共用同一套读取方式。

- 配置后端调用 `settingsContext.loadSettings()`，得到普通 JSON 字符串。页面回填、消息推送和保存都使用这份快照。
- 配置后端调用 `settingsContext.saveSettings(json)`，参数必须是 JSON 字符串。宿主会把它展开成动态配置对象并写入调度器配置组。
- 主流程没有配置桥。宿主仍通过 `settings` 注入启动时的动态配置对象，`main.js` 继续读取 `settings.字段名`。
- 不要在主流程里调用 `settingsUi`、`settingsContext` 或 `JSON.stringify(settings)`。前者只存在于配置会话，后者不能可靠展开动态对象的字段。

主流程把动态字段当字符串使用前，要先取成普通字符串：

```js
function settingText(value) {
  if (value === undefined || value === null) return "";
  return "" + value;
}

const processPath = settingText(settings.process_path);
const relativePath = processPath.replace(/^process[\\/]/, "");
```

布尔开关可以直接做条件判断，例如 `if (!settings.noSkip)`。不要用 `String(settings.process_path)` 把动态对象交给后续的字符串方法；它不一定得到配置里的文本。

配置保存必须合并旧快照。`saveSettings` 是整份替换，页面只提交修改字段时会删掉未提交的数据：

```js
const current = JSON.parse(settingsContext.loadSettings());
const next = { ...current, process_path: processPath };
settingsContext.saveSettings(JSON.stringify(next));
```

如果页面消息里的字段本身可能来自动态对象，先用 `JSON.parse(JSON.stringify(value))` 收成普通 JSON，再合并和保存。保存成功后不要把返回值赋回宿主注入的 `settings`，它是只读绑定。

主流程只消费已经保存的结果。剧情选择、目录扫描和全局配置都留在 HTML 配置页；主流程启动时直接读取 `settings.process_path` 等字段，不再打开遮罩选择器。配置页保存的新值从下一次主流程运行生效。
