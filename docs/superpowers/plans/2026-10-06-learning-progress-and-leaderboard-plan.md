# 个人进度、题目积分与排行榜实施计划

> **For agentic workers:** Execute tasks in order, keeping unrelated files untouched. Do not add or run test suites unless the user asks; verify with builds and code inspection.

**Goal:** 个人页同步所有已发布技能树，题目支持固定分值和四档中文难度，并提供可切换解题数与积分的全站排行榜。

**Architecture:** `SkillTreeEnrollmentService` 合并公开树与已删除的加入历史。题目 `Challenge.Score` 持久化固定分值，现有首次解题记录提供排行榜时间轴；新的公开排行榜服务只统计当前有效题目。前端使用独立路由与同一指标开关驱动曲线和表格。

**Tech Stack:** ASP.NET Core、EF Core/PostgreSQL、React、Mantine、ECharts、SWR。

---

### Task 1: 个人学习记录

**Files:** `src/GZCTF/Features/SkillTrees/Application/SkillTreeEnrollmentService.cs`、`src/GZCTF/ClientApp/src/components/skill-trees/MySkillTreeRecord.tsx`、`src/GZCTF/ClientApp/src/locales/{zh-CN,en-US}/skillTrees.json`

- [ ] 查询当前发布且未删除的技能树，并与用户已加入的已删除树按 ID 去重；对每棵树调用现有发布版本解析与进度汇总，未加入的树 `IsCurrent=false`。
- [ ] 个人页保留历史标记，显示中文或英文进度标签和明确空状态。
- [ ] 检查公开列表与个人页使用相同的内容发布及启用过滤条件。

### Task 2: 固定分值和难度

**Files:** `src/GZCTF/Features/ChallengeLibrary/Domain/ChallengeModels.cs`、`src/GZCTF/Features/ChallengeLibrary/Application/ChallengeLibraryService.cs`、`src/GZCTF/Features/LearningPaths/Infrastructure/LearningModelConfiguration.cs`、`src/GZCTF/Migrations/`、`src/GZCTF/ClientApp/src/{Api.ts,utils/challengeEditor.ts,components/admin/library/ChallengeBasicsForm.tsx,components/skill-trees/SkillTreeOutline.tsx}`、相关中英文资源。

- [ ] 给新题目默认分值 `100`，迁移旧行到 `100`，限制管理员输入为 `0..10000` 整数；编辑和列表响应包含分值。
- [ ] 管理员新建题目只提供 `Baby/Easy/Normal/Hard` 四个底层值，对应中文“入门/简单/中等/困难”；旧值在编辑时保留直到明确更改。
- [ ] 技能树公开题目卡片把难度枚举翻译为用户语言。

### Task 3: 全站排行榜

**Files:** `src/GZCTF/Features/Leaderboard/{Application,Api}/`、`src/GZCTF/Extensions/Startup/ServicesExtension.cs`、`src/GZCTF/ClientApp/src/pages/leaderboard/Index.tsx`、`src/GZCTF/ClientApp/src/components/AppNavbar.tsx`、相关中英文资源。

- [ ] 公开 GET 接口查询当前发布且启用、可从已发布技能树到达的题目 ID；一次读取首次解题记录和当前分值，按用户及北京时间日期汇总，每题每人只计一次。
- [ ] 返回排名前五十的用户名、解题数、积分与前十名的每日累计曲线；按选定指标、另一指标、最后有效解题时间及用户名稳定排序。结果短时缓存，写入后及时失效或短时过期。
- [ ] 新增 `/leaderboard` 页面和导航；指标切换同时改变曲线纵轴与表格排序，并处理加载、空数据和接口失败。
- [ ] 核对公开响应不含邮箱、姓名、学号、提交或用户 ID。

### Task 4: 构建与交付

- [ ] 使用已有本地依赖编译后端和前端；检查迁移与模型快照一致、编译无错误。
- [ ] 只提交上述相关文件，保留工作区内原有的 `.gitignore`、`.zcodeignore` 改动。
- [ ] 推送到此前指定的 GitHub 仓库；上线需先按已有更新流程备份数据库和文件，再由部署步骤切换镜像。
