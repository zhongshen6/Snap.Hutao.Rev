## ✨ 一句话简介

**Snap.Hutao.Rev 是一个具有完整功能的胡桃启动器整合版本**：在保留胡桃主体功能体验的基础上，合并了注入能力与相关工具入口，面向日常使用与学习研究场景。

---

## 📌 项目声明（请先阅读）

本仓库 **Snap.Hutao.Rev** 是一个“合并项目”，并非全新独立开发的工具箱。

- 基础功能（除注入相关功能外）主要来自：
  - https://github.com/wangdage12/Snap.Hutao
- 注入功能主要来自：
  - https://github.com/sigewinnefish/sigewinne-toolkit

本项目是我基于上述两个项目进行功能合并后的学习性实践版本。

---

## ⚠️ 责任边界与风险说明

为避免误解，请务必知悉：

1. **本项目仅是合并产物**
   - 代码能力与后续可用性，本质上依赖两个上游项目是否持续维护与更新。

2. **与原作者无关联**
   - 本项目的合并行为仅出于学习与技术研究目的。
   - 本项目与两位原作者及其仓库 **没有合作、授权背书或运营关系**。

3. **问题处理边界**
   - 我只能尝试处理“因合并行为直接引入的问题”。
   - 对于原项目本身既有问题、上游逻辑缺陷、接口变更、兼容性变化等，
     我通常**无法保证修复**，也不承诺长期维护。

4. **使用者自行评估风险**
   - 请在充分理解来源、维护现状与潜在风险后再使用。

---

## 📖 项目简介

**中文**

胡桃工具箱是一款以 MIT 协议开源的原神工具箱，面向现代 Windows 平台，用于改善桌面端玩家体验。

本仓库版本目标：
- 尽量保留上游主体功能，减少不必要重写
- 合并并保留注入相关能力（来源见上文）
- 提供可直接使用的第三方扩展入口
- 提供云抽卡日志能力

官网：https://htserver.wdg.cloudns.ch/

Discord（可选）：https://discord.gg/ucH3mgeWpQ

---

## 🚀 安装说明

当前版本采用 MSI 安装方式，便于管理员权限场景与功能配置。

- 推荐使用 `.msi` 包安装。
- 若从旧版 `.msix` 迁移出现 `0x80073CF3`：
  1. 先备份旧版数据目录；
  2. 卸载旧版；
  3. 安装本版本；
  4. 将备份数据复制到新版本数据目录恢复数据。

若发布测试版，会在官网提供下载，也可在 Discord 获取通知并反馈问题。

---

## 🛠 开发与编译

- 解决方案入口（VS 2026 slnx）：
  - `src/Snap.Hutao/Snap.Hutao.slnx`
- 建议使用：
  - [Visual Studio 2026](https://visualstudio.microsoft.com/zh-hans/)
- 调试建议：
  - 选择 `unpackaged`（不打包）

参考开发文档（上游历史文档）：
- https://deepwiki.com/DGP-Studio/Snap.Hutao
- https://deepwiki.com/DGP-Studio/Snap.Hutao.Server

相关仓库：
- 元数据：https://github.com/wangdage12/Snap.Metadata
- 服务端：https://github.com/wangdage12/Snap.Server
- Web 管理后台与官网：https://github.com/wangdage12/Snap.Server.Web

---

## 📦 打包测试

项目使用 WiX 进行安装包构建。

- Visual Studio 需安装：**HeatWave for VS2022**（2026 兼容）
- 需要 MSI 安装包时：
  - 在解决方案中右键 `Snap.Hutao.Installer` 执行生成
  - 默认输出目录：
    - `Snap.Hutao.Installer/bin/x64/Release/en-US/Snap.Hutao.Installer.msi`

---

## 📄 许可与致谢

- 本仓库遵循 MIT License（详见 `LICENSE`）。
- 再次感谢两个上游项目作者的开源贡献。

> 若你需要稳定、可持续维护的功能体验，请优先关注并支持上游仓库，如果你喜欢这个项目,请给两个上游项目点一个star，非常感谢!
