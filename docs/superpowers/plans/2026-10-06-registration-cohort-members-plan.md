# 注册选择年级与成员可见性实施计划

> **For agentic workers:** Execute tasks in order. Do not add or run test suites unless the user asks; verify implementation with builds and focused inspection.

**Goal:** 注册时选择启用年级，注册用户立即归入该年级；管理员能查看所有普通成员与未分配旧账号，并识别验证和审核状态。

**Architecture:** 公共年级目录只公开 ID 与名称。注册 API 校验年级后写入现有 `UserInfo.CohortId`。管理员成员接口按角色、搜索和分页读取用户，并由成员面板展示全部及年级视图。无需数据库迁移。

**Tech Stack:** ASP.NET Core、EF Core/PostgreSQL、React、Mantine、SWR、现有多语言资源。

---

### Task 1: 年级目录与注册后端

**Files:** `src/GZCTF/Features/Dashboard/Api/ActiveCohortsController.cs`、`src/GZCTF/Models/Request/Account/RegisterModel.cs`、`src/GZCTF/Controllers/AccountController.cs`。

- [ ] 新建公开只读 GET 接口，查询 `Cohorts` 中 `IsActive` 的 ID 和名称，按名称排序。
- [ ] 注册 DTO 增加 `Guid? CohortId`；注册方法在创建或复用待验证账号前确认 ID 非空且指向启用年级，失败返回 400。
- [ ] 新账号创建时设置 `UserInfo.CohortId`；验证邮件重发的待审核账号在密码校验成功后更新所选年级，已批准账号的重复提交保持拒绝。

### Task 2: 注册页面

**Files:** `src/GZCTF/ClientApp/src/pages/account/Register.tsx`、`src/GZCTF/ClientApp/src/Api.ts`、`src/GZCTF/ClientApp/src/locales/{zh-CN,en-US}/account.json`。

- [ ] 注册页加载公开启用年级列表，显示必选 `Select`；加载失败或列表为空时给出错误/空状态并禁用提交。
- [ ] 在注册请求中发送选择的 `cohortId`；选择值随表单禁用状态正确切换。
- [ ] 增加中英文年级标签、占位文本和错误提示。

### Task 3: 管理员成员数据与页面

**Files:** `src/GZCTF/Features/Dashboard/Api/AdminCohortsController.cs`、`src/GZCTF/ClientApp/src/components/admin/workspace/MembersPanel.tsx`、`src/GZCTF/ClientApp/src/locales/{zh-CN,en-US}/skillTrees.json`。

- [ ] 新增管理员分页接口查询 `Role.User`，支持搜索和未分配筛选，响应包含总数、用户名、真实姓名、年级、邮箱确认和审核状态；对分页参数设上限。
- [ ] 扩展现有年级成员响应，包含邮箱与审核状态；手动分配动作继续使用现有 API。
- [ ] 成员面板增加全部成员列表、未分配筛选、搜索和分页；年级列表为待验证/待审核账号显示状态标记，保留原分配操作。

### Task 4: 构建与交付

- [ ] 核对公开目录只含 ID 与名称，未批准账号不能通过注册选择年级绕过登录限制；静态检查返回类型和边界条件。
- [ ] 用现有依赖完成后端及前端构建，检查 Git diff；不运行或添加测试套件。
- [ ] 仅提交相关文件，保留工作区原有 `.gitignore`、`.zcodeignore`；推送到指定 GitHub 仓库并按现有更新服务部署。
