# 在更新管理中配置来源

多来源更新使用独立的宿主机更新服务，支持 macOS/Linux 本机 .NET 发布运行方式，以及 Linux Docker Compose。页面可保存来源，不再要求修改固定的 GitHub 仓库配置。初次安装更新服务、指定部署位置和授权本机目录仍需运维操作；页面不能修改运行命令、密钥或任意系统目录。

## 三种来源

- GitHub：填写公开仓库地址或 `https://github.com/owner/repo/tree/branch`，可单独填写分支/标签。校验时固定实际提交，构建前后端；没有推送到 GitHub 的本地修改不会出现。
- 更新包：填写 ZIP、TAR、TAR.GZ 的 HTTP/HTTPS 下载链接；也可填写整个下载文件的 SHA-256。支持源码归档和下文定义的发布包，普通介绍网页不能作为下载包。
- 本机目录：填写**运行更新服务的服务器上的绝对路径**。目录必须在宿主机 `allowed_source_roots` 范围内；支持源码或发布包。当前运行目录不会被覆盖，服务在版本目录之间切换。

默认拒绝下载到本机/内网地址、URL 凭据、符号链接、压缩包越界/特殊文件。私有镜像/内网下载由运维在私有配置里设置 `allow_private_downloads: true`，不要把访问令牌写进网页地址。GitHub 仓库需公开可读。

## 操作流程

1. 在管理后台的“更新管理”保存来源。
2. 点击“验证来源”。页面逐项显示部署环境、来源识别、构建与完整性、数据库副本迁移及候选程序试运行的结果。构建可能需要数分钟，浏览器关闭不影响任务。
3. 全部通过后，结果绑定来源、候选文件摘要和当前已应用的数据库迁移，有效期一小时。应用时会再次校验文件和数据库结构。
4. 点击“应用更新”并确认。更新程序停止应用、备份 PostgreSQL/上传文件/运行配置，切换版本并检查健康状态。
5. 启动失败时先停止新程序，再用完整数据库备份及文件恢复旧版本。恢复失败进入“需要人工恢复”，禁止新的更新。

源码中的构建脚本和发布程序都是管理员选择执行的代码；校验验证完整性和兼容性，不能证明任意第三方代码可信。只选择可信项目。旧版源码/发布包如果没有隔离校验协议，会显示不兼容并阻止替换。

## 发布包格式

包根目录含 `GZCTF.dll`、`.deps.json`、`.runtimeconfig.json`、完整依赖、`wwwroot/index.html` 和 `gzctf-release.json`。使用本功能版本的源码发布（后端必须实现 `GZCTF_UPDATE_VALIDATION=true` 下的 `/update-validation` 协议）；包不能包含运行配置、密钥、数据库或用户文件。

```sh
dotnet publish src/GZCTF/GZCTF.csproj -c Release -r osx-arm64 --no-self-contained -o /tmp/gzctf-release
# 移除发布输出中自动复制的本机 appsettings 文件；运行配置保存在部署目录外。
python3 deploy/self-update/make-release.py /tmp/gzctf-release osx-arm64 1.8.7
cd /tmp/gzctf-release
tar -czf /tmp/gzctf-release.tar.gz .
```

`runtime` 必须与部署匹配（例如 `osx-arm64`、`linux-x64` 或 `linux-arm64`）。清单包含 `project: GZCTF`、`validationProtocol: 1`、`version`、`runtime` 和 `files` 字典（相对文件名 → SHA-256）。清单及全部文件会被核对；源码包自动构建并生成清单。

## 本机部署

1. 复制本目录全部 Python 文件到固定的更新服务目录；服务代码本身不在网页更新替换范围内。需要 Python 3.10+、Git、.NET SDK 10、Node、pnpm、Docker CLI。PostgreSQL 在指定 Docker 容器中，更新服务通过容器内 `pg_dump`/`pg_restore` 备份及创建临时校验数据库；数据库角色需有创建数据库权限。
2. 复制 `managed-updater.example.json` 为私有 `updater.json`。填写当前数据库、构建工具绝对路径、上传目录、运行配置和允许的更新来源根目录。`work_dir`、`backup_dir` 目录权限为 `700`；配置与令牌文件权限为 `600`。
3. 首个发布版本放在 `/opt/gzctf/releases/initial`，`/opt/gzctf/current` 创建为指向它的符号链接。运行配置放在 `/opt/gzctf/appsettings.json`，上传目录保持原路径。初始发布目录中的 `files` 链接指向该目录；后续由更新程序创建。不要修改原 XOR key。首次接管已有本机部署时，设置 `native.data_protection_application_name` 为原部署的数据保护应用标识（ASP.NET 默认是原 ContentRootPath，含末尾 `/`），以后保持该值不变；否则旧登录票据和加密审计记录可能无法解密。新部署默认使用固定的 `current_link` 路径加末尾 `/` 作为标识。该设置独立于实际发布目录，不会使新版本读取旧的前端文件。
4. 在运行配置设置 `Updater:SocketPath`、`Updater:TokenFile`，与服务配置中的路径一致。令牌至少 32 个字符。原数据库、缓存、存储连接和 XOR key 保持不变。
5. 停止原本机应用后，用以下命令启动管理的应用和更新服务，二者应由同一可信系统账号运行。生产环境使用 systemd/launchd 托管更新服务；应用进程由它管理，不能同时由另一个守护程序强制重启。

```sh
python3 /opt/gzctf/updater/native-app.py /opt/gzctf/updater.json start
python3 /opt/gzctf/updater/updater.py /opt/gzctf/updater.json
# 单独停止应用（保留数据）
python3 /opt/gzctf/updater/native-app.py /opt/gzctf/updater.json stop
```

本机默认仅监听回环地址，按既有反向代理提供访问。端口可通过 `native.server_port`、`native.metric_port` 配置。候选校验使用临时端口、数据库、上传目录和内存缓存，关闭定时任务及遥测；不会连接实际更新服务。

## Compose 部署

原 `updater.example.json` 和固定镜像更新方式继续可用。启用多来源时使用 `deployment_mode: compose`，保留 `work_dir`、`backup_dir`、`files_dir`、`socket_path`、`token_file`、`allowed_source_roots`、`database`、`tools`；添加 `runtime: linux-x64`（或 linux-arm64）、`compose_file`、`env_file`、`settings_file`、`app_service`。`settings_file` 是服务启动时读取的宿主机私有 appsettings JSON；Compose 应将其只读挂载到 `/app/appsettings.json`。原环境变量配置保持不变。

应用镜像通过宿主机 .NET SDK 构建及 Docker 构建，然后在与数据库相连的独立容器中验证。`database.container` 填实际数据库容器名；如有多网络，填写 `validation_network`。Compose `.env` 必须有唯一 `GZCTF_IMAGE=`。校验产生的镜像被固定，应用时只替换该应用服务，不重建数据库与缓存。

## 备份、失败恢复与空间管理

备份目录包含 `database.dump`、完整 `files/`、`appsettings.json`、`deployment.json`，Compose 另有 `deployment.env`。自动恢复会删除并重建**配置中指定的应用数据库**，再导入完整备份，避免遗留新版本创建的表；系统数据库名不允许用于更新。应用停机期间不要通过其他服务写入此数据库。

若状态为 `recoveryRequired` 或 `interrupted`，先停止更新服务，检查日志及最近备份，停止应用并完整恢复数据库、文件和部署设置，核对旧版本正常后，再由运维归档 `managed-update-state.json` 并重启更新服务。不要直接启动旧程序连接可能迁移过的数据库。

暂存目录、已发布版本、备份和失败文件树默认保留供检查；运维应定期清理无引用版本及过期备份，并保留当前 `current` 链接所指向的版本。校验至少要求 2 GiB 及上传文件三倍的空闲空间；大型数据库还需额外预留备份及恢复空间。


## Docker Desktop 的本机通信

macOS Docker Desktop 无法直接使用宿主机的 Unix Socket 文件时，可在宿主机配置中增加 `"listen_tcp": {"host": "127.0.0.1", "port": 51237}`。此接口只允许绑定回环地址，仍必须使用同一份私有令牌。应用设置 `Updater:BaseUrl=http://host.docker.internal:51237`，并通过只读挂载的 `Updater:TokenFile` 读取令牌；不要把接口绑定到局域网地址。

未配置 BaseUrl 时继续使用原 Unix Socket。明确填写了无效 BaseUrl 会拒绝连接，不会静默改用其他地址。TCP 与 Socket 仅是应用连接本机更新进程的方式；网页上的更新来源地址仍单独保存和校验。
