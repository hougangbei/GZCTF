# Docker Compose 更新管理部署

此目录提供新部署示例。已有平台先核对数据库、上传文件、配置和端口的实际位置，再迁移到 Compose；**不要把已有数据库换成示例里的空卷，也不要覆盖原来的 XOR key**。本功能只支持 Linux Docker Compose 宿主机。开发环境和其他部署方式会在更新管理页显示“未配置”。

## 发布镜像

向 `hougangbei/GZCTF` 的 `main` 分支推送 `src/**` 变更，或手动运行 GitHub Actions 的 `Publish self-update image`。构建成功后，工作流发布 `ghcr.io/hougangbei/gzctf:sha-<完整提交 SHA>` 及 `:main`。首次使用前在 GitHub Packages 中把镜像设为公开，或在部署机执行 `docker login ghcr.io` 配置读取权限。仓库或账号变更时，同步修改工作流、`updater.json` 和 `.env` 的镜像地址；不要只改网页上的值。

工作流成功才代表新版本可以安装。若从现有旧镜像迁移，先在维护窗口按普通部署流程更新到本工作流生成的镜像；它带有 `org.opencontainers.image.revision` 标签。页面在读取不到当前标签时不会提供更新按钮。

## 安装到 Linux 宿主机

以下示例以 `/opt/gzctf` 为安装目录，执行前先按实际环境调整。保持现有反向代理、外部数据库和对象存储时，可以修改示例 Compose，但更新程序所用的应用服务名、数据库服务名、挂载目录必须一致。内置备份流程要求数据库在同一个 Compose 项目中；使用外部数据库时需要先改备份方案并验证。

1. 将 `compose.example.yml` 复制为 `/opt/gzctf/compose.yml`，将 `.env.example` 复制为 `/opt/gzctf/.env`，将 `updater.example.json` 复制为 `/opt/gzctf/updater.json`，并复制 `updater.py`。将示例密码替换为长随机值，并设置实际镜像和路径。
2. 创建 `/opt/gzctf/files`、`/opt/gzctf/backups`、`/opt/gzctf/run`，将已有上传文件迁移到 `files`。确认 PostgreSQL 数据在已有持久化卷中；新安装则允许 Compose 创建 `db_data`。数据库和上传文件都不放在应用镜像中。
3. 生成至少 32 个字符的令牌，写入 `/opt/gzctf/updater.token`。令牌文件权限设为 `600`，部署目录只允许可信管理员写入。应用容器通过只读挂载读取令牌与 Unix Socket。
4. `docker compose --env-file .env -f compose.yml up -d` 启动平台，并确认后台正常、`http://localhost:3000/healthz` 在应用容器内可访问。
5. 将 `gzctf-updater.service.example` 按实际 Python 路径复制到 `/etc/systemd/system/gzctf-updater.service`，执行 `systemctl daemon-reload` 和 `systemctl enable --now gzctf-updater`。此宿主机服务需要调用 Docker CLI 和读取备份目录，因此应只由可信运维安装。确认 `/opt/gzctf/run/updater.sock` 可由应用容器读取。
6. 打开后台“更新管理”。它显示当前镜像提交、最近一次成功构建的提交和更新任务状态。只有管理员可发起更新。

示例中的 `GZCTF_IMAGE` 初始值是 `:main`；稳定运行后，建议改为第一次成功构建的 `:sha-<SHA>`，避免以后的手动 `compose up` 意外追随移动标签。更新程序每次都会改写该变量为固定 SHA 标签。

## 更新时发生什么

更新程序只接受固定 GitHub 工作流中最近一次成功构建的提交。它先拉取镜像并检查提交标签，再停止应用、备份数据库和 `files`、切换 `GZCTF_IMAGE`，最后只重建 `app` 服务。备份保存在 `/opt/gzctf/backups/<UTC 时间>-<SHA>/`，其中包含 `database.dump`、`files.tar.gz` 和原 `.env`。PostgreSQL 和 Redis 容器不会被更新任务重建。

数据库迁移会在新应用启动时自动执行。如果备份失败，更新程序尝试重启旧应用；如果新应用启动失败，更新程序停止它，保留备份，并恢复 `.env` 中的旧镜像选择，等待人工处理。**此时不能仅启动旧镜像**：数据库可能已被新版本迁移，必须先检查日志和备份。

## 人工恢复

在维护窗口确认完整备份目录后，先停止应用。使用该目录的 `deployment.env` 恢复镜像配置，并按实际数据库名与凭据执行 `pg_restore --clean --if-exists` 恢复 `database.dump`。把 `files.tar.gz` 解压到原上传目录，检查所有权与权限，再启动旧应用并核对账号、题目和附件。若更新过程中产生了新数据，恢复备份会丢失这些新写入；先由运维决定是否保留。恢复命令依现场数据库与目录布局而定，不应由网页自动执行。

恢复完并核对平台后，先停止更新程序，归档 `backups/update-state.json`，再启动更新程序。中断任务或已创建备份的失败任务会锁住后续更新，直至运维完成此步骤。

## 操作限制

- 不要将 Docker Socket 挂载到 Web 应用；更新程序的 Unix Socket 仅接受“查看状态”和“安装已验证的最新提交”。
- `updater.json`、令牌、`.env`、备份文件仅放在宿主机，不推送到 GitHub。
- 当前实现一次只处理一个更新，检查 GitHub 的结果缓存五分钟；`main` 构建尚未成功前不会显示更新。
- 该部署方案不会自动推送代码或改动线上内容。GitHub 推送、镜像发布和部署初始化由维护者完成。
