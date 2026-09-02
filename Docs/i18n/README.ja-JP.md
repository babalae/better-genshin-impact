<div align="center">
  <h1 align="center">
    <a href="https://www.bettergi.com/"><img src="https://img.alicdn.com/imgextra/i2/2042484851/O1CN014wn1rf1lhoFYjL0gA_!!2042484851.png" width="200"></a>
    <br/>
    <a href="https://www.bettergi.com/">BetterGI</a>
  </h1>
  <a href="https://trendshift.io/repositories/5269" target="_blank"><img src="https://trendshift.io/api/badge/repositories/5269" alt="babalae%2Fbetter-genshin-impact | Trendshift" style="width: 200px; height: 46px;" width="250" height="46"/></a>
</div>

<br/>

<div align="center">
  <a href="https://dotnet.microsoft.com/ja-jp/download/dotnet/latest/runtime"><img alt="Windows" src="https://img.shields.io/badge/platform-Windows-blue?logo=windowsxp&style=flat-square&color=1E9BFA" /></a>
  <a href="https://github.com/babalae/better-genshin-impact/releases"><img alt="ダウンロード数" src="https://img.shields.io/github/downloads/babalae/better-genshin-impact/total?logo=github&style=flat-square&color=1E9BFA"></a>
  <a href="https://github.com/babalae/better-genshin-impact/releases"><img alt="Release" src="https://img.shields.io/github/v/release/babalae/better-genshin-impact?logo=visualstudio&style=flat-square&color=1E9BFA"></a>
</div>

<br/>

<div align="center">
🌟 このプロジェクトを気に入っていただけたら、右上の Star をお願いします。
</div>

<div align="center">
    <img src="https://img.alicdn.com/imgextra/i1/2042484851/O1CN01OL1E1v1lhoM7Wdmup_!!2042484851.gif" alt="Star" width="186" height="60">
  </a>
</div>

<br/>  

[English](./README.en-US.md) | [中文](./README.zh-CN.md) | [繁體中文](./README.zh-TW.md) | [日本語](./README.ja-JP.md)

[![Discord](https://img.shields.io/badge/Discord-Join%20Chat-%237289DA?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/8xUfcw5nTS)

BetterGIは、コンピュータービジョン技術を活用し、原神をより快適に遊べるようにするプロジェクトです。

## 機能

* **リアルタイムタスク**
  * **[自動拾い](https://www.bettergi.com/feats/timer/pick.html):** 近くにある調査ポイントやアイテムに反応し、<kbd>F</kbd>キーを自動で押します。拾う項目と拾わない項目を個別に設定できます。
  * **[ストーリー自動進行／スキップ](https://www.bettergi.com/feats/timer/skip.html):** 会話の早送り、選択肢の選択、アイテムの提出、ポップアップを閉じる操作などを自動化します。
    * キャサリンとの会話では、デイリー依頼報酬の受け取りと[探索派遣の再出発](https://www.bettergi.com/feats/timer/skip.html#%E8%87%AA%E5%8A%A8%E9%87%8D%E6%96%B0%E6%B4%BE%E9%81%A3)も自動化できます。
  * **[デートイベント自動進行](https://www.bettergi.com/feats/timer/skip.html#%E8%87%AA%E5%8A%A8%E9%82%80%E7%BA%A6):** ストーリー自動進行が有効なとき、デートイベントの選択肢を自動で選びます。
  * **[クイックテレポート](https://www.bettergi.com/feats/timer/tp.html):** マップ上のワープポイントを自動で選択し、テレポートします。
  * **[半自動釣り](https://www.bettergi.com/feats/timer/fish.html):** AIを使って、竿を投げる操作、魚が掛かったことの検出、釣り上げを補助します。

* **個別タスク**
  * **[七聖召喚の全自動プレイ](https://www.bettergi.com/feats/task/tcg.html):** キャラ招待やウィークリー挑戦などのPvEコンテンツを自動で進めます。
  * **[自動伐採](https://www.bettergi.com/feats/task/felling.html):** 「王樹の加護」（<kbd>Z</kbd>）を使い、再ログインを繰り返して木材を効率よく集めます。
  * **[秘境の自動周回](https://www.bettergi.com/feats/task/domain.html):** 秘境の開始から戦闘、報酬の受け取りまでを自動化します。
  * **[幽境の激戦の自動周回](https://www.bettergi.com/feats/task/stygian.html):** 幽境の激戦へ自動でテレポートし、主に難易度3を周回して聖遺物を集めます。
  * **[全自動釣り](https://www.bettergi.com/feats/task/fish.html):** 指定した釣り場で、昼夜の切り替えを含む一連の釣り操作を自動化します。
  * **[地脈の花芽の自動周回](https://www.bettergi.com/feats/task/leyline.html):** 対応しているほとんどの場所で、地脈の花芽を連続して周回できます。
  * **[幾千のメロディー自動演奏](https://www.bettergi.com/feats/task/music.html):** アルバムの演奏を自動化し、関連アチーブメントの獲得を支援します。
  * **[自動調理](https://www.bettergi.com/feats/task/cook.html):** 調理ゲージの成功エリアに合わせて自動で操作します。
  * **[聖遺物の自動分解](https://www.bettergi.com/feats/task/artifactSalvage.html):** クイック分解と、条件を指定した分解に対応しています。

* **自動化機能**
  * **[デイリータスクの一括実行](https://github.com/babalae/better-genshin-impact/issues/846):** デイリータスクを進め、報酬を受け取ります。
  * **[自動採取／採掘／フィールド討伐](https://www.bettergi.com/feats/autos/pathing.html):** ミニマップを認識しながら、指定した経路に沿って資源収集や討伐を自動化します。
  * **[キーマウス操作の録画](https://www.bettergi.com/feats/autos/kmscript.html):** キーボードとマウスの操作を記録して再生できます。スケジューラからの実行にも対応しています。

* **便利機能**
  * **[ヌヴィレットの高速回転](https://www.bettergi.com/feats/macro/other.html#%E9%82%A3%E7%BB%B4%E8%8E%B1%E7%89%B9-%E8%BD%AC%E5%9C%88%E5%9C%88):** キーを押している間、視点を水平方向に高速回転させます。ナヒーダでも使用できます。
  * **[聖遺物の高速強化](https://www.bettergi.com/feats/macro/other.html#%E5%9C%A3%E9%81%97%E7%89%A9%E4%B8%80%E9%94%AE%E5%BC%BA%E5%8C%96):** 「詳細」と「強化」の画面を切り替えて演出を省略します。
  * **[ショップでまとめ買い](https://www.bettergi.com/feats/macro/other.html#%E4%B8%80%E9%94%AE%E8%B4%AD%E4%B9%B0):** イベントや塵歌壺などのショップで、商品を一度に上限まで購入します。
* **[そのほかの機能](https://www.bettergi.com/doc.html)**

<div align="center">
  <img src="https://github.com/babalae/better-genshin-impact/assets/15783049/57ab7c3c-709a-4cf3-8f64-1c78764c364c"/>
  <p>ログと画像認識結果を表示するオーバーレイ付きです。</p>
</div>

## スクリーンショット

![0 39 1](https://github.com/user-attachments/assets/a65aafe9-d8d7-4ffb-8cdc-9939c2fb3bdf)

## ダウンロード

> [!NOTE]
> ダウンロード: [⚡GitHub Releases](https://github.com/babalae/better-genshin-impact/releases)
>
> 初めての方は [Quick Start](https://www.bettergi.com/quickstart.html) を、問題がある場合は [FAQ](https://www.bettergi.com/faq.html) をご覧ください。

最新ビルド: [![](https://github.com/babalae/better-genshin-impact/actions/workflows/publish.yml/badge.svg)](https://github.com/babalae/better-genshin-impact/actions/workflows/publish.yml)

## 使い方
**動作要件:**

- Windows 10／11（64ビット）
- [.NET 8 Runtime](https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0)（インストールされていない場合は自動で案内されます）

**⚠️ 注意:**

1. 画面サイズ、解像度、使用するモニターを変更した場合は、アプリを再起動してください。
2. 画面フィルター（HDR／NVIDIA Freestyle）は無効にし、ゲーム内の明るさはデフォルトのままにしてください。
3. 対応している画面比率は`16:9`のみです。`1920x1080`のウィンドウ表示を推奨します。
4. **セキュリティソフトがシミュレーション入力をブロックする場合があります。必要に応じて除外設定に追加してください。**

アプリを起動し、「開始」ページでキャプチャ方式を選んでから、「開始」を押してください。

詳細ガイド: [Quick Start](https://www.bettergi.com/quickstart.html)

詳細ドキュメント: [Documentation](https://www.bettergi.com/doc.html)

## FAQ

* **なぜ管理者権限が必要ですか？**
  * 原神が管理者権限で実行されるため、BetterGIにも同じ権限が必要です。
* **アカウント停止のリスクはありますか？**
  * **ゲームファイルやメモリは変更しません。** 視覚認識と入力シミュレーションのみを使います。ただし、miHoYo の利用規約では第三者ツールは制限されています。ご自身の判断でご利用ください。
* [FAQをもっと見る](https://www.bettergi.com/faq.html)

## クレジット
協力プロジェクト:

* [Yap](https://github.com/Alex-Beng/Yap)
* [genshin-woodmen](https://github.com/genshin-matrix/genshin-woodmen)
* [Fischless](https://github.com/genshin-matrix/Fischless)
* [MicaSetup](https://github.com/lemutec/MicaSetup)
* [cvAutoTrack](https://github.com/GengGode/cvAutoTrack)
* [genshin_impact_assistant](https://github.com/infstellar/genshin_impact_assistant)
* [HutaoFisher](https://github.com/myHuTao-qwq/HutaoFisher)
* [minimap](https://github.com/tignioj/minimap)
* [kachina-installer](https://github.com/YuehaiTeam/kachina-installer)

コア貢献者: [@Lightczx](https://github.com/Lightczx), [@emako](https://github.com/emako)

## 開発
コード整形: [CodeMaid.config](../../CodeMaid.config)、[Settings.XamlStyler](../../Settings.XamlStyler)

[ビルド手順](../../BetterGenshinImpact/README.md)

## ライセンス
GPLv3

## サポート
不具合の報告: [GitHub Issues](https://github.com/babalae/better-genshin-impact/issues)
