# Historical Ranking Trend Implementation Plan

> **For agentic workers:** Use subagent-driven-development for independent implementation and review; no Astra.

**Goal:** 展开迁入 CTFd 的历史积分/解题趋势，保留统一总分。

**Architecture:** 只读时间线投影从冻结批次元数据和既有历史表恢复每日事件，排行榜聚合历史事件与新增原生通关，ECharts 提供跨年缩放。

**Tech Stack:** EF Core PostgreSQL / ASP.NET DataProtection / Apache ECharts / React / Playwright.

### Task 1: 冻结历史投影

- [x] 新建 `Features/Leaderboard/Application/ImportedScoreTimelineService.cs`：根据已过滤的 baseline ID/UserId/BatchId/SourceUserId 提取冻结题目分值与历史个人正确解题、奖励事件。每个用户/题目只取最早正确记录。
- [x] 新增合成集成测试：2021 解题、2024 解题及扣分、2026 新题，首尾时间真实、末端对齐、改当前分值不改曲线、重复提交去重、不可解密/不完整历史安全回退。
- [x] 注册 DI；更新 LearningLeaderboardService 调用。只查前十名，公开 DTO 只返回聚合日期/分数/解题数。响应添加 HasHistoricalTrend，默认 false 保持兼容。
- [x] 不改变基线、积分入账、排行榜次序或数据库结构。

### Task 2: 图表与预览

- [x] 先给 LeaderboardChart.test.ts 增加跨年份刻度、横向 dataZoom、阶梯线及负数测试，验证旧实现失败。
- [x] 增加时间缩放滑块，默认 0–100% 完整区间；跨年刻度带年份，主题和 reduced-motion 延续。说明历史按导出时分值回算，统一总分无拆分。
- [x] 本地 serve.py 只读预览按真实历史事件生成每日走势，校验每条曲线末端等于排名分数。刷新现有 63018 预览，不动线上。
- [x] 浏览器验证 2021–2026 时间范围、图例/指标切换、手机缩放不溢出和总分无拆分。

### Task 3: 验收

- [x] 后端单元/集成顺序运行；前端单元、类型/构建及相关浏览器回归。
- [x] 独立规格、质量审查后修复回审；记录结果，更新操作指南/旧设计说明。
- [x] 本地提交，展示运行中的预览及上游源码引用；不自动发布部署。


## 验收记录

- 上游源码核对：GZTimeWalker/GZCTF develop 的 `components/charts/ScoreTimeLine.tsx` 使用 Apache ECharts、`EchartsContainer`、step:end、时间轴、图例与 dataZoom；本项目复用现有 ECharts，无需引入第二套图表库。
- 后端单元356项通过，集成246项通过，最终11项排行测试复跑通过。真实格式合成 ZIP 测试含2021正确解题100、2024解题200扣分50、2026原生新题75，末端325与榜单一致，改当前题目分值不改旧走势。损坏加密元数据、缺题目快照、总分/数量不一致、缺时间/历史均安全回退；隐藏/封禁/其他用户不泄露到时间线。
- 前端35项单元、类型检查、生产构建通过；12项浏览器覆盖桌面/手机、累计积分/解题数、跨年历史、统一总分无拆分和自动刷新保留时间缩放。
- 真实私有 ZIP 的只读预览按原事件累计：积分前十名和解题前十名曲线都覆盖2021–2026，含2024记录；各曲线末端逐一对齐排名总分/解题数。预览不写数据库、不执行容器、不复制密码等认证字段。私有预览脚本和截图留在忽略目录，不提交。
- 独立规格/质量审查通过。审查发现15秒刷新会重置用户缩放范围，浏览器先复现失败后修复；新 `preserveDataZoom` 仅排行榜显式启用，其他图表不改变默认行为。
- 未新增数据库迁移，未调整固定旧分、新积分或通关规则。已有第三方依赖公告仍存在。图表历史分值为导出时分值回算，仅作趋势，不代表源站当年每次动态分数。
- 本轮本地提交与本地运行预览，不自动发布或部署。
