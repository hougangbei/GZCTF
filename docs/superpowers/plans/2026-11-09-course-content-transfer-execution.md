# Course Content Transfer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development or executing-plans task by task.

**Goal:** 管理员可在技能树间拖动或添加引用，找到移出内容，并区分同名全局类别的来源和课程显示名。

**Architecture:** 在现有CourseAuthoringWorkspace JSON草稿上增加单事务操作，绑定两边版本和操作ID，保留题目身份及发布快照。位置索引即时读取有效草稿/当前发布快照，来源从导入映射派生；前端新增双栏整理页复用现有主题及路由。

**Tech Stack:** ASP.NET10、EF/PostgreSQL、React/Mantine、HTML drag/drop + 等价按钮、Vitest/Playwright。

## 合同

使用现有GET `/api/admin/skill-trees/{id}/workspace` 读取/初始化草稿。新增：

```ts
// POST /api/admin/course-transfers
type TransferCommand = {
 operationId:string; mode:'move'|'reference';
 sourceTreeId:string; sourceVersion:string; sourceCategoryId:string; sourceSkillId:string;
 targetTreeId:string; targetVersion:string; targetCategoryId:string; targetSkillId:string;
 contents:{kind:'challenge'|'lesson';contentId:string}[];
}
// response: {source:CourseWorkspace,target:CourseWorkspace,addedCount:number,removedCount:number,duplicateCount:number,noOp:boolean}
// GET /api/admin/course-resources?kind=category|skill|challenge|lesson&search=&unassociated=false
type ResourceRow = {
 id:string;kind:string;name:string;sourceLabels:string[];
 locations:{treeId:string;treeName:string;categoryId:string;categoryName:string;skillId?:string;skillName?:string;state:'draft'|'published'}[];
 unassociated:boolean;contentCount:number;
}
// additive sourceLabels/locations/unassociated to global category admin rows optional; frontend separate endpoint canjoinbyid
```

400无效归属/输入、404对象不可用、409任一版本变化/重试ID不同payload。移动同树同skill无操作，同树共享skill须所有出现位置一致。最多100项/操作，目标(skill,kind,id)去重；不接受客户端正文/Flag。审计不记录私密内容。

## Task 1 后端原子转移和资源位置

Files: 新 `Features/SkillTrees/Application/CourseTransferService.cs`、`CourseResourceQueryService.cs`，`Api/CourseTransfersController.cs`；DbContext/模型/迁移加入持久化幂等操作记录；DI、AuditActionCatalog；集成测试 `Tests/SkillTrees/CourseTransferTests.cs`。

- [x] 写失败测试：跨树移动/引用同ID、重复目标、同skill无操作、共享skill影响、任一版本冲突全部回滚、非法归属、权限、重试幂等/ID滥用。
- [x] 顺序锁两棵树及工作区版本，单事务写入两边JSON和operation journal，响应版本更新，不改全局资源和已发布快照。
- [x] 复用现有目录验证边界，排除退役/删除内容；目标内容数量/长度上限，保留服务端原题元信息，不接受注入。
- [x] 资源查询合并有效草稿+当前已发布（无workspace时读当前结构），区分路径显示名和全局库名，从导入映射/批次取来源；未关联须两种有效位置都无引用。
- [x] 集成验证移出并找回/学员成绩不变/快照隔离与升级迁移。

## Task 2 大小写归类与重复导入兼容

Files: `Features/Imports/Application/PlatformImportService.cs`、Contracts按需；对应导入测试。

- [x] 合成PWN13/Pwn3预检查展示casefold建议，不自动合并已有不同来源库资源。
- [x] 新来源按Trim+OrdinalIgnoreCase分组，显示名第一组确定值；原始来源类别保留元数据。
- [x] 已有来源映射优先兼容原case-sensitive键；重复导入不重建题目、移动旧引用或影响成绩。
- [x] 预检查明确提示新导入会把同包大小写差异归同组；不擅自跨包同名合并。

## Task 3 双栏整理前端

Files: 新 `pages/admin/content-transfer/Index.tsx`、`components/admin/course-transfer/*`、`hooks/useCourseTransfers.ts`、`utils/courseTransfer.ts`；修改CourseWorkspace入口、移出提示及课程显示名标签。

- [x] 先加浏览器/单元红灯，跨树选择、拖放目标技能、多选和按钮等价路径、去重/影响确认、版本冲突、重试、未保存提示。
- [x] 列表来源和目标用独立SWR key/状态，切树清空旧选中/确认，陈旧响应不能触发转移；仅信任本页选中引用，外部drag payload不作为正文。
- [x] 确认前显示来源/目标完整路径、共享skill引用数；固定确认payload与两版本，幂等ID未变payload重试复用。
- [x] 成功显示两树草稿链接，先发布目标再来源提醒，不自动发布/删除空节点；无需终端。
- [x] nativeHTML拖动加明确“移动到所选技能/添加引用”按钮和keyboard目标选择，移动端纵向布局。

## Task 4 来源、位置和找回

Files: `components/admin/skill-trees/*`的全局类别列表及选择器、`pages/admin/skills/Index.tsx`按需、题目/课节库管理组件；新共享ResourceLocations组件。

- [x] 全局类别同名显示来源标签/树名/课程显示名，ID保持不变；自建来源明确，不以名字猜测。
- [x] 题目/课节库新增未关联筛选和位置查看；从资源位置可跳至课程，移出后提示到题目/课节/类别库。
- [x] 课程编辑标签显示本课程名称，参考库名称可见；从库添加选择器来源明确，避免选错同名类别。
- [x] Pwn空目录不自动删除，已移出类别从库添加仍可恢复；资源位置不泄露学员信息或Flag。

## Task 5 验证、审查与交付

- [x] .NET单元和Docker集成顺序运行，Python工具回归，前端单元/check/build和相关新旧浏览器测试。
- [x] 实际隔离DB上旧数据迁移、同名分类导入→移出找回→移动/引用→分别发布→学员可见性及分数身份保留。
- [x] 规范审查通过后质量审查，修复并复审；更新文档/迁移回退约束和结果。
- [x] 提交本地分支，不自动发布/部署或改用户正式数据。

## 最终验证与范围

- 后端376单元、316 Docker集成通过，无跳过；其中新增转移/位置/导入归类测试和旧编排回归27项通过。旧数据库升级与HasPendingModelChanges=false已验证。
- 前端81单元、类型检查和生产构建通过；生产浏览器45项通过，1项需要真实学员凭据的既有旅程跳过。另在隔离真实后端完成管理员浏览器拖动/引用/两树发布，非模拟API。
- Python3.8部署工具101+13+4共118通过。
- 隔离真实数据库导入两份合成CTFd包，PWN/Pwn同包归一而跨来源独立；草稿移出及恢复保持当前发布位置。真实浏览器移动后确认两边草稿与原题ID，公开目录发布前不变，先目标后来源发布后入口切换；添加引用保留来源。
- 规范审查修复旧关系发布目录漏计、审计缺路径/ID/模式、重复类别提示和恢复入口；质量审查修复共享图片批量转移自锁，5秒有界回归证实修复，缺失资产400且事务回滚。已复审通过。
- 额外修复空白类别预检和仅有旧类别映射时恢复技能的排序；About旧测试固定调色值改为跟随平台正文token，无About业务改动。
- 数据库迁移增加操作日志与AuditEvents.DetailsJson。未打包、推送、发布或修改正式业务数据；没有调用Harbor/CTFd/校园代理。既有依赖告警仍保留，未验证正式校园网络、K3s/MinIO或生产负载。
