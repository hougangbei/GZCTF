# GZCTF / CTFd 训练迁移执行计划

> For agentic workers: 使用 subagent-driven-development 与 TDD；用户已选择题目转训练、账号及历史成绩迁入、保留旧密码首次成功登录升级。禁止 Astra。保留线上及源平台现状。

**Goal:** 后台统一接收 GZCTF-GAME 与 CTFd ZIP，预检后幂等导入当前四层训练体系、账号及独立历史档案。
**Architecture:** 安全ZIP读取→来源解析→公共迁移模型→只读预检→事务数据库写入+文件补偿；各来源实例标识隔离ID。原内部导入端点保留。管理员账号/旧凭据不覆盖现有用户，历史不写训练积分。
**Tech Stack:** ASP.NET Core / EF Core PostgreSQL / Identity / BouncyCastle / React Mantine SWR。

## T1 — 安全读取与来源适配

文件：Features/Imports/Application/PlatformImportContracts.cs、Infrastructure/PlatformArchiveReader.cs、GzctfGameAdapter.cs、CtfdBackupAdapter.cs；UnitTests/Features/Imports/PlatformArchiveTests.cs。
- [x] 定义跨来源规范：题目、附件、用户、队伍、历史提交、问题清单及源元数据；明确字段单位。
- [x] 先写GZCTF-GAME及CTFd results格式解析、恶意路径/重复项/缺文件/未知格式的失败测试。
- [x] 有界读取ZIP（128MB压缩/256MB展开、10000文件、逐字节限制），不执行内容，不下载URL。
- [x] GZ题型、Flag、中文、7分类、4附带附件容器题、空模板随机Flag语义完整识别。
- [x] CTFd标准题/动态分值题、flags/hints/files、users/teams、submissions/solves/awards；未知插件报告，不猜测。
- [x] 真实用户ZIP仅本地读取验收，不提交账号/Flag/附件进Git。

## T2 — 数据模型、账号与旧密码

文件：Features/Imports/Domain/PlatformImportModels.cs、Infrastructure/PlatformImportModelConfiguration.cs、Application/ImportedAccountService.cs、CtfdPasswordHasher.cs、AppDbContext、IdentityExtension及EF迁移与快照。
- [x] 先写账号登录、角色/邮箱冲突、幂等映射、历史隐私测试。
- [x] 新批次/来源映射/历史账号及队伍/提交/奖励数据；唯一键来源实例+对象类型+源ID。
- [x] 导入普通账号；原封禁/验证状态保持，不授予源管理员权限，冲突显式阻止，不覆盖现有凭据。
- [x] 支持限定参数的CTFd bcrypt_sha256格式，复用项目BouncyCastle依赖；原生Identity不受影响。成功返回SuccessRehashNeeded，Identity重哈希后旧密码摘要失效；不支持格式预检提示重置。
- [x] 数据库迁移及约束测试；历史成绩独立存储，不写ChallengeProgress。

## T3 — 预检、导入与当前模型写入

文件：Application/PlatformImportService.cs、Api/PlatformImportsController.cs；IntegrationTests/Imports/PlatformImportTests.cs。
- [x] 先写真实HTTP预检/执行、管理权限/来源隔离/重复/事务回滚及附件读取测试。
- [x] POST/api/admin/platform-imports/preview、POST同路径执行（上传同一ZIP和选项/确认指纹），GET列表/详情。
- [x] 默认新技能树草稿，每类别同名技能；可关联已有技能（携带并发版本）；保留原发布状态为来源元数据。
- [x] 存储附件并改写引用，容器JSON包含补充附件；实际运行时Flag生成兼容源语义，不导入运行实例或拉镜像。
- [x] 事务导入内容/映射/账号/历史，失败文件补偿；相同来源同包/同选项重复执行返回已有批次，不重复写；已有源对象不覆盖手改。
- [x] 历史管理详情+当前用户个人历史API，禁止匿名/跨用户泄露，响应不含旧密码摘要/提交Flag。

## T4 — 管理向导与历史页面

文件：ClientApp/pages/admin/imports/Index.tsx、hooks/usePlatformImports.ts、utils/platformImports.ts、pages/account/history/Index.tsx（必要时结合现有账号导航）。
- [x] 先写浏览器ZIP→预检→报告→导入、错误清单、历史隐私与手机流程测试。
- [x] 格式自动识别；来源实例、路线名称/目标技能、账号/历史选项；显示数量、阻断/警告、账号冲突及不支持插件。
- [x] 避免普通页面显示Flag或密码；导入后链接草稿/批次、表格化失败项；旧内部ZIP仍可用。
- [x] 全部表面跟随当前主题和个人强调色，手机可用，失败保留输入。

## T5 — 验收与交付

- [x] dotnet单元及集成顺序运行，前端unit/check/build、针对性浏览器验收。
- [x] 真实GZ包26题/19文件、中文正文、容器附带附件、随机模板/停用/分类数量核对。
- [x] 合成CTFd账号摘要验证（正确/错误/Unicode/重置后旧摘要失效）、历史与训练榜隔离；实际CTFd完整包尚待提供，明确未验收项。
- [x] 独立规格审查→质量审查，修复后回归；更新文档及本地提交，不自动发布/部署。

验证：/Users/bubble/.dotnet/dotnet；Docker DOCKER_HOST=unix:///Users/bubble/.docker/run/docker.sock；pnpm目录src/GZCTF/ClientApp。编译不并发，防止deps.json冲突；代理工作先仅编辑各自文件，由主执行者安排构建。


## 执行与验收记录（2026-11-02）

- 已完成：安全ZIP识别/解析、当前四层写入、账号导入与旧密码首次升级、独立历史档案、管理员预检/导入/批次页、个人历史、附件及资源读取、源模板Flag生成兼容。
- 真实ExSolo样本：26题、7类别、7技能、19文件；16静态附件题、10动态容器题，4附带资源容器题；25启用/1停用。真实HTTP预检/执行与重复执行通过，资源全数入库、补充附件下载通过，不启动容器。临时私有验收源码已删除，原包与Flag未加入Git。
- 完整后端单元测试341项通过；完整集成225项通过、无跳过。包括并发跨来源邮箱冲突、暂存期间目标版本变化、部分文件写入故障补偿、大小写Flag经jsonb持久化后判题等。
- 前端unit33项、严格类型检查、生产构建通过；导入/历史页6项浏览器测试，训练目录/实例等回归16项，共22项不同浏览器检查通过。
- 两轮独立审查通过。修复动态附件池误混资源、别名改写、原始令牌保留、目标版本竞态、共享类别未保存引用、0分被数据库默认值覆盖等问题。
- 账号资料/历史备份中身份验证令牌与源会话被排除；支持的密码摘要仅用于本地导入账号，首次成功验证后变为本地Identity摘要。未知摘要需重置，无统一默认密码。
- CTFd实际校园站暂不能从本工作环境访问，真实完整备份尚未提供；标准包测试不等于该站插件/真实数据验收。这是交付限制，并未标为通过。
- 现有依赖漏洞警告仍存在，本次没有升级所有依赖或宣称无漏洞；本轮未更改源平台、线上库、Harbor、网络入口或SSH通道，未发布镜像。

- 账号冲突可逐项明确跳过后重新预检；跳过选项绑定计划指纹，源记录保留为不绑定本地账号的历史，未自动合并账号。新增2项集成与1项浏览器回归通过。
