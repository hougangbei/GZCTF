# 后台离线包更新：宿主机首次接入及操作

适用于已经由 `deploy/offline/manage.py` 安装、使用 `APP_IMAGE`、`compose.yml` 和 `compose.site.yml` 的 Linux amd64 Docker Compose 部署。Python 3.8+、Docker Compose 2.18.1+。不是首次安装器，不改变比赛题目机、Harbor、邮件或原 CTFd。第一次部署带新页面的发行包仍需运维在维护窗口执行；之后才可以在后台上传和扫描更新包。

## 一次接入

以下命令在解压后的发行包根目录运行；源码仓库中对应目录位于 `deploy/` 下。必须同时携带 `self-update/*.py` 和 `offline/manage.py`，不能只复制单个脚本，也不能执行待更新归档中的 Python。脚本导入固定相邻目录的 `../offline/manage.py`。

先核实当前安装身份和应用容器 UID/GID。数据库及 Redis 卷必须属于 `.env` 的实例；不能用其他安装的 `.env` 拼接。默认应用 UID/GID 为 0；如果现有应用使用非 root 用户，使用实际 UID 和有效 GID 传入 `--app-uid`、`--socket-gid`，不要把令牌改为全局可读。

```sh
python3 offline/manage.py --root /root/nynuctf/runtime status
python3 self-update/install-offline-updater.py \
  --runtime /root/nynuctf/runtime \
  --host-dir /root/nynuctf/updater \
  --packages /root/nynuctf/update-packages
```

默认是只读检查和预览，不创建文件、不操作 systemd、不重启容器。检查安装身份、恢复状态和卷后，只显示将修改的文件路径；不会显示 SMTP、仓库、数据库密码或私有令牌。

确认维护窗口后，使用相同参数增加 `--apply --activate`。`--apply` 写文件，`--activate` 才启动 systemd 并只重建 `app` 容器。`--apply` 不带 `--activate` 可以先检查文件，再由运维手动启用服务。`--python` 可指定已安装的 Python 3.8+ 绝对路径。不要在本地模拟树使用 `--activate`。

安装后目录：

```text
/root/nynuctf/updater/
  self-update/{updater,offline_updates,update_errors,managed_updates,update_sources,update_deployment}.py
  offline/manage.py                    # 固定、受信任的宿主机工具
  updater.json                        # 600 私有配置
  updater.token                       # 600；仅应用 UID 与宿主机 root 可读
  run/updater.sock                     # 660；专用组可连接
  state/                              # 700；上传、候选、检查报告、状态和审计
  bootstrap-backups/<编号>/index.json  # 首次接入的原文件及摘要
```

脚本保留 `compose.site.yml`、`.env`、`appsettings.json` 原始内容，另写 `compose.updater.yml`，仅合并两项只读挂载和更新服务设置。应用新增的权限只有私有 socket 目录和令牌文件，不新增 Docker Socket 或部署目录挂载。固定 `manage.py` 会把可选 updater overlay 加入 Compose、备份和恢复流程。运行时代码由审核过的宿主机工具替换并备份；离线包中的 `manage.py` 永不安装或执行。

systemd 单元为 `gzctf-offline-updater.service`。服务必须能访问 `/root` 下的 runtime；单元使用 `ProtectHome=false`，不能照搬会隐藏 `/root` 的 `ProtectHome=true`。私有目录权限及应用只读挂载承担访问隔离。重复运行不会重新生成令牌、重置网页保存的扫描目录或改写不变文件；只在内容变化时保存原文件到 bootstrap 备份。升级宿主机工具前等待所有更新任务结束。

## 页面操作

1. 管理员打开后台更新页。服务状态应为已连接，模式为离线包。
2. 服务器目录：填写允许根目录下的绝对真实路径并保存，然后扫描。只扫描直接子文件，不递归，最多列出100个包；为了避免大目录拖慢请求，最多检查1000个目录条目。支持 `.tar.gz`、`.tgz`、`.tar`、`.zip`。超过边界时把发行包放入单独的干净子目录。
3. 本机上传：选择包并等待上传完成。默认最大1GiB，上传中断不注册候选；可以失败重传。上传成功仅表示接收，尚未安装。
4. 可选 SHA-256：目录文件旁放 `<完整包名>.sha256` 或 `.sha256sum`，内容为64位摘要及可选文件名；网页也可附加摘要。外部摘要不匹配会阻止更新。没有外部摘要会明确显示“未提供外部校验值”；自算 SHA-256 和内部清单都不等于签名认证。
5. 手动检查包。服务复制不可变副本，检查安全解压、内部文件清单、linux/amd64 镜像内容指纹、数据库/缓存兼容、现有安装和完整 Compose 权限。检查阶段不停止业务应用，不执行包内脚本。仅镜像引用可改变；新增命令、挂载、服务、权限或网络会被拒绝。
6. 检查通过后核对版本，关闭外部写入入口进入维护状态，再勾选确认并执行备份更新。检查报告有效期30分钟，绑定源包摘要、候选副本、完整候选文件和当前安装配置；原包、校验值或安装有变化必须重检。
7. 应用会短暂停止。宿主机服务持续执行镜像导入、备份、启动、健康检查；应用重启或浏览器关闭不取消已接受的任务。网页断连后恢复轮询，不重复提交。幂等 UUID 防止重复点击重复更新。

允许根、大小和解压边界由私有 `updater.json` 控制，网页不能扩大权限。运行时保存的扫描目录位于 `state/offline-packages.json`；配置变更须保持该路径仍在允许根中。整个部署和命令行共用 runtime 的 `.operation.lock`，不允许并行写入。

镜像包必须包含 `manifest.json`、`images.tar.gz`、`compose.yml`、`manage.py` 和 `README.md`。支持完整三镜像包，及只带新应用、复用当前兼容数据库/Redis镜像的更新包；每个导入镜像必须匹配清单。若发行包包含已声明 `gatewayImage`，会检查其内容但不会启动、重配网关。多余未声明镜像会被拒绝。

## 上传入口代理

在更新入口对应的 Nginx `location` 中设置大小和时间限制，不要全局放开，也不要改比赛代理。例如宿主机上限为1GiB时：

```nginx
location /api/admin/updates/packages/upload {
    client_max_body_size 1g;
    client_body_timeout 120s;
    proxy_request_buffering off;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
    # 保留原 location 已有的 proxy_pass、鉴权及转发头配置
}
```

这只是现有站点中对应 location 的配置片段；先合并原有 `proxy_pass` 等，再 `nginx -t` 后重载。若有 WAF、外层反向代理或网关，应逐层匹配最大大小及超时；不关闭其他入口限制。应用及宿主机仍独立检查长度，拒绝缺失长度、超大及截断请求。

## 失败与恢复

正常失败会使用同一备份恢复数据库、附件、Redis和部署文件，不能仅把镜像切回旧版后继续。runtime 的 `backups/<编号>` 不自动删除，失败候选及原包也保留。

如果显示 `interrupted` 或 runtime 存在 `recovery-required.json`，新的检查和应用被暂停。先在维护状态查看宿主机日志及备份，按发行包根目录的 `OFFLINE-RECOVERY.zh-CN.md` 使用固定工具恢复完整快照。不要执行候选包的 `manage.py`，不要删除数据库或 Redis 卷，不要绕过恢复标记。

```sh
systemctl status gzctf-offline-updater.service
journalctl -u gzctf-offline-updater.service
python3 /root/nynuctf/updater/offline/manage.py \
  --root /root/nynuctf/runtime restore \
  --backup /root/nynuctf/runtime/backups/<编号> \
  --confirm-instance <当前实例ID> --maintenance-confirmed
```

确认旧版数据库、附件、Redis、配置与健康检查全部恢复后，停宿主机更新服务，将 `state/offline-state.json` 复制为一份运维留档，人工把 `phase` 改为 `idle`、`validation` 改为 `null`，并把 `state/validation.json` 移到留档路径（不要重新使用旧批准报告）。重启服务后重新扫描和检查。此人工恢复流程没有网页“强制继续”按钮。

审计位于 `state/audit.jsonl`，记录服务收到的已鉴权管理员标识、时间、操作、包ID、摘要及结果；应用层同时保留管理员鉴权审计。网页只获得结构化错误，不返回原始命令输出、配置或凭据。
