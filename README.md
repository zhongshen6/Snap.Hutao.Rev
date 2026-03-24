
## 📖 简介 / Introduction

**中文**  
胡桃工具箱是一款以 MIT 协议开源的原神工具箱，专为现代化 Windows 平台设计，旨在改善桌面端玩家的游戏体验。

自带的注入功能只有FPS调整，只保证FPS调整长期可用，你可以使用`注入选项`下方的第三方工具来使用更多功能，本项目提供的所有注入功能都不会影响游戏的公平性。

官网：https://htserver.wdg12.work/

**该版本的特点：**  
- 尽量保留原版功能，少重写功能，稳定性强
- 只集成没有争议的安全的注入功能
- 大部分注入功能以第三方工具形式提供，点击即用
- 永久免费的云抽卡日志

有条件的话可以加入discord服务器：https://discord.gg/ucH3mgeWpQ

**English**  
Snap Hutao is an open-source Genshin Impact toolkit under MIT license, designed for modern Windows platform to improve the gaming experience for desktop players.

---

## 🚀 安装 / Installation

目前 Sanp.Hutao.Rev 更新了打包方式，并采用了标准现代的 msi 安装，方便程序获取管理员权限和更多的功能设置，不再需要原 Depolyment

只有`.msi`安装包安装的可以和之前的版本共存，如果通过`.msix`安装包安装则可能出现`0x80073CF3`，备份旧版本数据文件夹后卸载旧版本即可继续安装，将旧版本数据文件夹里面的文件复制到该版本的数据文件夹中即可恢复数据

有时候我们在对某些功能有重大更改时发布测试版，可在官网的下载，可加入discord服务器报告功能使用情况和获取测试通知

---

## 开发
项目启动位置已升级为 VS2026 的 slnx 格式 Snap.Hutao\src\Snap.Hutao\Snap.Hutao.slnx
> [!WARNING]
> 要使该项目可以长期运行，我们需要以下资源
> 1. 元数据的编写
> 2. 图片资源

已同步原作者的元数据

**目前元数据的编写进度：**

| 项目（V6.4） | 是否完成     |
| ----------- | ----------- |
| 总体数据 | ✔️ |

✔️：已完成  
❌：未编写  
❇️：编写中  
❔：数据暂时无法得到  
 / ：似乎不需要变动  
💠：低优先级，以后编写  

**若需编译项目，请使用[Visual Studio 2026](https://visualstudio.microsoft.com/zh-hans/)**  
调试选项请选择unpackaged（不打包）
**原开发文档现在还可使用（其中的AI功能很好用），以下是开发文档链接：**  

https://deepwiki.com/DGP-Studio/Snap.Hutao

https://deepwiki.com/DGP-Studio/Snap.Hutao.Server

**该项目所需的其他仓库，欢迎贡献或者自部署**

- 元数据：[Snap.Metadata](https://github.com/wangdage12/Snap.Metadata)
- 服务端：[Snap.Server](https://github.com/wangdage12/Snap.Server)
- Web管理后台和官网：[Snap.Server.Web](https://github.com/wangdage12/Snap.Server.Web)

**第三方工具**

如果你想要添加你自己开发的工具到第三方工具列表中，请确保：
1. 工具应该提供源码或者开源，并且可以成功编译
2. 工具不应提供任何可能影响游戏公平性的功能

工具不限于注入功能，若满足以上条件，请提 issue，或者在 discord 服务器中联系管理员

## 打包测试

由于采用了 wix 进行打包程序，VS 需要安装 **HeatWave for VS2022**（2026兼容）。需要 msi 安装包时，右键选中 Snap.Hutao.Installer 生成后即可在目标目录找到。默认目录：Snap.Hutao.Installer\bin\x64\Release\en-US\Snap.Hutao.Installer.msi

## 资源和服务器状态


<a href="https://uptimerobot.com" target="_blank" rel="noopener">
<picture>
  <source media="(prefers-color-scheme: dark)"
          srcset="https://raw.githubusercontent.com/wangdage12/wangdage12/main/assets/uptimerobot-logo.svg">
  <img alt="logo"
       src="https://raw.githubusercontent.com/wangdage12/wangdage12/main/assets/uptimerobot-logo-dark.svg" width="300">
</picture>
</a>

我们将使用[UptimeRobot](https://uptimerobot.com)赞助的监控服务作为新的服务器状态页面，它有更多的功能

[新服务器状态页面](https://stats.uptimerobot.com/fHxWxdxK61)  

[旧服务器状态页面](http://serverjp.wdg.cloudns.ch:3001/status/hts)

---

**元数据仓库：**  
https://github.com/wangdage12/Snap.Metadata

仓库镜像：  
![http://serverjp.wdg.cloudns.ch:3001/api/badge/11/status?style=flat-square](http://serverjp.wdg.cloudns.ch:3001/api/badge/11/status?style=flat-square)

http://htgit.wdg.cloudns.ch/wdg1122/Snap.Metadata

---

**API：**  

![http://serverjp.wdg.cloudns.ch:3001/api/badge/10/status?style=flat-square](http://serverjp.wdg.cloudns.ch:3001/api/badge/10/status?style=flat-square)

https://htserver.wdg12.work/api/

---

**图片资源站：**  

https://htserver.wdg12.work/
