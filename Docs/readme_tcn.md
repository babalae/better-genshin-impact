<div align="center">
  <h1 align="center">
    <a href="https://bettergi.com/"><img src="https://img.alicdn.com/imgextra/i2/2042484851/O1CN014wn1rf1lhoFYjL0gA_!!2042484851.png" width="200"></a>
    <br/>
    <a href="https://bettergi.com/">BetterGI</a>
  </h1>
  <a href="https://trendshift.io/repositories/5269" target="_blank"><img src="https://trendshift.io/api/badge/repositories/5269" alt="babalae%2Fbetter-genshin-impact | Trendshift" style="width: 200px; height: 46px;" width="250" height="46"/></a>
</div>

<br/>

<div align="center">
  <a href="https://dotnet.microsoft.com/zh-cn/download/dotnet/latest/runtime"><img alt="Windows" src="https://img.shields.io/badge/platform-Windows-blue?logo=windowsxp&style=flat-square&color=1E9BFA" /></a>
  <a href="https://github.com/babalae/better-genshin-impact/releases"><img alt="下載數" src="https://img.shields.io/github/downloads/babalae/better-genshin-impact/total?logo=github&style=flat-square&color=1E9BFA"></a>
  <a href="https://github.com/babalae/better-genshin-impact/releases"><img alt="Release" src="https://img.shields.io/github/v/release/babalae/better-genshin-impact?logo=visualstudio&style=flat-square&color=1E9BFA"></a>
</div>

<br/>


<div align="center">
🌟 點一下右上角的 Star，Github 主頁就能收到軟件更新通知了哦~
</div>

<div align="center">
    <img src="https://img.alicdn.com/imgextra/i1/2042484851/O1CN01OL1E1v1lhoM7Wdmup_!!2042484851.gif" alt="Star" width="186" height="60">
  </a>
</div>

<br/>  

[English](./readme_en.md) | [中文](../README.md)| [繁体中文](./readme_tcn.md)

[![Discord](https://img.shields.io/badge/Discord-Join%20Chat-%237289DA?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/8xUfcw5nTS)



BetterGI · 更好的原神， 一個基於電腦視覺技術，意圖讓原神變得更佳的項目。

## 功能
* 實時任務
    * [自動拾取](https://bettergi.com/feats/timer/pick.html)：遇到可互動/拾取內容時自動按 <kbd>F</kbd>，支援黑白名單配置
    * [自動劇情](https://bettergi.com/feats/timer/skip.html)：快速點擊過劇情、自動選擇選項、自動提交物品、關閉彈出書頁等
        * 與凱瑟琳對話時有橙色選項會 [自動領取「每日委託」獎勵](https://bettergi.com/feats/timer/skip.html#%E8%87%AA%E5%8A%A8%E9%A2%86%E5%8F%96%E3%80%8E%E6%AF%8F%E6%97%A5%E5%A7%94%E6%89%98%E3%80%8F%E5%A5%96%E5%8A%B1)、[自動重新派遣](https://bettergi.com/feats/timer/skip.html#%E8%87%AA%E5%8A%A8%E9%87%8D%E6%96%B0%E6%B4%BE%E9%81%A3)
    * [自動邀約](https://bettergi.com/feats/timer/skip.html#%E8%87%AA%E5%8A%A8%E9%82%80%E7%B4%84)：自動劇情開啟的情況下此功能才會生效，自動選擇邀約選項
    * [快速傳送](https://bettergi.com/feats/timer/tp.html)：在地圖上點擊傳送點，或者點擊後出現的列表中存在傳送點，會自動點擊傳送點並傳送
    * [半自動釣魚](https://bettergi.com/feats/timer/fish.html)：AI 識別自動拋竿，魚上鉤時自動收杆，並自動完成釣魚進度
    * [自動烹飪](https://bettergi.com/feats/timer/cook.html)：自動在完美區域完成食物烹飪，暫不支援「仙跳牆」
* 獨立任務
    * [全自動七聖召喚](https://bettergi.com/feats/task/tcg.html)：幫助你輕鬆完成七聖召喚角色邀請、每週來客挑戰等 PVE 內容
    * [自動伐木](https://bettergi.com/feats/task/felling.html)：自動 <kbd>Z</kbd> 鍵使用「王樹瑞佑」，利用上下線可以刷新木材的原理，掛機刷滿一背包的木材
    * [自動秘境](https://bettergi.com/feats/task/domain.html)：全自動秘境掛機刷體力，自動循環進入秘境開啟鑰匙、戰鬥、走到古樹並領取獎勵
    * [自動音遊](https://bettergi.com/feats/task/music.html)：一鍵自動完成千音雅集的專輯，快速獲取成就
    * [全自動釣魚](https://bettergi.com/feats/task/fish.html)：在出現釣魚 <kbd>F</kbd> 按鈕的位置面向魚塘，然後啟動全自動釣魚，啟動後程式會自動完成釣魚，並切換白天和晚上
* 全自動
    * [一條龍](https://github.com/babalae/better-genshin-impact/issues/846)：一鍵完成日常（使用歷練點），並領取獎勵
    * [自動採集/挖礦/鋤地](https://bettergi.com/feats/autos/pathing.html)：透過左上角小地圖的識別，完成自動採集、挖礦、鋤地等功能
    * [鍵鼠錄製](https://bettergi.com/feats/autos/kmscript.html)：可以錄製回放當前的鍵鼠操作，建議配合調度器使用
* 操控輔助
    * [那維萊特轉圈](https://bettergi.com/feats/macro/other.html#%E9%82%A3%E7%BB%B4%E8%8E%B1%E7%89%B9-%E8%BD%AC%E5%9C%88%E5%9C%88)：設定快捷鍵後，長按可以不斷水平旋轉視角（當然你也可以用來轉草神）
    * [快速聖遺物強化](https://bettergi.com/feats/macro/other.html#%E5%9C%A3%E9%81%97%E7%89%A9%E4%B8%80%E9%94%AE%E5%BC%BA%E5%8C%96)：透過快速切換「詳情」、「強化」頁跳過聖遺物強化結果展示，快速+20
    * [商店一鍵購買](https://bettergi.com/feats/macro/other.html#%E4%B8%80%E9%94%AE%E8%B3%BC%E8%B2%B7)：可以快速以滿數量購買商店中的物品，適合快速清空活動兌換，塵歌壺商店兌換等
* [**……**](https://bettergi.com/doc.html)

<div align="center">
  <img src="https://github.com/babalae/better-genshin-impact/assets/15783049/57ab7c3c-709a-4cf3-8f64-1c78764c364c"/>
  <p>自帶一個遮罩視窗覆蓋在遊戲界面上，用於顯示日誌和圖像識別結果</p>
</div>

## 截圖

![0 39 1](https://github.com/user-attachments/assets/8fb0bfd9-e0db-4289-800f-1bc2efb221aa)


## 下載

> [!NOTE]
> 下載地址：[⚡Github 下載](https://github.com/babalae/better-genshin-impact/releases)
>
> 不知道下載哪個？第一次使用？請看：[快速上手](https://bettergi.com/quickstart.html) ， 遇到問題請先看：[常見問題](https://bettergi.com/faq.html)

最新編譯版本可以從自動構建中獲取： [![](https://github.com/babalae/better-genshin-impact/actions/workflows/publish.yml/badge.svg)](https://github.com/babalae/better-genshin-impact/actions/workflows/publish.yml)

## 使用方法
由於圖像識別比較吃性能，低配置電腦可能無法正常使用部分功能。

推薦的電腦配置至少能夠中畫質60幀流暢遊玩原神，否則部分功能的使用體驗會較差。

你的系統需要滿足以下條件：
* Windows 10 或更高版本的64位系統
* [.NET 8 運行時](https://dotnet.microsoft.com/zh-cn/download/dotnet/latest/runtime) （沒有的話，啟動程式，系統會提示下載安裝）

**⚠️注意：**
1. 視窗大小變化、切換遊戲解析度、切換顯示器的時候請重啟本軟件。
2. 不支援任何畫面濾鏡（HDR、N卡濾鏡等）。遊戲亮度請保持預設。
3. 當前只支援 `16:9` 的解析度，推薦在 `1920x1080` 視窗化遊戲下使用。
4. **模擬操作部分可能被部分安全軟件攔截，請加入白名單。已知360或者自訂規則WD會攔截部分類型的模擬點擊**

**打開軟件以後，在「啟動」頁選擇好截圖方式，點擊啟動按鈕就可以享受 BetterGI 帶來的便利了！**

詳細使用指南請看：[快速上手](https://bettergi.com/quickstart.html)

具體功能效果與使用方式見：[文件](https://bettergi.com/doc.html)

## 常見問題
* 為什麼需要管理員權限？
    * 因為遊戲是以管理員權限啟動的，軟件不以管理員權限啟動的話沒有權限模擬鼠標點擊。
* 會不會封號？
    * 理論上不會被封。 **BetterGI 不會做出任何修改遊戲文件、讀寫遊戲記憶體等任何危害遊戲本體的行為，單純依靠視覺算法和模擬操作實現。** 但是mhy是自由的，用戶條款上明確說明第三方軟件/模擬操作是封號理由之一。當前方案還是存在被檢測的可能。只能說請低調使用，請不要跳臉官方。
* [更多常見問題...](https://bettergi.com/faq.html)

## 致謝

本項目的完成離不開以下項目：
* [Yap](https://github.com/Alex-Beng/Yap)
* [genshin-woodmen](https://github.com/genshin-matrix/genshin-woodmen)
* [Fischless](https://github.com/genshin-matrix/Fischless)
* [MicaSetup](https://github.com/lemutec/MicaSetup)
* [cvAutoTrack](https://github.com/GengGode/cvAutoTrack)
* [genshin_impact_assistant](https://github.com/infstellar/genshin_impact_assistant)
* [HutaoFisher](https://github.com/myHuTao-qwq/HutaoFisher)
* [minimap](https://github.com/tignioj/minimap)
* [kachina-installer](https://github.com/YuehaiTeam/kachina-installer)

另外特別感謝 [@Lightczx](https://github.com/Lightczx) 和 [@emako](https://github.com/emako) 對本項目的指導與貢獻

## 開發者

格式化：[CodeMaid.config](CodeMaid.config)、[Settings.XamlStyler](Settings.XamlStyler)；<br>

[如何編譯項目？](BetterGenshinImpact/README.md)

## 許可證

![GPL-v3](https://www.gnu.org/graphics/gplv3-127x51.png)

## 問題反饋

提 [Issue](https://github.com/babalae/better-genshin-impact/issues) 或 discord
