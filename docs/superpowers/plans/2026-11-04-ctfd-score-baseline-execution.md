# CTFd Score Baseline Implementation Plan

> **For agentic workers:** Use subagent-driven-development to implement and independently review each component. Do not use Astra.

**Goal:** 将 CTFd 旧成绩和通关作为一次性基线接入训练排行，之后只累计新增成绩。

**Architecture:** 导入事务写入来源级迁移标记、个人固定成绩基线和关联旧通关；排行榜聚合基线加未归账原生通关。复用既有身份映射、来源锁与导入预检。默认关闭，保持旧模式。

**Tech Stack:** .NET 10 / EF Core PostgreSQL / React Mantine / Playwright.

### Task 1: 导入基线与迁移

- [x] 新增 `PlatformScoreBaseline`、来源级 `PlatformScoreMigration` 和 `ChallengeProgress.ImportedScoreBaselineId`。精确字段契约由实现者先回报主代理；基线包括 UserId/Score/SolvedCount/Hidden/HasActivity/LastScoringAtUtc/CreatedAtUtc/SourceInstance/SourceUserId/BatchId。
- [x] 给导入选项增加 `bool MergeScores = false`，预检、计划指纹与 UI 都绑定此字段；只支持个人 CTFd 并要求账号/历史启用。
- [x] 先添加失败集成测试：原生分值100、动态题快照200、扣分-50 => 基线250，旧题停用仍250，重复迁移仍250；隐藏/封禁不公开。
- [x] 在独立 `PlatformScoreMigrationService.cs` 实现只读预检和事务写入，在现有 ImportAsync 中账号与历史写入后、提交前调用。来源变更、未绑定引用、并发唯一冲突都明确报错，绝不吞失败。
- [x] 生成 EF migration 与 snapshot，测试空库和现有历史数据升级。旧题进度保留最早真实时间、元数据和归账关联；同步每日投影，合并题目保留标记。

Run: `/Users/bubble/.dotnet/dotnet test src/GZCTF.Integration.Test --no-restore --filter FullyQualifiedName~ScoreBaseline`

### Task 2: 排行榜接续与前端

- [x] 更新 `LearningLeaderboardService.cs`，原生通关过滤 `ImportedScoreBaselineId == null`；旧基线独立于题目发布状态计分。隐藏/封禁用户排除，0活动账号不因迁入上榜。保持现有 solves/score 排序，时间取原事件时间，图表基线落在迁入日。
- [x] API 附加 `ImportedScore` 和 `EarnedScore`、`ImportedSolvedCount`、`EarnedSolvedCount` 可选默认字段；不影响原生成客户端。
- [x] 导入页勾选成绩迁入时自动保持账号/历史启用，变更选项废弃旧预检。显示旧成绩基线说明和风险；排行榜说明旧基线与新增积分。
- [x] 浏览器测试覆盖默认关闭、显式打开、选项改变重预检、旧 API 字段缺失兼容。

Run: `pnpm check` and `E2E_BASE_URL=http://127.0.0.1:63017 pnpm exec playwright test tests/e2e/platform-imports.spec.ts`

### Task 3: 验收与独立审查

- [x] 实际私有完整 ZIP 单独隔离导入，四个异常用户名用显式测试覆盖，不改原始包。独立计算每个用户 `sum(challenge.value per unique solve)+sum(award.value)` 与库中基线逐个对照；旧成绩不依赖 4 个缺少镜像的题。
- [x] 先规格审查，再代码质量审查；修复后回审。
- [x] .NET 单元与集成顺序运行，避免共享输出冲突；前端检查/构建、相关浏览器回归。清除私有临时测试源码，文档记录实际结果。
- [x] 更新中文操作指南明确选项、备份、重复迁移规则及排序/趋势界限。本轮只本地提交，不自动部署或发布。


## 验收记录

- 实际原始 CTFd ZIP 在独立隔离库通过 HTTP 预检、完整导入和同包重放。原始 `solves`、`challenges.value`、`awards.value` 单独解析作为对照，1320 个账号逐个总分一致，13132 个旧通关恢复，215 条扣分共 -17960 正确保留。106 个源隐藏账号保持隐藏，按隐藏/封禁/活动过滤有 507 个公开合格账号。未启动题目容器。
- 私有整包临时测试源码已删除，原 ZIP、账号密码、题目 Flag/附件和原始日志不提交。
- 7 项迁入集成测试覆盖固定快照、负分、旧通关最早时间、每日投影、重复/并发同来源只入账一次、变更备份/跳过集合拒绝、无副作用预检、历史模式兼容、旧数据升级、强制数据库失败回滚，以及合并题目后的旧映射阻断。
- 3 项排行集成测试覆盖固定旧分、未发布/停用/改分不影响旧基线、旧题再次正确作答不算首解、新题首次正确作答继续加分、隐藏/封禁/无活动账号、负分和 64 位总分。
- 最终集成回归 238 项通过，无跳过。真实 Docker 端口回归在一次复跑中出现未定位的偶发失败，单独复查及最终 238 项完整回归均通过；未以本功能名义修改端口分配实现。
- 后端单元回归 356 项通过；前端单元 34 项通过；前端类型检查/生产构建通过；导入、排行榜、四层技能与题目空间浏览器回归 35 项通过。
- 独立规格与质量审查通过。审查发现并修复负分曲线下界截断，以及先合并题目再延迟入账导致旧题可重复得分的边界；各有回归。
- 已有第三方依赖安全公告保留，本轮未更换依赖；同分顺序沿用训练平台规则，历史趋势以迁入日快照开始。正式成绩基线应使用最终确认的完整备份，不会实时同步源 CTFd 后续数据。
- 本轮仅本地提交，未发布镜像、推送远端或变更线上环境。
