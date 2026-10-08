import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import WebSocket, { WebSocketServer } from 'ws'
import puppeteer from '../../../lib/puppeteer/puppeteer.js'

const pluginRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const configPath = path.join(pluginRoot, 'config', 'default.json')
const packageInfo = JSON.parse(fs.readFileSync(path.join(pluginRoot, 'package.json'), 'utf8'))
const yunzaiPackageInfo = JSON.parse(fs.readFileSync(path.resolve(pluginRoot, '..', '..', 'package.json'), 'utf8'))
let reverseServer
let reverseServerConfig
const reverseSockets = new Map()
let reverseQueue = Promise.resolve()
let clientSocket
let clientConnecting
let clientQueue = Promise.resolve()

function loadConfig() {
  return JSON.parse(fs.readFileSync(configPath, 'utf8'))
}

function getWebSocketUrl(config) {
  const host = String(config.host || '').trim()
  const port = Number(config.port)
  if (!host || !Number.isInteger(port) || port < 1024 || port > 65535) return null
  const formattedHost = host.includes(':') && !host.startsWith('[') ? `[${host}]` : host
  return `ws://${formattedHost}:${port}/`
}

function escapeHtml(value = '') {
  return String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char])
}

function parseCommand(message) {
  const text = String(message || '').replace(/<@!?[^>]+>/g, '').trim()
  const commands = ['#一条龙', '#启动截图器', '#关闭截图器', '#暂停任务', '#继续任务', '#停止任务', '#退出游戏']
  if (commands.includes(text)) return text
  for (const prefix of ['#执行调度器', '#启动任务']) {
    if (text.startsWith(prefix) && text.length > prefix.length) return `${prefix} ${text.slice(prefix.length).trim()}`
  }
  return null
}

export default class BetterGI extends plugin {
  constructor() {
    super({
      name: 'BetterGI 控制',
      dsc: '通过本机 WebSocket 控制 BetterGI 并返回状态卡片',
      event: 'message',
      // Yunzai 按 priority 升序执行；控制命令需先于常见业务插件拦截器处理。
      priority: 100,
      rule: [
        { reg: '^#(?:执行调度器|启动任务)\\s+.+$', fnc: 'control' },
        { reg: '^#(?:一条龙|启动截图器|关闭截图器|暂停任务|继续任务|停止任务|退出游戏)$', fnc: 'control' },
      ],
    })
    this.checkConnectionOnLoad()
  }

  checkConnectionOnLoad() {
    let config
    try { config = loadConfig() } catch (error) {
      logger.error(`[BetterGI] 插件加载失败，无法读取 config/default.json：${error.message}`)
      return
    }
    if (config.mode === 'server') {
      this.startReverseServer(config)
      return
    }
    const url = getWebSocketUrl(config)
    if (!url || !config.token) {
      logger.info(`[BetterGI] 插件已加载。请填写目标 IP、端口和令牌（当前配置 ${config.host || '未设置'}:${config.port || '未设置'}）`)
      return
    }
    logger.info(`[BetterGI] 插件已加载，收到控制命令时连接 BetterGI：${url}`)
  }

  startReverseServer(config) {
    const host = String(config.host || '0.0.0.0').trim()
    const port = Number(config.port)
    if (!Number.isInteger(port) || port < 1024 || port > 65535 || !config.token) {
      logger.error('[BetterGI] 反向 WebSocket 配置无效，请检查监听 IP、端口和令牌')
      return
    }
    const requestedConfig = `${host}:${port}:${config.token}`
    if (reverseServer && reverseServerConfig === requestedConfig && reverseServer.address()) {
      return
    }

    if (reverseServer) {
      for (const socket of reverseSockets.values()) socket.close()
      reverseSockets.clear()
      reverseServer.close()
      reverseServer = undefined
    }

    reverseServer = new WebSocketServer({
      host,
      port,
      verifyClient: ({ req }) => req.headers.authorization === `Bearer ${config.token}`,
    })
    reverseServerConfig = requestedConfig
    reverseServer.on('connection', (socket, req) => {
      const role = new URL(req.url || '/', 'ws://localhost').searchParams.get('role') || 'controller'
      reverseSockets.get(role)?.close()
      reverseSockets.set(role, socket)
      logger.info(`[BetterGI] BetterGI 已连接到反向 WebSocket 服务端 ${host}:${port}`)
      socket.on('message', raw => {
        let packet
        try { packet = JSON.parse(raw.toString()) } catch (error) {
          logger.warn(`[BetterGI] 忽略无效的反向 WebSocket 消息：${error.message}`)
          return
        }
        if (packet.type === 'notification' || packet.type === 'commandReply') {
          this.sendNotification(packet).catch(error => logger.error(`[BetterGI] QQ 通知发送失败：${error.stack || error}`))
        }
      })
      socket.on('close', () => {
        if (reverseSockets.get(role) === socket) reverseSockets.delete(role)
        logger.warn('[BetterGI] 反向 WebSocket 连接已断开')
      })
      socket.on('error', error => logger.warn(`[BetterGI] 反向 WebSocket 连接错误：${error.message}`))
    })
    reverseServer.on('listening', () => logger.info(`[BetterGI] 插件已加载，反向 WebSocket 服务端监听 ${host}:${port}`))
    reverseServer.on('error', error => logger.error(`[BetterGI] 反向 WebSocket 服务端启动失败：${error.message}`))
  }

  async control(e) {
    const message = parseCommand(e.msg)
    if (!message) return false
    let config
    try { config = loadConfig() } catch (error) { await e.reply(`BetterGI 配置读取失败：${error.message}`); return true }
    if (config.mode === 'server') {
      if (!config.token) {
        await e.reply('请先配置 WebSocket 监听令牌。')
        return true
      }
      let response
      try {
        logger.info(`[BetterGI] 正在通过反向 WebSocket 发送命令：${message}`)
        response = await this.requestReverse({ message, userOpenId: String(e.user_id || '') })
        logger.info(`[BetterGI] 收到 BetterGI 命令响应：${response.message ?? response.Message ?? '响应未包含 message 字段'}`)
      }
      catch (error) { await e.reply(`连接 BetterGI 失败：${error.message}`); return true }
      return this.replyCard(e, this.normalizeResponse(response))
    }
    const url = getWebSocketUrl(config)
    if (!url || !config.token) {
      await e.reply('请先配置 BetterGI 的目标 IP、端口和令牌。')
      return true
    }

    let response
    try {
      const payload = { message, userOpenId: String(e.user_id || '') }
      response = await this.request(url, config.token, payload)
    } catch (error) {
      await e.reply(`连接 BetterGI 失败：${error.message}`)
      return true
    }

    return this.replyCard(e, response)
  }

  async replyCard(e, response) {
    const logs = Array.isArray(response.logs) ? response.logs : [response.message]
    try {
      const image = await this.renderCard({
        message: response.message,
        logHtml: logs.map(line => `<div class="line">${escapeHtml(line)}</div>`).join(''),
        imageHtml: response.screenshotBase64
          ? `<img class="screenshot" src="data:image/jpeg;base64,${response.screenshotBase64}" />`
          : '<div class="empty">当前没有可用的游戏截图</div>',
        yunzaiVersion: yunzaiPackageInfo.version || '未知',
        pluginVersion: packageInfo.version,
      }, e.runtime)
      await e.reply(image)
    } catch (error) {
      logger.error(`[BetterGI] 图片渲染失败：${error.stack || error}`)
      await e.reply(`${response.message}\n（图片渲染失败，请检查 Yunzai Puppeteer 配置）`)
    }
    return true
  }

  normalizeResponse(response = {}) {
    return {
      ...response,
      message: response.message ?? response.Message ?? '',
      logs: response.logs ?? response.Logs,
      screenshotBase64: response.screenshotBase64 ?? response.ScreenshotBase64,
      betterGiVersion: response.betterGiVersion ?? response.BetterGiVersion,
    }
  }

  async sendNotification(packet) {
    const stamp = new Date(packet.timestamp || Date.now()).toLocaleString('zh-CN', { hour12: false })
    const image = await this.renderCard({
      message: packet.message || '',
      logHtml: `<div class="line">${escapeHtml(stamp)} [${escapeHtml(packet.result || '通知')}] ${escapeHtml(packet.eventName || '')}</div><div class="line">${escapeHtml(packet.message || '')}</div>`,
      imageHtml: packet.screenshotBase64
        ? `<img class="screenshot" src="data:image/jpeg;base64,${packet.screenshotBase64}" />`
        : '<div class="empty">当前没有可用的游戏截图</div>',
      yunzaiVersion: yunzaiPackageInfo.version || '未知',
      pluginVersion: packageInfo.version,
    })
    const targets = []
    if (packet.userOpenId) targets.push(() => Bot.sendFriendMsg('', String(packet.userOpenId), image))
    if (packet.groupOpenId) targets.push(() => Bot.sendGroupMsg('', String(packet.groupOpenId), image))
    if (targets.length === 0) throw new Error('没有配置 QQ 私聊或群聊 OpenID')
    const results = await Promise.allSettled(targets.map(send => send()))
    if (results.every(result => result.status === 'rejected'))
      throw new Error(results.map(result => result.reason?.message || String(result.reason)).join('；'))
  }

  renderCard(data, runtime) {
    if (runtime?.render) return runtime.render('BetterGI-Plugin', 'control', data, { retType: 'base64' })
    return puppeteer.screenshot('BetterGI-Plugin/control', {
      ...data,
      _plugin: 'BetterGI-Plugin',
      tplFile: path.join(pluginRoot, 'resources', 'control.html'),
      saveId: 'control',
    })
  }

  request(url, token, payload) {
    const runRequest = async () => {
      const ws = await this.ensureClientConnection(url, token)
      return new Promise((resolve, reject) => {
        const cleanup = () => {
          clearTimeout(timeout)
          ws.off('message', onMessage)
          ws.off('close', onClose)
          ws.off('error', onError)
        }
        const timeout = setTimeout(() => {
          cleanup()
          ws.close()
          reject(new Error('等待 BetterGI 响应超时'))
        }, 30000)
        const onMessage = raw => {
          cleanup()
          try { resolve(JSON.parse(raw.toString())) } catch (error) { reject(error) }
        }
        const onClose = () => { cleanup(); reject(new Error('BetterGI 连接已断开')) }
        const onError = error => { cleanup(); reject(error) }
        ws.once('message', onMessage)
        ws.once('close', onClose)
        ws.once('error', onError)
        ws.send(JSON.stringify(payload), error => {
          if (error) { cleanup(); reject(error) }
        })
      })
    }
    const request = clientQueue.then(runRequest, runRequest)
    clientQueue = request.catch(() => {})
    return request
  }

  ensureClientConnection(url, token) {
    if (clientSocket?.readyState === WebSocket.OPEN) return Promise.resolve(clientSocket)
    if (clientConnecting) return clientConnecting
    const ws = new WebSocket(url, { headers: { Authorization: `Bearer ${token}` } })
    ws.on('error', error => logger.warn(`[BetterGI] WebSocket 连接异常：${error.message}`))
    clientConnecting = new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        ws.terminate()
        reject(new Error('连接 BetterGI 超时'))
      }, 10000)
      ws.once('open', () => {
        clearTimeout(timeout)
        clientSocket = ws
        logger.info(`[BetterGI] WebSocket 已连接：${url}`)
        resolve(ws)
      })
      ws.once('error', error => {
        clearTimeout(timeout)
        reject(error)
      })
      ws.once('close', () => {
        clearTimeout(timeout)
        if (clientSocket === ws) clientSocket = undefined
        logger.info(`[BetterGI] WebSocket 已断开：${url}`)
        if (ws.readyState !== WebSocket.OPEN) reject(new Error('BetterGI 连接已断开'))
      })
    }).finally(() => { clientConnecting = undefined })
    return clientConnecting
  }

  requestReverse(payload) {
    const runRequest = async () => {
      // Worker 和主程序可能同时连接；远程任务控制优先发给 Worker。
      const workerSocket = reverseSockets.get('worker')
      const role = workerSocket?.readyState === WebSocket.OPEN ? 'worker' : 'controller'
      const socket = role === 'worker' ? workerSocket : reverseSockets.get('controller')
      if (!socket || socket.readyState !== WebSocket.OPEN) throw new Error('BetterGI Worker/Controller 尚未连接到 Yunzai 服务端')
      logger.info(`[BetterGI] 反向 WebSocket 已选择 ${role} 连接发送命令`)
      return new Promise((resolve, reject) => {
        const timeout = setTimeout(() => { cleanup(); reject(new Error('等待 BetterGI 响应超时')) }, 30000)
        const cleanup = () => {
          clearTimeout(timeout)
          socket.off('message', onMessage)
          socket.off('close', onClose)
          socket.off('error', onError)
        }
        const onMessage = raw => {
          try {
            const packet = JSON.parse(raw.toString())
            // 反向连接还承载通知帧；通知由 connection 级监听器处理，不能当作命令响应。
            if (packet.type === 'notification' || packet.type === 'commandReply') return
            cleanup()
            resolve(packet)
          } catch (error) {
            cleanup()
            reject(error)
          }
        }
        const onClose = () => { cleanup(); reject(new Error('BetterGI 连接已断开')) }
        const onError = error => { cleanup(); reject(error) }
        // 忽略通知帧时监听器必须继续保留，直到真正收到命令响应。
        socket.on('message', onMessage)
        socket.once('close', onClose)
        socket.once('error', onError)
        socket.send(JSON.stringify(payload), error => {
          if (error) { cleanup(); reject(error) }
          else logger.info(`[BetterGI] 命令帧已交给 ${role} WebSocket 连接发送`)
        })
      })
    }
    const request = reverseQueue.then(runRequest, runRequest)
    reverseQueue = request.catch(() => {})
    return request
  }

}
