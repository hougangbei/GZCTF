# Unified Course Authoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox syntax. User authorized execution in this session; do not pause between tasks for repeated approval. Do not use Astra.

**Goal:** 从技能树自上而下完成课程编排，所有改动留在草稿，统一发布；共享内容保持稳定身份与一份进度/成绩。

**Architecture:** 以独立课程工作区文档保存四层草稿，公开课程只读取不可变已发布快照。使用现有资源库实体作为稳定身份，工作区保存内容编辑版本而非覆盖当前活动实体。发布在事务中检查共享运行/计分冲突、激活内容并切换课程快照；运行中实例固定启动契约。旧后台入口与导入器不得绕过公开快照访问控制。

**Tech Stack:** ASP.NET Core / EF Core PostgreSQL、React / Mantine / SWR、现有课节 Markdown 编辑器、现有题目设置表单、Vitest / Playwright / xUnit。

## 代码边界与执行顺序

- 后台实施者：所有 C#、EF migration/snapshot、后端测试；不编辑 ClientApp。
- 主实施者：ClientApp 编排组件、hooks/types、内容编辑上下文、浏览器/单元测试、文档。
- 先约定接口再实现；.NET 编译/测试串行，避免共享输出冲突。工作区位于已有隔离 worktree，分支 `codex/course-authoring-workspace`。
- 保留之前未提交的排行榜与 chunk 恢复改动，保留用户 `.zcodeignore` 和 `FULL-QA-REPORT.md`。不操作生产、Harbor、旧 CTFd、校园入口与原有容器。

## Task 1：课程草稿与不可变快照

Files:
- Create `src/GZCTF/Features/CourseAuthoring/Domain/CourseModels.cs`
- Create `src/GZCTF/Features/CourseAuthoring/Application/CourseWorkspaceContracts.cs`
- Create `src/GZCTF/Features/CourseAuthoring/Application/CourseWorkspaceService.cs`
- Create `src/GZCTF/Features/CourseAuthoring/Infrastructure/CourseModelConfiguration.cs`
- Modify `src/GZCTF/Models/AppDbContext.cs`, `src/GZCTF/Migrations/*`
- Test `src/GZCTF.Integration.Test/Tests/SkillTrees/CourseWorkspaceTests.cs`

- [x] 写失败测试：A/B 两树复用技能/题目；在 A 修改树名、目录、技能名、课节正文后，A/B 的公开内容不变；发布 A 后仅 A 的快照改变。
- [x] 运行筛选测试确认失败：`dotnet test src/GZCTF.Integration.Test --filter FullyQualifiedName~CourseWorkspaceTests`。
- [x] 增加有并发令牌的工作区 JSON 文档及不可变发布快照。文档只对管理员可读，含私有 Flag 的内容版本不混进公开 DTO。
- [x] 定义版本化响应和写入：
```ts
type Workspace = {
  skillTreeId: string
  version: string
  document: CourseDocument
  hasPublishedVersion: boolean
}
type CourseDocument = {
  name: string; summary: string; iconKey: string; coverUrl?: string | null
  categories: CourseCategory[]
}
type CourseCategory = {
  categoryId: string; name: string; summary: string; iconKey: string
  skills: CourseSkill[]
}
type CourseSkill = {
  skillId: string; name: string; summary: string; iconKey: string; isEnabled: boolean
  contents: CourseContent[]
}
type CourseContent = {
  contentId: string; kind: 'lesson' | 'challenge'; title: string; summary: string
  state: string
}
// GET /api/admin/skill-trees/{id}/workspace -> Workspace
// PUT same path, { version, document } -> Workspace
```
- [x] 验证上限、图标、每层唯一身份、父子引用、保存版本与事务。空课程与空技能作为提醒，不伪造可学内容。
- [x] 加迁移与一次性已有发布快照固化：保持 ID、既有公开可见范围与账号/得分；新建安装、带旧数据升级都测试。不得在每次公开读取时懒回填。

## Task 2：公开读取、兼容写入与学习权限

Files:
- Create `src/GZCTF/Features/CourseAuthoring/Application/CoursePublishedReader.cs`
- Modify `src/GZCTF/Features/SkillTrees/Application/SkillTreeQueryService.cs`
- Modify `src/GZCTF/Features/SkillTrees/Application/AdminSkillTreeService.cs`
- Modify `src/GZCTF/Features/SkillTrees/Application/SkillTreeEnrollmentService.cs`
- Modify `src/GZCTF/Features/LearningPaths/Application/EnrollmentService.cs`
- Modify challenge/lesson public access services and publication compatibility paths

- [x] 先测试：猜测草稿内容 ID、旧课程 URL、跨树父级不匹配均不能读取草稿；旧学员可正常访问旧快照。
- [x] 所有公开目录、标题、课节正文、题目说明、附件解析使用已发布快照。稳定 ID 用于个人进度与去重。
- [x] 旧管理页面保存写资源库草稿或工作区，不原地修改公开快照；旧发布端点经相同激活校验，不暗中解除其他课程引用。
- [x] 对明确停用/撤销等管理操作保留既有语义，检查对计分的影响；不以普通草稿编辑触发撤销。
- [x] 测试旧引用、旧导入器、旧技能树 publish/draft API 的兼容；不靠大规模删除测试掩盖变更。

## Task 3：内容版本、统一发布与实例契约

Files:
- Create `src/GZCTF/Features/CourseAuthoring/Api/CourseWorkspaceController.cs`
- Create `src/GZCTF/Features/CourseAuthoring/Application/CoursePublicationService.cs`
- Modify lesson/challenge admin APIs and services for optional course context
- Modify challenge runtime models/service, scoring projections and caches where required
- Test `src/GZCTF.Integration.Test/Tests/SkillTrees/CoursePublicationTests.cs`

- [x] 对内容上下文接口约定：工作区内内容 GET/PUT 使用与现有独立编辑器相同的 request/response DTO；返回后继续停留原树、类别、技能。
- [x] 写失败测试：草稿 Flag/镜像/分值修改不影响正在使用的题目；运行中实例保持旧版本；重新提交仍只计一份通关分数。
- [x] 发布检查接口：`POST /api/admin/skill-trees/{id}/workspace/check`，body `{ version }`，返回 `{ version, issues, categoryCount, skillCount, challengeCount, lessonCount }`；issue 为 `{ severity:'error'|'warning', code, message, categoryId?, skillId?, contentId? }`。
- [x] 发布接口：`POST /api/admin/skill-trees/{id}/workspace/publish`，body `{ version }`。检查与发布之间版本变化必须 409；发布再次在事务内验证，不能信任客户端检查结果。
- [x] 必需附件不可用、题目配置无效、共享运行/计分配置冲突为阻塞；空目录、停用内容等为提醒。配置可解析不冒充题目机已启动验证。
- [x] 在事务内激活内容、更新稳定引用投影并切换活动快照；提交后清缓存不改变版本真实性。共享课程互相冲突的运行规则必须明确拒绝并给出影响树，不能静默覆盖。
- [x] 实例绑定启动配置；发布新版本不得强杀旧实例，Flag 验证解析实例版本。旧迁入实例保留旧规则兼容。
- [x] 验证失败回滚、重复请求、并发管理员、公开计分资格、导入基线及原生得分没有意外变化。

## Task 4：编排工作区与上下文创建

Files:
- Create `ClientApp/src/utils/courseAuthoring.ts`
- Create `ClientApp/src/hooks/useCourseAuthoring.ts`
- Create `ClientApp/src/components/admin/course-authoring/*`
- Modify `ClientApp/src/pages/admin/skill-trees/[id].tsx`
- Modify new-tree destination if necessary
- Test `ClientApp/tests/e2e/course-authoring.spec.ts`

- [x] 先写浏览器失败场景：空树→新类别→新技能→新课节/题目，不离开课程上下文；从库添加不复制原 ID，移出不删资源。
- [x] 左目录右编辑，顶部保存状态/预览/发布检查；移动端目录抽屉；主题颜色使用现有 Mantine / hbc 变量。
- [x] 所有新建动作只需先填名称，资源创建成功自动添加当前位置并选中；创建成功关联保存失败时保留 ID 并重试，不能再次建同名副本。
- [x] 保存全工作区文档使用版本令牌；失败保留本地改动，409 不自动覆盖；保存期间冻结对应输入，切节点不能丢本地编辑；beforeunload 提醒。
- [x] 目录排序支持拖拽和键盘按钮；每层“新建/从库添加/移出”清楚区分。相同技能/类别多处引用的影响在侧栏可见。
- [x] 预览用当前工作区数据，展示管理员标识与实际可见数量；不能跳转公开页假装预览。

## Task 5：完整内容编辑器接入

Files:
- Modify `ClientApp/src/pages/admin/library/lessons/[id].tsx`
- Modify `ClientApp/src/pages/admin/library/challenges/[id].tsx`
- Modify `ClientApp/src/hooks/useChallengeLibraryAdmin.ts`
- Add focused context helpers/tests in `courseAuthoring.ts`

- [x] `?course={treeId}&category={categoryId}&skill={skillId}` 传父级上下文；上下文必须由后台校验，不信任 query 直接拼接任意跳转。
- [x] 保存写工作区内容版本；课程中编辑不显示重复选择路径的独立发布弹窗。主动作“保存并返回课程”，次动作“保存草稿”。
- [x] 返回 `/admin/skill-trees/{treeId}?category={categoryId}&skill={skillId}` 并恢复选中节点。
- [x] 保留现有 Markdown、图片上传、本地草稿、课节 locale；题目所有容器/Flag/附件高级字段继续可编辑。
- [x] 测试独立资源库编辑不受 context 分支破坏，恶意/缺失 context 报明确错误且不掉回全局写入。

## Task 6：发布检查与资源库兼容

Files:
- Create `ClientApp/src/components/admin/course-authoring/PublishReview.tsx`
- Modify tree cover editor/context, category/skill library explanatory states as required
- Test `ClientApp/tests/e2e/course-authoring.spec.ts`

- [x] 检查结果按阻塞/提醒分组，点问题定位节点，显示可学的去重数量。
- [x] 已检查后再编辑使检查失效；发布后重载新工作区，不保留旧令牌。
- [x] 共享运行规则冲突显示受影响课程与可行操作，不提供会误覆盖其他课程的“强制”按钮。
- [x] 草稿封面上传返回工作区版本，不立即换掉公开封面；不可用素材拒绝发布。
- [x] 显式传达“资源身份复用，完成记录共用；当前编辑保存不是公开发布”。

## Task 7：独立审查、迁移与全量验证

- [x] 后台/前端分别先规范符合性审查，再质量审查；修复后复核。审查使用 GPT-6.1 系列，不用 Astra。
- [x] `dotnet test src/GZCTF.Test --no-restore --verbosity quiet`，随后 Docker 本地模式 `dotnet test src/GZCTF.Integration.Test --no-restore --verbosity quiet`，串行执行。
- [x] `pnpm test:unit`、`pnpm check`、`pnpm build`；需要明确错误与既有 warning，不声称零漏洞。
- [x] 完整 Chromium 套件使用独立真实后端与测试数据库，保留真实管理员/学员建课路径、旧路由、进度私有、排行榜、导入、WP、主题和移动布局。
- [x] 已有发布→新草稿→预览→统一发布→两课程共享→运行中实例延续的真实 HTTP 验证；迁移前后比对公开目录与账号得分。
- [x] 更新中文操作文档、计划勾选与验证记录；本地提交。未授权本轮发布 GitHub 或部署正式平台，不自动操作远端。


## 实施记录与代码位置

实现沿用现有 `Features/SkillTrees` 边界，而非另建重复 CourseAuthoring 模块。控制器、文档/版本实体和服务分别位于：

- `Features/SkillTrees/Api/CourseAuthoringController.cs`、`CourseWorkspaceNodesController.cs`、`CourseWorkspaceAssetsController.cs`、`CourseWorkspaceCoverController.cs`。
- `Features/SkillTrees/Application/CourseSnapshotBuilder.cs`、`CourseWorkspaceContentService.cs`、`CoursePublicationService.cs`、`CourseAssetRegistry.cs`。
- `Features/SkillTrees/Domain/CourseAuthoringWorkspace.cs`，迁移 `20261107010000` 至 `20261107070000`。
- 公开运行内容读取统一到 `Features/ChallengeRuntime/Application/PublishedCourseContentReader.cs` 与 `CourseResourceUrls.cs`；实例持有启动快照。
- 新前端位于 `components/admin/course-authoring/`，默认树编辑页进入新工作区。旧树编辑器移入 `LegacySkillTreeEditor.tsx`，通过显式兼容参数保留，但已有新工作区时旧写入被拒绝。
- 新建身份最终采用幂等 `POST /workspace/nodes`，持久绑定 operationId 与请求，不再用四个普通 POST 猜测失败结果；关联仍是版本化工作区 PUT。
- 私有文件新增独立库 `POST /api/admin/content-assets?kind=...&contentId=...`，课程内上传走 `/workspace/assets`；已发布资源访问验证活动快照引用。历史公共文件保持原兼容路径。
- 新工作区测试在 `CourseAuthoringWorkspaceTests.cs`、`PublishedRuntimeSnapshotTests.cs`、`course-authoring.spec.ts` 与 `course-authoring-live.spec.ts`。

模型：后台版本与前端质量审查使用 GPT-6.1 Terra，运行/计分与前端规范审查使用 GPT-6.1 Sol；未使用 Astra。规范和质量审查分别执行，发现问题修复后复核。

验证结果和限制见 `docs/qa/2026-11-07-course-authoring-verification.md`。本地实现不代表已发布 GitHub 或部署内网。
