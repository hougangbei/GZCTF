# GZCTF 内网 amd64 部署包

目标：Linux x86_64（Intel/AMD 64 位），Python 3.8+、Docker Engine、Docker Compose 插件 v2.18.1+。完整安装包包含应用、PostgreSQL 15、Redis 7 三个镜像；应用更新包可以仅包含新应用镜像，并复用本机已验证的数据库/Redis 镜像。安装和更新不在线拉取镜像，不需要 Git、Node 或 .NET SDK。Docker/Compose/Python 本身不包含在包内，需要运维提前安装并维护。

这是单业务机部署工具，不会迁移 CTFd 数据，也不会修改现有 CTFd、FRP、Harbor 或题目机。动态题目 Docker 控制通道及学生访问入口仍需配置和验收。应用镜像的构建提交见对应 Release 说明及包内 source.json（如提供），镜像内容指纹记录于 manifest.json。NuGet 依赖检查存在包含高危级别的漏洞告警；镜像包不是“无漏洞”或“绝对安全”的认证。Ubuntu 20.04 已结束标准安全维护，使用它时必须安排 ESM/离线安全补丁或升级到受支持系统。

已有实例更新到 20261027 技能树/题目弹窗版本，请优先阅读[本版更新命令](TRAINING_UI_UPDATE.zh-CN.md)。[独立公网入口方案](TRAINING_PUBLIC_ACCESS.zh-CN.md)为后续网络实施设计。

## 传入并核验

从可信 GitHub Release 下载本包与对应 `.sha256`。在 Linux 上执行：

```sh
sha256sum -c gzctf-intranet-amd64-20261026.tar.gz.sha256
mkdir gzctf-offline
# 解压前确认包来自可信发布渠道；校验值应从可信渠道获取。
tar -xzf gzctf-intranet-amd64-20261026.tar.gz -C gzctf-offline
cd gzctf-offline
```

SHA-256 用于完整性核对，不能单独证明发布者身份。工具会再次核对包中文件和导入镜像的配置/内容指纹。请勿执行来源不明的安装脚本；本包不附带私钥，也未宣称完成可信发布者签名验证。

## 首次安装

选择一个全新的专用安装目录和未使用的卷名前缀。原有部署必须另行迁移，禁止指向它的数据库目录后尝试安装。

```sh
uname -m   # 应为 x86_64
docker compose version
sudo python3 manage.py --root /opt/gzctf install \
  --bundle "$PWD" --prefix gzctfintranet --port 18080 --bind 127.0.0.1
```

安装会导入三个镜像，生成私有随机密钥和初始管理员密码，显式创建外部数据卷并启动平台。初始账号 `Admin`；密码仅保存在 `/opt/gzctf/.env` 的 `ADMIN_PASSWORD` 中，使用服务器上的可信编辑器查看，不要发送到聊天或 GitHub。首次登录后修改密码；环境变量中的初始密码不会重置已存在账号。

默认 HTTP 仅绑定宿主机回环 `127.0.0.1:18080`，应通过已有 HTTPS 入口访问。不公开数据库、Redis 或健康检查端口。若安装失败，保留目录、卷和日志进行诊断；不要删除后重装。禁止重跑 install 覆盖原密钥。

```sh
sudo python3 /opt/gzctf/manage.py --root /opt/gzctf status
```

如 Docker 默认地址池耗尽，可在首次安装时由运维选择与现场网络不冲突的两个 IPv4 网段，分别通过 `--data-subnet` 和 `--frontend-subnet` 指定；不要为腾出网段删除其他项目的网络。

初装 app 启动失败时，可在确认原因并修复私有配置后，由运维使用完整的 Compose 参数重启；切勿重建数据卷。不要把 `GZCTF_UPDATE_VALIDATION` 加入正式运行配置。

## 各类数据在哪里

- 固定外部卷 `<prefix>-postgres`：账号、成绩、题目配置、DataProtectionKeys 等数据库内容。
- 固定外部卷 `<prefix>-redis`：缓存和 Redis 持久化状态。
- `/opt/gzctf/files`：附件、上传、日志等；不在应用镜像内。
- `/opt/gzctf/.env`：数据库密码、XOR key、初始管理员密码、实例 ID、固定卷名和镜像标签，权限 600。
- `/opt/gzctf/appsettings.json`：Harbor、题目 Docker、SMTP、代理信任等现场运行配置，只读挂载入应用。
- `/opt/gzctf/installation.json`：固定实例及卷创建身份，不能删除或自行修改。恢复操作同时验证卷标签、创建时间和实际挂载。
- `/opt/gzctf/compose.site.yml`：现场额外网络/挂载等配置，更新时保留，备份与恢复时一起处理。
- `/opt/gzctf/backups`：数据库、文件、Redis、运行配置及旧版选择的一致性备份。目录权限 700，含敏感数据，必须加密异机保存并演练恢复。

不要执行 `docker compose down -v`、`docker volume prune` 或删除上述数据目录。持久化卷不等于备份，磁盘损坏仍会丢数据。本工具从不删除生产数据卷。数据库/Redis 的引擎镜像升级是独立运维任务，app 更新不会顺带替换它们；应用新增字段则随其 EF Core 迁移执行。

## 接入现有 FRP 和题目机

现有 FRPC 在 Docker 网络 `ctfd_frp` 中，可以将以下配置合并到 `/opt/gzctf/compose.site.yml`，前提是该网络确实在同一 Docker 主机上且管理员认可这项接入：

```yaml
services:
  app:
    networks:
      frp:
        aliases: [gzctf-app]
networks:
  frp:
    external: true
    name: ctfd_frp
```

如果现场覆盖文件已有 IPAM 等网络设置，应保留它们，再合并上述网络。然后 FRPC 的网站代理目标可设为 `gzctf-app:8080`；域名、HTTPS 和代理名称按现场规则安排。不要复制 Token 到公开文件。此设置只解决网站入口，不自动创建动态题目代理。现场覆盖文件的权限应保持 600，不能挂载宿主机 Docker Socket、根目录或数据库卷到 app。

安装不会授权 app 访问业务机 Docker。默认 appsettings 中题目 Docker 指向 `/run/gzctf-challenges/docker.sock`；宿主机目录 `/opt/gzctf/challenge-control` 已只读挂载到该目录，需运维先建立认证的远程控制通道或改为已有可信的 Docker API 连接。当前代码未实现完整的原生 mTLS 客户端证书配置，不应只填一个 HTTPS 地址便认定接入成功。不要开放未认证的 2375。

Harbor 私有拉取凭据配置在 `appsettings.json` 的 `Registries`，键为 `harbor.nynusec.com`，值包含 `UserName`、`Password`。使用只读机器人凭据。后台题目填完整镜像路径。修改配置后在维护窗口验证 Compose 并只重建 app：

```sh
cd /opt/gzctf
sudo docker compose --env-file .env -p gzctfintranet \
  -f compose.yml -f compose.site.yml config --quiet
sudo docker compose --env-file .env -p gzctfintranet \
  -f compose.yml -f compose.site.yml up -d --no-deps --force-recreate app
```

如果安装时使用其他 prefix，以上 `-p` 必须替换为 `.env` 中 COMPOSE_PROJECT 的原值。应用默认不信任任意反向代理转发头，需要按实际入口配置可信代理；不能将所有来源设为可信。

正式提供服务前，必须实测动态题目创建、学生访问、Flag、停止/到期清理以及与办公网络隔离。未配置题目连接时，网站基础功能可以先验证，但动态题目和相关清理任务不能视为已就绪。

## 备份

```sh
sudo python3 /opt/gzctf/manage.py --root /opt/gzctf backup
```

会短暂停止 app、保存并停止 Redis，数据库保持运行；完成数据库 dump、文件和缓存备份后恢复服务。应确保没有其他程序写入同一数据库或文件目录。数据库归档会执行 `pg_restore --list` 校验；这只证明归档可读取，真实可恢复性还要通过恢复演练证明。

备份目录会显示在输出中，不会打印密码。备份无自动清理，保留当前和可恢复旧版镜像，并预留至少两份数据库/文件及镜像空间；最低 2 GiB 检查不能替代真实容量规划。

## 前后端及数据库字段更新

先在测试环境以数据库副本验证新版本迁移、业务流程和旧浏览器页面。新包应由构建工具导出，保持数据库与 Redis 镜像内容一致。

1. 下载并验证新包，将其解压到另一个目录；不要覆盖正在运行的安装目录。
2. 在外部反向代理/FRP 入口切换维护页面，确认没有其他写入客户端。工具不会替你操作外部入口。
3. 执行：

```sh
sudo python3 /opt/gzctf/manage.py --root /opt/gzctf update \
  --bundle /path/to/new-bundle --maintenance-confirmed
```

工具在停机前校验包、导入镜像并检查数据库/缓存内容未变；停机备份后只替换 app。新 app 启动执行 EF Core 迁移并完成健康检查与前端入口检查。失败则停止候选版本，恢复数据库、完整文件、Redis 及旧配置/镜像。成功后仍需人工核对登录、附件、题目和成绩，再解除入口维护。

自动恢复会重建本实例的 `gzctf` 数据库，因此必须保持外部入口维护，不能让候选版本接受真实写入。恢复备份会回退到备份时点。不能仅换回旧镜像连接可能迁移过的数据库。

如果断电、进程中断或恢复失败，`recovery-required.json` 会保留并阻止继续更新。保持维护、查看日志，使用确认后的备份执行下一节恢复；不要直接删除状态文件或强行启动旧镜像。状态标记不是跨服务器分布式锁，此工具只支持单业务机、单操作进程。

## 人工恢复

恢复会覆盖现有数据库和缓存，文件目录替换前会保留为 `files-before-restore-*`，但这不保存当前数据库。需要保留恢复前的新写入时，应先另做备份，再决定恢复时点。

从本实例的备份中选择完整目录，并从私有 `.env` 查看 INSTANCE_ID：

```sh
sudo python3 /opt/gzctf/manage.py --root /opt/gzctf restore \
  --backup /opt/gzctf/backups/已核对的备份目录 \
  --confirm-instance 本实例的INSTANCE_ID --maintenance-confirmed
```

命令在校验实例 ID、备份校验值、旧镜像和密钥一致性后恢复。旧镜像必须仍在本机；不要清理它。任何错误均应保持外部维护并检查状态和日志。完整实例丢失、外部对象存储、数据库密码变更或主机迁移不属于这个本机恢复命令的范围，需要另行恢复卷、配置和网络。

## 发布与验证范围

本包不在线拉镜像，也不授予网页更新宿主机权限。它不包含 Docker 引擎安装器、HTTPS 证书、Harbor 凭据、用户数据或外部 FRP 配置。可用性验证不能代替漏洞修复与网络隔离验收；也不承诺零停机或任何情况下零数据损失。

本地开发测试使用 ARM64 Mac 上的 amd64 模拟，生产前仍需在原生 Linux x86_64 上验收。打包时应记录三镜像内容指纹、组件版本和测试结果。

## 本包已完成的验证

11 项安全单元测试通过。Docker 隔离环境完成首次安装、备份、正常 app 替换并核对数据/密钥/现场配置、当前 Compose 损坏后的人工恢复、无效候选配置停机前拒绝，以及候选镜像启动失败后的完整自动回滚。失败回滚测试同时验证备份之后新增的表被移除、数据库值和附件被还原、Redis 值恢复、旧应用健康。测试使用独立卷、目录和网络，不连接生产数据库。

## Compose 2.18.1 兼容补丁

原始 20261026 完整包的部署脚本保守要求 Compose 2.20+。兼容补丁将门槛调整为实际验证的 2.18.1，不更换三个镜像，不升级宿主机 Docker/Compose/Python，也不修改已运行的项目。下载兼容工具包及校验文件后，校验并解压到已经解压的原始完整包目录（必须含原 images.tar.gz），同时更新 manage.py、README.md、compose.yml 和 manifest.json，随后执行首次安装。不要只手改版本数字，否则会触发文件完整性校验。

兼容性验证使用官方 Compose v2.18.1 的独立二进制及 Docker Desktop 的较新 Engine；没有替换本机的默认 Compose 插件。它验证 Compose 命令/配置和容器生命周期，不等同于已在现场 Docker 24.0.2/Ubuntu 20.04 完成验收。先在独立项目验证，不改动现有 CTFd 服务。

## 管理员授权

只有初始化创建的内置 `Admin` 账号可以授予或撤销管理员权限。其他管理员仍能管理普通用户，但不能修改其他管理员、重置管理员密码或通过批量导入覆盖管理员账号。内置 `Admin` 的名称和管理员身份不能修改；已有管理员不会自动降级。本规则无需数据库表结构迁移。

## 运行环境与验证范围

Harbor 是镜像仓库，不提供题目容器运行能力。独立题目机仍须配置 Docker 控制连接；业务机不应为了消除错误而直接挂载本机 Docker socket。未配置时，平台会返回运行环境不可用的提示，基本后台和实例状态查询仍可用。

Docker 题目实例的 CPU 和内存限制已在真实容器中验证；当前 Docker 实现尚未强制执行可写层磁盘配额，不能把 `StorageMb` 当作已生效的磁盘上限。学校内网、Harbor 登录和远程题目机需要现场联调。

本次代码通过 .NET 单元与 PostgreSQL 集成测试、前端类型检查/单元测试/构建、浏览器权限与后台操作回归，以及离线部署和源码更新 Python 测试。当前编译范围之外的旧比赛测试、Kubernetes/MinIO 模式不在此次通过结论内。构建仍报告既有 MessagePack 与 System.Security.Cryptography.Pkcs 依赖漏洞，需要另行评估升级；测试通过不能证明绝对安全。
