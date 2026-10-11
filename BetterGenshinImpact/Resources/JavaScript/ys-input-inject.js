(() => {
  const GLOBAL_KEY = "__ysInputInject";
  const VERSION = "1.0.0";
  const MODULE_ID_CLIENT_CORE = 53638;
  const DEFAULTS = {
    absX: 0.5,
    absY: 0.5,
    stepIntervalMs: 8,
    keyHoldMs: 60,
    clickHoldMs: 35,
    comboHoldMs: 80,
    comboGapMs: 30,
    wheelStepMs: 16,
    dragButton: "left",
    debug: false
  };

  if (window[GLOBAL_KEY] && window[GLOBAL_KEY].version) {
    console.info(`[YSInputInject] reuse ${window[GLOBAL_KEY].version}`);
    return window[GLOBAL_KEY];
  }

  const state = {
    pressedKeys: new Map(),
    pressedButtons: new Set(),
    defaults: { ...DEFAULTS }
  };

  const aliasToCode = {
    w: "KeyW",
    a: "KeyA",
    s: "KeyS",
    d: "KeyD",
    q: "KeyQ",
    e: "KeyE",
    r: "KeyR",
    f: "KeyF",
    c: "KeyC",
    x: "KeyX",
    z: "KeyZ",
    v: "KeyV",
    b: "KeyB",
    n: "KeyN",
    m: "KeyM",
    i: "KeyI",
    j: "KeyJ",
    k: "KeyK",
    l: "KeyL",
    o: "KeyO",
    p: "KeyP",
    u: "KeyU",
    y: "KeyY",
    t: "KeyT",
    g: "KeyG",
    h: "KeyH",
    esc: "Escape",
    escape: "Escape",
    enter: "Enter",
    tab: "Tab",
    space: "Space",
    spacebar: "Space",
    shift: "ShiftLeft",
    lshift: "ShiftLeft",
    rshift: "ShiftRight",
    ctrl: "ControlLeft",
    control: "ControlLeft",
    lctrl: "ControlLeft",
    rctrl: "ControlRight",
    alt: "AltLeft",
    lalt: "AltLeft",
    ralt: "AltRight",
    meta: "MetaLeft",
    cmd: "MetaLeft",
    win: "MetaLeft",
    up: "ArrowUp",
    down: "ArrowDown",
    left: "ArrowLeft",
    right: "ArrowRight",
    pgup: "PageUp",
    pgdn: "PageDown",
    del: "Delete",
    ins: "Insert",
    bs: "Backspace",
    caps: "CapsLock",
    home: "Home",
    end: "End",
    plus: "Equal",
    minus: "Minus",
    comma: "Comma",
    period: "Period",
    slash: "Slash",
    semicolon: "Semicolon",
    quote: "Quote",
    bracketleft: "BracketLeft",
    bracketright: "BracketRight",
    backslash: "Backslash",
    backquote: "Backquote",
    num0: "Digit0",
    num1: "Digit1",
    num2: "Digit2",
    num3: "Digit3",
    num4: "Digit4",
    num5: "Digit5",
    num6: "Digit6",
    num7: "Digit7",
    num8: "Digit8",
    num9: "Digit9",
    f1: "F1",
    f2: "F2",
    f3: "F3",
    f4: "F4",
    f5: "F5",
    f6: "F6",
    f7: "F7",
    f8: "F8",
    f9: "F9",
    f10: "F10",
    f11: "F11",
    f12: "F12"
  };

  const keyCodeMap = {
    Escape: 27,
    Tab: 9,
    CapsLock: 20,
    ShiftLeft: 16,
    ShiftRight: 16,
    ControlLeft: 17,
    ControlRight: 17,
    AltLeft: 18,
    AltRight: 18,
    MetaLeft: 91,
    MetaRight: 92,
    Space: 32,
    Enter: 13,
    Backspace: 8,
    Delete: 46,
    Insert: 45,
    Home: 36,
    End: 35,
    PageUp: 33,
    PageDown: 34,
    ArrowUp: 38,
    ArrowDown: 40,
    ArrowLeft: 37,
    ArrowRight: 39,
    PrintScreen: 44,
    ScrollLock: 145,
    Pause: 19,
    Minus: 189,
    Equal: 187,
    BracketLeft: 219,
    BracketRight: 221,
    Backslash: 220,
    Semicolon: 186,
    Quote: 222,
    Backquote: 192,
    Comma: 188,
    Period: 190,
    Slash: 191,
    NumpadMultiply: 106,
    NumpadAdd: 107,
    NumpadSubtract: 109,
    NumpadDecimal: 110,
    NumpadDivide: 111,
    NumLock: 144
  };

  const buttonMap = {
    left: ["mouseLeftButtonDown", "mouseLeftButtonUp"],
    right: ["mouseRightButtonDown", "mouseRightButtonUp"],
    middle: ["mouseMiddleButtonDown", "mouseMiddleButtonUp"]
  };

  function log(...args) {
    if (state.defaults.debug) {
      console.debug("[YSInputInject]", ...args);
    }
  }

  function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  function clamp01(value, fallback) {
    const num = Number(value);
    if (!Number.isFinite(num)) {
      return fallback;
    }
    if (num < 0) {
      return 0;
    }
    if (num > 1) {
      return 1;
    }
    return num;
  }

  function toFiniteNumber(value, name) {
    const num = Number(value);
    if (!Number.isFinite(num)) {
      throw new Error(`${name} must be a finite number`);
    }
    return num;
  }

  function getWebpackRequire() {
    if (window.__ysWebpackRequire) {
      return window.__ysWebpackRequire;
    }
    const chunk = window.webpackChunkcloudgame_web_app;
    if (!chunk || typeof chunk.push !== "function") {
      throw new Error("webpack chunk runtime not found");
    }
    let req = null;
    chunk.push([[Symbol("ys-input-inject")], {}, (runtimeRequire) => {
      req = runtimeRequire;
    }]);
    if (!req) {
      throw new Error("failed to resolve webpack require");
    }
    window.__ysWebpackRequire = req;
    return req;
  }

  function getCore() {
    const req = getWebpackRequire();
    const mod = req(MODULE_ID_CLIENT_CORE);
    if (!mod || !mod.$) {
      throw new Error("ClientCore module not available");
    }
    return mod.$;
  }

  function ensureOpen() {
    const core = getCore();
    const readyState =
      core &&
      core.rtcConnection &&
      core.rtcConnection.rtcDataChannel &&
      core.rtcConnection.rtcDataChannel.readyState;
    if (readyState !== "open") {
      throw new Error(`rtc data channel is not open: ${readyState || "unknown"}`);
    }
    return core;
  }

  function normalizeCode(input) {
    if (!input) {
      throw new Error("key code is required");
    }
    const raw = String(input).trim();
    if (!raw) {
      throw new Error("key code is empty");
    }
    const alias = aliasToCode[raw.toLowerCase()];
    if (alias) {
      return alias;
    }
    if (/^Key[A-Z]$/.test(raw)) {
      return raw;
    }
    if (/^Digit[0-9]$/.test(raw)) {
      return raw;
    }
    if (/^F([1-9]|1[0-2])$/.test(raw)) {
      return raw;
    }
    if (/^Numpad[0-9]$/.test(raw)) {
      return raw;
    }
    return raw;
  }

  function getKeyCodeFromCode(code) {
    if (Object.prototype.hasOwnProperty.call(keyCodeMap, code)) {
      return keyCodeMap[code];
    }
    if (/^Key[A-Z]$/.test(code)) {
      return code.charCodeAt(3);
    }
    if (/^Digit[0-9]$/.test(code)) {
      return code.charCodeAt(5);
    }
    if (/^F([1-9]|1[0-2])$/.test(code)) {
      return 111 + Number(code.slice(1));
    }
    if (/^Numpad[0-9]$/.test(code)) {
      return 96 + Number(code.slice(6));
    }
    throw new Error(`unsupported key code: ${code}`);
  }

  function getKeyFromCode(code) {
    if (/^Key[A-Z]$/.test(code)) {
      return code.slice(3).toLowerCase();
    }
    if (/^Digit[0-9]$/.test(code)) {
      return code.slice(5);
    }
    if (/^F([1-9]|1[0-2])$/.test(code)) {
      return code;
    }
    if (/^Numpad[0-9]$/.test(code)) {
      return code.slice(6);
    }
    switch (code) {
      case "Escape":
        return "Escape";
      case "Tab":
        return "Tab";
      case "CapsLock":
        return "CapsLock";
      case "ShiftLeft":
      case "ShiftRight":
        return "Shift";
      case "ControlLeft":
      case "ControlRight":
        return "Control";
      case "AltLeft":
      case "AltRight":
        return "Alt";
      case "MetaLeft":
      case "MetaRight":
        return "Meta";
      case "Space":
        return " ";
      case "Enter":
        return "Enter";
      case "Backspace":
        return "Backspace";
      case "Delete":
        return "Delete";
      case "Insert":
        return "Insert";
      case "Home":
        return "Home";
      case "End":
        return "End";
      case "PageUp":
        return "PageUp";
      case "PageDown":
        return "PageDown";
      case "ArrowUp":
        return "ArrowUp";
      case "ArrowDown":
        return "ArrowDown";
      case "ArrowLeft":
        return "ArrowLeft";
      case "ArrowRight":
        return "ArrowRight";
      case "Minus":
        return "-";
      case "Equal":
        return "=";
      case "BracketLeft":
        return "[";
      case "BracketRight":
        return "]";
      case "Backslash":
        return "\\";
      case "Semicolon":
        return ";";
      case "Quote":
        return "'";
      case "Backquote":
        return "`";
      case "Comma":
        return ",";
      case "Period":
        return ".";
      case "Slash":
        return "/";
      case "NumLock":
        return "NumLock";
      case "NumpadAdd":
        return "+";
      case "NumpadSubtract":
        return "-";
      case "NumpadMultiply":
        return "*";
      case "NumpadDivide":
        return "/";
      case "NumpadDecimal":
        return ".";
      default:
        return code;
    }
  }

  function getLocationFromCode(code) {
    if (
      code.endsWith("Left") ||
      code === "NumpadMultiply" ||
      code === "NumpadAdd" ||
      code === "NumpadSubtract" ||
      code === "NumpadDecimal" ||
      code === "NumpadDivide" ||
      /^Numpad[0-9]$/.test(code)
    ) {
      return code.endsWith("Left") ? 1 : 3;
    }
    if (code.endsWith("Right")) {
      return 2;
    }
    return 0;
  }

  function buildKeyEventLike(type, input, extra = {}) {
    const code = normalizeCode(input);
    const keyCode = extra.keyCode ?? getKeyCodeFromCode(code);
    const altKey =
      extra.altKey ?? (code === "AltLeft" || code === "AltRight");
    const ctrlKey =
      extra.ctrlKey ??
      (code === "ControlLeft" || code === "ControlRight");
    const shiftKey =
      extra.shiftKey ?? (code === "ShiftLeft" || code === "ShiftRight");
    const metaKey =
      extra.metaKey ?? (code === "MetaLeft" || code === "MetaRight");
    const capsLockOn = !!extra.capsLockOn;
    const numLockOn = !!extra.numLockOn;
    const eventLike = {
      type,
      key: extra.key ?? getKeyFromCode(code),
      code,
      keyCode,
      which: extra.which ?? keyCode,
      charCode: extra.charCode ?? 0,
      location: extra.location ?? getLocationFromCode(code),
      repeat: !!extra.repeat,
      isComposing: !!extra.isComposing,
      altKey,
      ctrlKey,
      shiftKey,
      metaKey,
      bubbles: true,
      cancelable: true,
      getModifierState(name) {
        switch (name) {
          case "Alt":
            return this.altKey;
          case "Control":
            return this.ctrlKey;
          case "Shift":
            return this.shiftKey;
          case "Meta":
            return this.metaKey;
          case "CapsLock":
            return capsLockOn;
          case "NumLock":
            return numLockOn;
          default:
            return false;
        }
      }
    };
    return eventLike;
  }

  function normalizeButton(button) {
    const name = String(button || "").trim().toLowerCase();
    if (!buttonMap[name]) {
      throw new Error(`unsupported mouse button: ${button}`);
    }
    return name;
  }

  function makeAbsPosition(x, y) {
    return {
      x: clamp01(x, state.defaults.absX),
      y: clamp01(y, state.defaults.absY)
    };
  }

  function getStatus() {
    const core = getCore();
    return {
      version: VERSION,
      defaults: { ...state.defaults },
      pressedKeys: Array.from(state.pressedKeys.keys()),
      pressedButtons: Array.from(state.pressedButtons.values()),
      stateMachine:
        core && core.stateMachine && typeof core.stateMachine.getState === "function"
          ? core.stateMachine.getState()
          : null,
      transportProtocol: core ? core.transportProtocol : null,
      rtcDataChannelState:
        core &&
        core.rtcConnection &&
        core.rtcConnection.rtcDataChannel &&
        core.rtcConnection.rtcDataChannel.readyState,
      gameDataStarted:
        core && core.gameDataChannel ? core.gameDataChannel.isStarted : null
    };
  }

  function setDefaults(partial = {}) {
    if (Object.prototype.hasOwnProperty.call(partial, "absX")) {
      state.defaults.absX = clamp01(partial.absX, state.defaults.absX);
    }
    if (Object.prototype.hasOwnProperty.call(partial, "absY")) {
      state.defaults.absY = clamp01(partial.absY, state.defaults.absY);
    }
    if (Object.prototype.hasOwnProperty.call(partial, "stepIntervalMs")) {
      state.defaults.stepIntervalMs = Math.max(
        0,
        Math.floor(toFiniteNumber(partial.stepIntervalMs, "stepIntervalMs"))
      );
    }
    if (Object.prototype.hasOwnProperty.call(partial, "keyHoldMs")) {
      state.defaults.keyHoldMs = Math.max(
        0,
        Math.floor(toFiniteNumber(partial.keyHoldMs, "keyHoldMs"))
      );
    }
    if (Object.prototype.hasOwnProperty.call(partial, "clickHoldMs")) {
      state.defaults.clickHoldMs = Math.max(
        0,
        Math.floor(toFiniteNumber(partial.clickHoldMs, "clickHoldMs"))
      );
    }
    if (Object.prototype.hasOwnProperty.call(partial, "comboHoldMs")) {
      state.defaults.comboHoldMs = Math.max(
        0,
        Math.floor(toFiniteNumber(partial.comboHoldMs, "comboHoldMs"))
      );
    }
    if (Object.prototype.hasOwnProperty.call(partial, "comboGapMs")) {
      state.defaults.comboGapMs = Math.max(
        0,
        Math.floor(toFiniteNumber(partial.comboGapMs, "comboGapMs"))
      );
    }
    if (Object.prototype.hasOwnProperty.call(partial, "wheelStepMs")) {
      state.defaults.wheelStepMs = Math.max(
        0,
        Math.floor(toFiniteNumber(partial.wheelStepMs, "wheelStepMs"))
      );
    }
    if (Object.prototype.hasOwnProperty.call(partial, "debug")) {
      state.defaults.debug = !!partial.debug;
    }
    return getStatus();
  }

  function setAbsPosition(x, y) {
    state.defaults.absX = clamp01(x, state.defaults.absX);
    state.defaults.absY = clamp01(y, state.defaults.absY);
    return { absX: state.defaults.absX, absY: state.defaults.absY };
  }

  function mouseMove(dx, dy, x = state.defaults.absX, y = state.defaults.absY) {
    const core = ensureOpen();
    const rel = {
      x: toFiniteNumber(dx, "dx"),
      y: toFiniteNumber(dy, "dy")
    };
    const abs = makeAbsPosition(x, y);
    core.mouseMove(abs, rel);
    log("mouseMove", { abs, rel });
    return { ok: true, abs, rel };
  }

  async function moveSteps(
    dx,
    dy,
    steps = 8,
    intervalMs = state.defaults.stepIntervalMs,
    x = state.defaults.absX,
    y = state.defaults.absY
  ) {
    const totalSteps = Math.max(1, Math.floor(toFiniteNumber(steps, "steps")));
    const interval = Math.max(0, Math.floor(toFiniteNumber(intervalMs, "intervalMs")));
    for (let index = 0; index < totalSteps; index += 1) {
      mouseMove(dx / totalSteps, dy / totalSteps, x, y);
      if (index !== totalSteps - 1 && interval > 0) {
        await sleep(interval);
      }
    }
    return { ok: true, dx, dy, steps: totalSteps, intervalMs: interval };
  }

  async function movePath(points, intervalMs = state.defaults.stepIntervalMs) {
    if (!Array.isArray(points) || points.length === 0) {
      throw new Error("points must be a non-empty array");
    }
    const interval = Math.max(0, Math.floor(toFiniteNumber(intervalMs, "intervalMs")));
    for (let index = 0; index < points.length; index += 1) {
      const point = points[index] || {};
      mouseMove(
        point.dx ?? 0,
        point.dy ?? 0,
        point.x ?? state.defaults.absX,
        point.y ?? state.defaults.absY
      );
      if (index !== points.length - 1 && interval > 0) {
        await sleep(interval);
      }
    }
    return { ok: true, count: points.length, intervalMs: interval };
  }

  async function look(
    dx,
    dy,
    steps = 8,
    intervalMs = state.defaults.stepIntervalMs,
    x = state.defaults.absX,
    y = state.defaults.absY
  ) {
    return moveSteps(dx, dy, steps, intervalMs, x, y);
  }

  async function nudge(direction, distance = 24, steps = 4, intervalMs = state.defaults.stepIntervalMs) {
    const dir = String(direction || "").trim().toLowerCase();
    switch (dir) {
      case "up":
        return moveSteps(0, -distance, steps, intervalMs);
      case "down":
        return moveSteps(0, distance, steps, intervalMs);
      case "left":
        return moveSteps(-distance, 0, steps, intervalMs);
      case "right":
        return moveSteps(distance, 0, steps, intervalMs);
      default:
        throw new Error(`unsupported direction: ${direction}`);
    }
  }

  async function jitter(distance = 8, count = 6, intervalMs = state.defaults.stepIntervalMs) {
    const totalCount = Math.max(1, Math.floor(toFiniteNumber(count, "count")));
    const amount = toFiniteNumber(distance, "distance");
    for (let index = 0; index < totalCount; index += 1) {
      const sign = index % 2 === 0 ? 1 : -1;
      mouseMove(sign * amount, 0);
      if (intervalMs > 0) {
        await sleep(intervalMs);
      }
      mouseMove(0, sign * amount);
      if (intervalMs > 0 && index !== totalCount - 1) {
        await sleep(intervalMs);
      }
    }
    return { ok: true, distance: amount, count: totalCount };
  }

  async function circle(radius = 30, steps = 24, turns = 1, intervalMs = state.defaults.stepIntervalMs) {
    const totalSteps = Math.max(4, Math.floor(toFiniteNumber(steps, "steps")));
    const totalTurns = Math.max(1, Math.floor(toFiniteNumber(turns, "turns")));
    const interval = Math.max(0, Math.floor(toFiniteNumber(intervalMs, "intervalMs")));
    const r = toFiniteNumber(radius, "radius");
    let prevX = 0;
    let prevY = 0;
    const count = totalSteps * totalTurns;
    for (let index = 1; index <= count; index += 1) {
      const angle = (Math.PI * 2 * index) / totalSteps;
      const targetX = Math.cos(angle) * r;
      const targetY = Math.sin(angle) * r;
      mouseMove(targetX - prevX, targetY - prevY);
      prevX = targetX;
      prevY = targetY;
      if (index !== count && interval > 0) {
        await sleep(interval);
      }
    }
    mouseMove(-prevX, -prevY);
    return { ok: true, radius: r, steps: totalSteps, turns: totalTurns };
  }

  function mouseDown(button = "left", x = state.defaults.absX, y = state.defaults.absY) {
    const name = normalizeButton(button);
    const [methodName] = buttonMap[name];
    const core = ensureOpen();
    const abs = makeAbsPosition(x, y);
    core[methodName](abs);
    state.pressedButtons.add(name);
    log("mouseDown", { button: name, abs });
    return { ok: true, button: name, abs };
  }

  function mouseUp(button = "left", x = state.defaults.absX, y = state.defaults.absY) {
    const name = normalizeButton(button);
    const [, methodName] = buttonMap[name];
    const core = ensureOpen();
    const abs = makeAbsPosition(x, y);
    core[methodName](abs);
    state.pressedButtons.delete(name);
    log("mouseUp", { button: name, abs });
    return { ok: true, button: name, abs };
  }

  async function click(
    button = "left",
    holdMs = state.defaults.clickHoldMs,
    x = state.defaults.absX,
    y = state.defaults.absY
  ) {
    mouseDown(button, x, y);
    if (holdMs > 0) {
      await sleep(Math.max(0, Math.floor(toFiniteNumber(holdMs, "holdMs"))));
    }
    mouseUp(button, x, y);
    return { ok: true, button: normalizeButton(button), holdMs };
  }

  async function doubleClick(
    button = "left",
    gapMs = 70,
    holdMs = state.defaults.clickHoldMs,
    x = state.defaults.absX,
    y = state.defaults.absY
  ) {
    await click(button, holdMs, x, y);
    if (gapMs > 0) {
      await sleep(Math.max(0, Math.floor(toFiniteNumber(gapMs, "gapMs"))));
    }
    await click(button, holdMs, x, y);
    return { ok: true, button: normalizeButton(button), gapMs, holdMs };
  }

  async function repeatClick(
    button = "left",
    times = 5,
    intervalMs = 100,
    holdMs = state.defaults.clickHoldMs,
    x = state.defaults.absX,
    y = state.defaults.absY
  ) {
    const count = Math.max(1, Math.floor(toFiniteNumber(times, "times")));
    const interval = Math.max(0, Math.floor(toFiniteNumber(intervalMs, "intervalMs")));
    for (let index = 0; index < count; index += 1) {
      await click(button, holdMs, x, y);
      if (index !== count - 1 && interval > 0) {
        await sleep(interval);
      }
    }
    return { ok: true, button: normalizeButton(button), times: count };
  }

  async function drag(
    dx,
    dy,
    options = {}
  ) {
    const button = options.button ?? state.defaults.dragButton;
    const steps = options.steps ?? 10;
    const intervalMs = options.intervalMs ?? state.defaults.stepIntervalMs;
    const x = options.x ?? state.defaults.absX;
    const y = options.y ?? state.defaults.absY;
    const holdMs = options.holdMs ?? state.defaults.clickHoldMs;
    mouseDown(button, x, y);
    if (holdMs > 0) {
      await sleep(Math.max(0, Math.floor(toFiniteNumber(holdMs, "holdMs"))));
    }
    await moveSteps(dx, dy, steps, intervalMs, x, y);
    mouseUp(button, x, y);
    return { ok: true, button: normalizeButton(button), dx, dy, steps };
  }

  function scroll(delta) {
    const core = ensureOpen();
    const value = toFiniteNumber(delta, "delta");
    core.mouseScroll(value);
    log("scroll", value);
    return { ok: true, delta: value };
  }

  async function scrollSteps(totalDelta, steps = 6, intervalMs = state.defaults.wheelStepMs) {
    const count = Math.max(1, Math.floor(toFiniteNumber(steps, "steps")));
    const interval = Math.max(0, Math.floor(toFiniteNumber(intervalMs, "intervalMs")));
    const unit = toFiniteNumber(totalDelta, "totalDelta") / count;
    for (let index = 0; index < count; index += 1) {
      scroll(unit);
      if (index !== count - 1 && interval > 0) {
        await sleep(interval);
      }
    }
    return { ok: true, totalDelta, steps: count };
  }

  function keyDown(input, extra = {}) {
    const core = ensureOpen();
    const eventLike = buildKeyEventLike("keydown", input, extra);
    core.keyboardKeyDown(eventLike);
    state.pressedKeys.set(eventLike.code, eventLike);
    log("keyDown", eventLike);
    return {
      ok: true,
      code: eventLike.code,
      key: eventLike.key,
      keyCode: eventLike.keyCode
    };
  }

  function keyUp(input, extra = {}) {
    const core = ensureOpen();
    const normalizedCode = normalizeCode(input);
    const cached = state.pressedKeys.get(normalizedCode);
    const eventLike = buildKeyEventLike("keyup", normalizedCode, {
      ...cached,
      ...extra,
      code: normalizedCode
    });
    core.keyboardKeyUp(eventLike);
    state.pressedKeys.delete(normalizedCode);
    log("keyUp", eventLike);
    return {
      ok: true,
      code: eventLike.code,
      key: eventLike.key,
      keyCode: eventLike.keyCode
    };
  }

  async function tapKey(input, holdMs = state.defaults.keyHoldMs, extra = {}) {
    keyDown(input, extra);
    if (holdMs > 0) {
      await sleep(Math.max(0, Math.floor(toFiniteNumber(holdMs, "holdMs"))));
    }
    keyUp(input, extra);
    return { ok: true, code: normalizeCode(input), holdMs };
  }

  async function holdKeyFor(input, durationMs, extra = {}) {
    const duration = Math.max(0, Math.floor(toFiniteNumber(durationMs, "durationMs")));
    keyDown(input, extra);
    if (duration > 0) {
      await sleep(duration);
    }
    keyUp(input, extra);
    return { ok: true, code: normalizeCode(input), durationMs: duration };
  }

  function holdKeys(inputs, extra = {}) {
    if (!Array.isArray(inputs) || inputs.length === 0) {
      throw new Error("inputs must be a non-empty array");
    }
    return inputs.map((input) => keyDown(input, extra));
  }

  function releaseKeys(inputs, extra = {}) {
    if (!Array.isArray(inputs) || inputs.length === 0) {
      throw new Error("inputs must be a non-empty array");
    }
    return inputs.map((input) => keyUp(input, extra));
  }

  async function combo(
    inputs,
    holdMs = state.defaults.comboHoldMs,
    gapMs = state.defaults.comboGapMs,
    extra = {}
  ) {
    if (!Array.isArray(inputs) || inputs.length === 0) {
      throw new Error("inputs must be a non-empty array");
    }
    const gap = Math.max(0, Math.floor(toFiniteNumber(gapMs, "gapMs")));
    for (let index = 0; index < inputs.length; index += 1) {
      keyDown(inputs[index], extra);
      if (index !== inputs.length - 1 && gap > 0) {
        await sleep(gap);
      }
    }
    if (holdMs > 0) {
      await sleep(Math.max(0, Math.floor(toFiniteNumber(holdMs, "holdMs"))));
    }
    for (let index = inputs.length - 1; index >= 0; index -= 1) {
      keyUp(inputs[index], extra);
      if (index !== 0 && gap > 0) {
        await sleep(gap);
      }
    }
    return { ok: true, codes: inputs.map(normalizeCode), holdMs, gapMs: gap };
  }

  async function tapKeys(inputs, holdMs = state.defaults.keyHoldMs, gapMs = state.defaults.comboGapMs) {
    if (!Array.isArray(inputs) || inputs.length === 0) {
      throw new Error("inputs must be a non-empty array");
    }
    const gap = Math.max(0, Math.floor(toFiniteNumber(gapMs, "gapMs")));
    for (let index = 0; index < inputs.length; index += 1) {
      await tapKey(inputs[index], holdMs);
      if (index !== inputs.length - 1 && gap > 0) {
        await sleep(gap);
      }
    }
    return { ok: true, codes: inputs.map(normalizeCode), holdMs, gapMs: gap };
  }

  function releaseAllKeys() {
    const active = Array.from(state.pressedKeys.keys()).reverse();
    for (const code of active) {
      try {
        keyUp(code);
      } catch (error) {
        console.warn("[YSInputInject] releaseAllKeys failed", code, error);
      }
    }
    return { ok: true, released: active };
  }

  function releaseAllMouse(x = state.defaults.absX, y = state.defaults.absY) {
    const active = Array.from(state.pressedButtons.values());
    for (const button of active) {
      try {
        mouseUp(button, x, y);
      } catch (error) {
        console.warn("[YSInputInject] releaseAllMouse failed", button, error);
      }
    }
    return { ok: true, released: active };
  }

  function releaseAll() {
    const mouse = releaseAllMouse();
    const keyboard = releaseAllKeys();
    return { ok: true, mouse, keyboard };
  }

  async function withHold(inputs, action, extra = {}) {
    const codes = Array.isArray(inputs) ? inputs : [inputs];
    holdKeys(codes, extra);
    try {
      return await action(api);
    } finally {
      releaseKeys(codes, extra);
    }
  }

  async function forward(durationMs = 250) {
    return holdKeyFor("KeyW", durationMs);
  }

  async function backward(durationMs = 250) {
    return holdKeyFor("KeyS", durationMs);
  }

  async function left(durationMs = 250) {
    return holdKeyFor("KeyA", durationMs);
  }

  async function right(durationMs = 250) {
    return holdKeyFor("KeyD", durationMs);
  }

  async function jump(holdMs = 50) {
    return tapKey("Space", holdMs);
  }

  async function sprintForward(durationMs = 500) {
    return combo(["ShiftLeft", "KeyW"], durationMs, 0);
  }

  async function dashRight(durationMs = 300) {
    return combo(["ShiftLeft", "KeyD"], durationMs, 0);
  }

  async function dashLeft(durationMs = 300) {
    return combo(["ShiftLeft", "KeyA"], durationMs, 0);
  }

  function sendIme(text) {
    const core = ensureOpen();
    const payload = String(text ?? "");
    core.sendImeString(payload);
    log("sendIme", payload);
    return { ok: true, text: payload };
  }

  function sendClipboard(text) {
    const core = ensureOpen();
    const payload = String(text ?? "");
    core.sendClipboardString(payload);
    log("sendClipboard", payload);
    return { ok: true, text: payload };
  }

  function resetInput() {
    const core = ensureOpen();
    if (typeof core.resetInput === "function") {
      core.resetInput();
    }
    state.pressedKeys.clear();
    state.pressedButtons.clear();
    return { ok: true };
  }

  async function repeat(action, times = 1, intervalMs = 0) {
    if (typeof action !== "function") {
      throw new Error("action must be a function");
    }
    const count = Math.max(1, Math.floor(toFiniteNumber(times, "times")));
    const interval = Math.max(0, Math.floor(toFiniteNumber(intervalMs, "intervalMs")));
    const results = [];
    for (let index = 0; index < count; index += 1) {
      results.push(await action(index, api));
      if (index !== count - 1 && interval > 0) {
        await sleep(interval);
      }
    }
    return results;
  }

  async function run(sequence = []) {
    if (!Array.isArray(sequence)) {
      throw new Error("sequence must be an array");
    }
    const results = [];
    for (const step of sequence) {
      if (typeof step === "number") {
        await sleep(step);
        results.push({ type: "sleep", ms: step });
        continue;
      }
      if (typeof step === "function") {
        results.push(await step(api));
        continue;
      }
      if (!step || typeof step !== "object") {
        throw new Error("invalid sequence step");
      }
      switch (step.type) {
        case "sleep":
          await sleep(step.ms ?? 0);
          results.push({ type: "sleep", ms: step.ms ?? 0 });
          break;
        case "move":
          results.push(
            await moveSteps(
              step.dx ?? 0,
              step.dy ?? 0,
              step.steps ?? 1,
              step.intervalMs ?? state.defaults.stepIntervalMs,
              step.x ?? state.defaults.absX,
              step.y ?? state.defaults.absY
            )
          );
          break;
        case "click":
          results.push(
            await click(
              step.button ?? "left",
              step.holdMs ?? state.defaults.clickHoldMs,
              step.x ?? state.defaults.absX,
              step.y ?? state.defaults.absY
            )
          );
          break;
        case "doubleClick":
          results.push(
            await doubleClick(
              step.button ?? "left",
              step.gapMs ?? 70,
              step.holdMs ?? state.defaults.clickHoldMs,
              step.x ?? state.defaults.absX,
              step.y ?? state.defaults.absY
            )
          );
          break;
        case "scroll":
          results.push(scroll(step.delta ?? 0));
          break;
        case "keyDown":
          results.push(keyDown(step.code, step.extra));
          break;
        case "keyUp":
          results.push(keyUp(step.code, step.extra));
          break;
        case "tapKey":
          results.push(await tapKey(step.code, step.holdMs, step.extra));
          break;
        case "combo":
          results.push(
            await combo(
              step.codes ?? [],
              step.holdMs ?? state.defaults.comboHoldMs,
              step.gapMs ?? state.defaults.comboGapMs,
              step.extra
            )
          );
          break;
        case "text":
          results.push(
            step.mode === "clipboard"
              ? sendClipboard(step.text ?? "")
              : sendIme(step.text ?? "")
          );
          break;
        default:
          throw new Error(`unsupported sequence step type: ${step.type}`);
      }
    }
    return results;
  }

  function help() {
    return {
      version: VERSION,
      note: "Mouse movement goes to ClientCore directly and does not require pointer lock.",
      methods: [
        "status()",
        "help()",
        "sleep(ms)",
        "setDefaults(partial)",
        "setAbsPosition(x, y)",
        "mouseMove(dx, dy, x?, y?)",
        "moveSteps(dx, dy, steps?, intervalMs?, x?, y?)",
        "movePath(points, intervalMs?)",
        "look(dx, dy, steps?, intervalMs?, x?, y?)",
        "nudge(direction, distance?, steps?, intervalMs?)",
        "jitter(distance?, count?, intervalMs?)",
        "circle(radius?, steps?, turns?, intervalMs?)",
        "mouseDown(button?, x?, y?)",
        "mouseUp(button?, x?, y?)",
        "click(button?, holdMs?, x?, y?)",
        "doubleClick(button?, gapMs?, holdMs?, x?, y?)",
        "repeatClick(button?, times?, intervalMs?, holdMs?, x?, y?)",
        "drag(dx, dy, options?)",
        "scroll(delta)",
        "scrollSteps(totalDelta, steps?, intervalMs?)",
        "keyDown(code, extra?)",
        "keyUp(code, extra?)",
        "tapKey(code, holdMs?, extra?)",
        "holdKeyFor(code, durationMs, extra?)",
        "holdKeys(codes, extra?)",
        "releaseKeys(codes, extra?)",
        "combo(codes, holdMs?, gapMs?, extra?)",
        "tapKeys(codes, holdMs?, gapMs?)",
        "releaseAllKeys()",
        "releaseAllMouse()",
        "releaseAll()",
        "withHold(codes, asyncFn, extra?)",
        "forward(ms?)",
        "backward(ms?)",
        "left(ms?)",
        "right(ms?)",
        "jump(holdMs?)",
        "sprintForward(ms?)",
        "dashLeft(ms?)",
        "dashRight(ms?)",
        "sendIme(text)",
        "sendClipboard(text)",
        "resetInput()",
        "repeat(fn, times?, intervalMs?)",
        "run(sequence)"
      ],
      aliases: aliasToCode
    };
  }

  const api = {
    version: VERSION,
    state,
    getWebpackRequire,
    getCore,
    status: getStatus,
    help,
    sleep,
    setDefaults,
    setAbsPosition,
    normalizeCode,
    buildKeyEventLike,
    mouseMove,
    moveSteps,
    movePath,
    look,
    nudge,
    jitter,
    circle,
    mouseDown,
    mouseUp,
    click,
    doubleClick,
    repeatClick,
    drag,
    scroll,
    scrollSteps,
    keyDown,
    keyUp,
    tapKey,
    holdKeyFor,
    holdKeys,
    releaseKeys,
    combo,
    tapKeys,
    releaseAllKeys,
    releaseAllMouse,
    releaseAll,
    withHold,
    forward,
    backward,
    left,
    right,
    jump,
    sprintForward,
    dashLeft,
    dashRight,
    sendIme,
    sendClipboard,
    resetInput,
    repeat,
    run
  };

  Object.defineProperty(window, GLOBAL_KEY, {
    value: api,
    configurable: true,
    enumerable: false,
    writable: true
  });

  console.info(`[YSInputInject] installed ${VERSION}`);
  return api;
})();
