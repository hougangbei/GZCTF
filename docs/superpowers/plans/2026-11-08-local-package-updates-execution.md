# Local Package Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use subagent-driven-development or executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 管理员通过服务器目录或网页上传现有amd64离线包，检查后确认备份更新，不依赖浏览器存活。

**Architecture:** 独立offline宿主机服务复用固定manage.py，提供受令牌保护的包API及持久化任务；应用仅鉴权、流式转发和显示，不取得Docker权限。保留原更新模式。

**Tech Stack:** Python3.8、Docker/Compose、ASP.NET10、React/Mantine、Vitest/Playwright。

## 固定接口

所有应用路径在 `/api/admin/updates`，服务路径去掉此前缀。

```ts
type PackageSettings = { mode:'offline'; directory:string; maxUploadBytes:number; allowedRoots:string[] }
type PackageItem = { id:string; name:string; size:number; modifiedAtUtc:string; source:'directory'|'upload'; sha256?:string; checksumProvided:boolean }
// GET packages/settings; PUT packages/settings {directory}
// GET packages => {directory,items:PackageItem[]}
// POST packages/upload: 原始application/octet-stream，X-File-Name=encodeURIComponent(name)，固定Content-Length
// POST packages/{id}/checksum {sha256}: 单独可选外部摘要，不接受路径/命令
// POST packages/{id}/validate => 202状态
// POST packages/{id}/apply {validationId,operationId,maintenanceConfirmed:true} => 202状态
type PackageValidation = {id:string;packageId:string;sourceDigest:string;version:string;artifactHash:string;expiresAtUtc:string;canApply:boolean;checks:{key:string;label:string;status:'passed'|'failed'|'running';message:string}[]}
// GET status扩展，保留现有字段：
type OfflineStatus = {configured:true;mode:'offline';phase:string;message?:string;currentSha?:string;validation?:PackageValidation;backupPath?:string;operationId?:string;packageId?:string;changedAtUtc?:string}
```

服务报错统一结构 `{message}`，400输入、404包不存在、409并发/过期/恢复状态、413过大、503服务不可用。上传超时独立于20秒控制请求。上传过程XHR可取消，成功只登记包，不执行。最多1GiB，校验文件最多4KiB，默认扫描100个直接子归档。

## Task 1 — 宿主机离线包服务及测试

Files: 新建 `deploy/self-update/offline_packages.py`、`offline_updater.py`、`tests/test_offline_packages.py`、`tests/test_offline_updater.py`；修改 `updater.py` 以offline模式分派并保持旧协议；按需为固定 `deploy/offline/manage.py` 增加进度回调，不使用包内脚本。

- [ ] 先写失败测试：相似路径前缀、目录/文件符号链接、路径穿越、重复条目、解压上限、截断上传、SHA不符、包替换和Python3.8支持。
- [ ] `uv run --python 3.8 --no-project python -m unittest discover -s deploy/self-update/tests` 验证红灯。
- [ ] 实现有界包仓库、原子接收、可选外部校验和不可变候选；解析manifest/source，缺少source元数据以镜像指纹展示版本。
- [ ] 固定工具完成manifest、linux/amd64镜像和兼容检查，使用现有安装身份与Compose检查，不运行包中Python代码。
- [ ] 持久化任务、重启interrupted状态、报告过期、operationId幂等与文件锁；调用Deployment.update共享备份/恢复语义，任务与浏览器请求独立。
- [ ] 测试包ID混用、更新中并发、外部命令行锁、恢复required及同operationId重放；旧26+2测试仍通过。

## Task 2 — 应用转发与授权

Files: `Features/Updates/Application/UpdateAgentClient.cs`、新增 `Api/AdminUpdatePackagesController.cs`；测试在现有Updates测试目录。

- [ ] 先写管理员权限、错误长度/文件名、上传上限及服务超时测试。
- [ ] 为客户端加受限stream转发，不将整个包读进内存；上传只传原始流与文件名和固定长度，令牌仍从私有文件读取。
- [ ] 新Controller保留RequireAdmin、NoStore和AuditAction，转发固定受验证路径，错误不泄露原始异常；校验GUID、摘要、operationId和维护确认。
- [ ] 后端测试顺序运行，避免共享输出目录冲突。

## Task 3 — 更新页包工作区

Files: 新建 `components/admin/updates/OfflinePackageUpdates.tsx` 和样式；修改 `pages/admin/Updates.tsx`，补充中文/英文admin翻译；增加 `tests/e2e/offline-updates.spec.ts`。

- [ ] 写浏览器红灯：双入口、无校验文件提示、扫描/上传/取消、检查结果与过期、确认变更及重连。
- [ ] 检测mode=offline渲染专属工作区，旧source请求在offline模式不做强制成功条件；其他模式原页面不变。
- [ ] 服务器绝对目录保存/扫描；包列表明确来源、时间、大小、摘要校验状态。
- [ ] XHR原始流上传显示进度；取消后不登记，拒绝超限和不支持扩展名；SHA文件解析有界并提交摘要。
- [ ] 检查后才允许确认维护更新；确认回调重读资格，绑定当前包/报告，修改包/摘要/目录使报告失效。
- [ ] 状态轮询重连，任务活跃时禁用重复操作，失败/恢复入口明确；颜色使用现有主题变量。

## Task 4 — 一次接入与运维文档

Files: 新建 `deploy/self-update/install-offline-updater.py`、配置模板与 `OFFLINE-PACKAGE-UPDATES.zh-CN.md`。

- [ ] 脚本先验证runtime安装身份和私有文件，备份现有配置；固定部署服务路径，拒绝覆盖非匹配服务。
- [ ] 安装固定Python服务及manage模块、生成token、systemd托管；新增仅UnixSocket/只读token挂载，保留原settings和compose.site；自带dry-run和重复执行保护。
- [ ] 解释首次接入需新版应用一次部署，后续可网页更新；给出单站点Nginx上传上限/超时、检测/恢复命令。
- [ ] 单元验证配置合并保持SSH通道/邮件/仓库/端口池；systemd不使用ProtectHome=true阻断/root部署。

## Task 5 — 集成与独立审查

- [ ] Python3.8全工具、.NET单元+Docker集成、前端单元/check/build与新旧更新页浏览器测试。
- [ ] 实际隔离Compose接入offline服务，上传真实发行包并验证→更新→重连，账号/课程/配置/附件保留；目录模式再次更新；错误包不停止服务，故障恢复与锁可验证。
- [ ] 规范审查后质量审查，逐条修复；原CTFd、Harbor、比赛代理和正式部署不改。
- [ ] 更新设计状态与本文结果，提交代码；本轮不自动发布/部署。
