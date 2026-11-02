# 四层技能体系执行计划与验收记录

> 执行方式：当前隔离工作区内连续实施，使用 subagent-driven-development 进行分工、规格审查和代码审查。沿用当前模型，必要时选择 GPT-6.1 Sol / Terra；禁止使用 Astra。用户已授权开始，不再等待阶段间确认。

**目标：** 完成可复用的技能树 → 类别 → 技能 → 题目 / 课节，以及封面卡片、真实个人进度、后台编排、兼容迁移和主题感知动画。

**设计依据：** `docs/superpowers/specs/2026-11-02-skill-hierarchy-development.md`；基线 `03760026`。

**工作分支：** `codex/skill-hierarchy`。不接触线上数据库、比赛平台、Harbor、SSH 隧道或校园网配置。不自动发布新镜像。

## 1. 执行约束与接口约定

采用 EF Core / PostgreSQL、ASP.NET Core、React / Mantine / SWR / Vite。保留内容库和学习记录主键。旧 CategoryContents 只读兼容，新的编排写入 SkillContents。

新增模型：

```csharp
// 名称128、简介1024、图标32，RowVersion沿用项目的uint并发策略。
Skill { Guid Id; string Name; string Summary; string IconKey;
        bool IsEnabled; DateTimeOffset? DeletedAtUtc; uint RowVersion; }
CategorySkillRef { Guid Id; Guid CategoryId; Guid SkillId; int SortOrder; }
SkillContent { Guid Id; Guid SkillId; Guid? LessonId; Guid? ChallengeId; int SortOrder; }
```

公开目录新增 `category.skills`，每个技能为以下形状，原有 category.contents 在兼容期保留为去重后的扁平摘要：

```ts
type SkillSummary = {
  skillId: string; name: string; summary: string; iconKey: string;
  sortOrder: number; challengeCount: number; lessonCount: number;
  contents: SkillTreeContentSummaryResponse[];
}
type ProgressCounts = { totalCount: number; completedCount: number }
// GET /api/my-learning/skill-trees/{treeId}，只返回当前身份。
type TreeProgress = ProgressCounts & {
  completedLessonIds: string[]; solvedChallengeIds: string[];
  categories: (ProgressCounts & { categoryId: string })[];
  skills: (ProgressCounts & { skillId: string })[];
}
```

技能管理 API：`/api/admin/skills` GET/POST，`/{id}` GET/PUT/DELETE，`/{id}/contents` PUT，`/{id}/delete-impact` GET。类别技能排序：`PUT /api/admin/skill-categories/{id}/skills`。更新完整引用集合时携带 RowVersion；提交字段 `skills:[{skillId,sortOrder}]`、`contents:[{kind,contentId,sortOrder}]`。

后台技能详情：`skillId,name,summary,iconKey,isEnabled,rowVersion,categories,contents`；categories每项含categoryId/name。创建 / 更新使用相同元信息字段，创建不要求RowVersion。

发布位置提交扩展 `skillIds` 和 `inlineSkills:[{categoryId,name,summary,iconKey}]`；兼容保留CategoryIds/InlineCategories字段，但多技能类别的旧请求返回409 `skill_selection_required`。新增字段必须有默认值，不能导致旧序列化测试因构造函数变化无法编译。

前台路径：`/skill-trees` → `/:treeId` → `/:treeId/:categoryId/skills` → `/:treeId/:categoryId/skills/:skillId` → `.../:kind/:contentId`。现有文件路由的参数使用`id`表示treeId，组件统一转换，不能把两套参数名混用。

## 2. 任务清单

### T1 — 模型、约束与有数据迁移

- [x] 先在 `src/GZCTF.Integration.Test/Tests/SkillTrees/SkillHierarchyTests.cs` 添加迁移前有类别、课节、题目、完成记录的测试，验证迁移后按类别生成默认技能、排序与ID保持。
- [x] 修改 `Features/SkillTrees/Domain/SkillTreeModels.cs`、`Infrastructure/SkillTreeModelConfiguration.cs` 和 AppDbContext：增加Skill/CategorySkillRef/SkillContent及CoverHash。
- [x] 增加关系唯一索引、内容二选一约束、受限删除、软删除与RowVersion。
- [x] 新增EF迁移及同步ModelSnapshot；旧类别只创建一个迁移技能，跨路线引用继续共享；空类别不制造内容。
- [x] 升级测试查询SQL前后内容集合和历史记录；迁移不依赖线上执行或启动时重复回填。

测试断言要点：

```csharp
Assert.Equal(beforeContentIds, afterContentIds);
Assert.Equal(beforeCompletedCount, afterCompletedCount);
Assert.Single(categorySkills.Where(x => x.CategoryId == originalCategoryId));
```

### T2 — 技能服务、发布、权限与真实进度

- [x] 新增 `Application/SkillService.cs` 与 `Api/AdminSkillsController.cs` 并注册DI，完成创建/读取/更新/软删除/影响检查/有序内容保存。
- [x] 扩展SkillTreeContracts：公开SkillSummary、管理SkillResponse、CategorySkillOrderCommand和TreeProgress契约。
- [x] 调整SkillTreeQueryService、AdminSkillTreeService、SkillCategoryService：读取技能引用；旧扁平摘要只用于兼容，不写回旧表。
- [x] 调整ContentPublicationService、SkillTreeEnrollmentService及依赖旧CategoryContent的内容访问检查，以新引用为权威。不能出现旧表保留导致已撤销内容仍可访问。
- [x] 增加当前用户树进度接口；统计按 `(kind,id)` 去重。A=[X,Y]、B=[Y,Z]时树总数3，完成Y后树为1/3，两个技能都为1/2。
- [x] 测试匿名/普通用户/管理员边界、未发布/禁用/软删除内容、共享修改、409版本冲突与非法父子路径。
- [x] 维护类别合并、删除影响、旧发布兼容和缓存失效；不引入默认零分母100%或虚构学习人数。

### T3 — 封面与后台编排

- [x] 服务端封面上传：管理员权限，≤5MB，JPEG/PNG/WebP真实解码，生成缩略图后才替换CoverHash；重置解除引用，不删除共享资源。
- [x] 新增 `pages/admin/skills/Index.tsx` 与 `[id].tsx`：搜索、启用状态、元信息编辑、关联类别、混排题目/课节、删除影响和并发提示。
- [x] 调整 `pages/admin/skill-categories/[id].tsx`：关联/新建技能，引用排序、取消关联；提供拖拽及上移/下移。
- [x] 调整 `ContentPublishModal.tsx` 为路线/类别/技能选择；共享引用影响可见，支持内联创建技能。
- [x] 技能树管理页加入封面上传、默认封面、四层预览；管理导航新增技能库，不复制题目或学习记录。
- [x] 增加独立类型/Hook `utils/skillHierarchy.ts`、`hooks/useSkillHierarchy.ts`，避免多个执行者同时改生成Api.ts；生成API同步在后端稳定后统一处理。

### T4 — 三级卡片、目录与新旧路由

- [x] 先添加 `tests/e2e/skill-hierarchy.spec.ts`：点击路线→类别→技能→内容、浏览器返回/刷新、旧URL重定向、空状态与移动目录。
- [x] 创建 `components/skill-trees/SkillHierarchyBrowser.tsx` 及CSS：三步选择导航、4/2/1响应网格、16:9路线封面、技能统计和个人完成状态。
- [x] 创建技能内容页与公共目录组件，保留展开状态和当前引用上下文。新路由组件使用同一实现，避免复制分支。
- [x] 更新SkillTreeCard、SkillTreeOutline、技能树页面及SkillTreeContentWorkspace：路由验证每一层，旧内容URL解析到排序稳定的技能位置。
- [x] LessonReader接收技能上下文，上一项/下一项限制在当前技能；题目弹窗关闭回技能内容页，不触发容器启动。
- [x] 我的学习/完成动作后同时刷新树进度；公开目录不存储个人状态，匿名不发用户进度请求。

浏览器测试核心断言：

```ts
await page.getByRole('link', { name: /Web 基础/ }).click()
await page.getByRole('link', { name: /编码技能/ }).click()
await expect(page.getByRole('navigation', { name: '课程目录' })).toBeVisible()
await expect(page.getByText('1 / 3')).toBeVisible()
```

### T5 — 主题、动画与可访问性

- [x] 所有新增组件使用`--hbc-*`与Mantine主题，不覆盖用户自选色。第一版不加入作者、热度、学习人数。
- [x] 悬停3px/180ms；切层8px淡入220ms；卡片错峰24ms且最多120ms；目录200ms；进度240ms。
- [x] Reduced motion禁用位移/错峰；键盘焦点与悬停等价；手机目录关闭恢复焦点；长标题不溢出。
- [x] 浏览器覆盖320/390/768/1024/1440px、三种强调色来源、深/浅/系统、旧数据空封面、断网与快速切层。

### T6 — 集成验收、审查与交付

- [x] 运行完整后端单元测试，再顺序运行集成测试；不同dotnet测试工程不同时编译同一输出目录。
- [x] 前端unit、严格类型检查、生产构建和相关浏览器套件通过。将旧三层测试改为兼容/四层断言，不直接删测试规避失败。
- [x] 执行独立规格审查，再执行代码质量审查；有数据迁移和权限缺陷必须修复并回归。
- [x] 对真实本地HTTP/API编排流程验收：新增技能、关联共享内容、发布、读取、完成、统计、旧地址；截图检查桌面/手机和动画降级。服务端真实 HTTP 流程由集成测试验收；浏览器 UI 使用隔离接口数据，需真实账号的旧完整浏览器旅程未运行，见实测记录。
- [x] 更新本文件实测记录及部署恢复说明，提交实现。保留`.zcodeignore`、`FULL-QA-REPORT.md`等用户文件。
- [x] 本轮不直接部署生产，也不未经请求发布镜像；交付清晰说明代码、测试及部署状态。

## 3. 验证命令

工作目录为本工作区。SDK使用本机已有安装，数据库集成使用Docker Desktop socket。

```bash
/Users/bubble/.dotnet/dotnet test src/GZCTF.Test --no-restore --verbosity quiet
DOCKER_HOST=unix:///Users/bubble/.docker/run/docker.sock \
  /Users/bubble/.dotnet/dotnet test src/GZCTF.Integration.Test --no-restore --verbosity quiet
```

在 `src/GZCTF/ClientApp` 执行：

```bash
pnpm test:unit
pnpm check
pnpm build
E2E_BASE_URL=http://127.0.0.1:63017 pnpm exec playwright test \
  skill-hierarchy.spec.ts lesson-workspace.spec.ts lesson-editor.spec.ts \
  lesson-library.spec.ts lesson-theme.spec.ts training-workspace.spec.ts \
  theme-settings.spec.ts platform-branding.spec.ts
```

每个测试需记录退出码和通过数量。环境缺失必须记录实际原因，不能将未运行写成通过。预期的依赖安全告警与既有失败单独标记，不以此掩盖新增问题。

## 4. 执行记录

- 2026-11-02：用户批准设计并授权开始；执行计划已建立。下一项：T1模型与带数据升级测试。

## 5. 升级与恢复约束

本版包含数据库结构变更。部署前必须备份数据库、配置及文件目录，并记录当前镜像标识。升级后旧类别自动获得默认技能，原有题目、课节、内容排序及学习记录保留；空类别保持为空。管理员可在「技能库」复用技能，在类别编辑页调整技能关联，再在技能编辑页编排题目与课节。

本次代码完成不表示已部署；当前运行环境与校园入口无需修改。后续构建更新包时应将本分支与尚未部署的课节阅读/编辑改版一并打包。不要只复制网页文件绕过后端和迁移。

**不支持数据库自动降级。** 新技能存在多个类别引用后，无法安全还原旧三层结构，因此迁移 Down 会主动拒绝。恢复需停止新版本，恢复升级前数据库及相配的文件备份，再使用匹配的旧镜像；不能仅回退镜像、继续连接新数据库。正式更新应在本地验证完成后另行构建并提供实际版本的更新命令。


## 6. 实测验收记录（2026-11-02）

- 后端单元测试：286 通过；完整集成测试：203 通过。新增层级重点测试14项，含旧 PostgreSQL 数据库实际升级、原内容行/排序/学习记录保持、数据库约束、共享进度去重、父子路径权限、取消发布访问回收、封面尺寸及延迟版本冲突。
- 前端单元测试：33 通过；严格类型检查及生产构建通过。
- 浏览器主要回归：47 通过；新增延迟课节焦点、进度接口错误、类别干净表单刷新及 Flag 完成进度刷新共4项通过，另补充5种屏宽的全层级测试1项，总计52项；旧卡片回归另2项通过。浏览器共54项不同检查通过。
- 旧的完整真实管理员/学员旅程3项因未提供测试账号而跳过；未将跳过记为通过。服务端的真实HTTP/API操作、数据库升级和权限由集成测试覆盖，浏览器主题/布局/交互使用隔离接口数据。
- 独立规格审查通过；独立代码质量审查通过。审查发现的重新发布重排、损失性降级、内联技能类别版本不更新、类别干净表单快照错配、异步课节焦点丢失均已修复并回归。
- 桌面/手机截图检查已完成，深/浅/系统主题和个人/默认/管理员颜色、减弱动态效果均已验证。新默认封面为本地SVG，无外部素材依赖。
- 保留现有依赖安全告警；本次并非依赖安全升级，不声称不存在漏洞。未改线上、Harbor、校园入口、比赛平台或SSH通道。
- 本轮交付为本地分支代码和执行文档，尚未构建发布镜像；需要升级时与上版未部署的课节/主题改版一起打包。
