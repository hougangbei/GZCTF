# 训练实例固定端口池与 Traefik 网关

本方案供校园网学员使用，Traefik 部署在业务机 `192.168.1.120`，独立题目机为 `192.168.1.160`。不需要为每个实例新增 DNS 或手工修改路由。默认池为 **30000–30199/TCP**，每个单端口实例占一个端口，最多200个并发实例；管理员测试容器、旧业务占用也会消耗端口。

## 先区分旧版和新版

- `intranet-training-ui-20261027` UI 更新版仍使用 Docker 默认动态端口，没有固定端口池。
- 从 `intranet-traefik-pool-20261027` 开始支持可选 `ContainerProvider.DockerConfig.HostPortRange`。未配置时保持原有动态端口行为。
- 不要在旧版上仅添加字段就认定生效；先更新镜像，再合并配置。
- 此改动没有新增数据库迁移；新包继承 UI 版已包含的图标迁移。

## 开放哪些端口

| 来源 | 目标 | 端口 | 用途 |
| --- | --- | --- | --- |
| 校园网学员 | 业务机的校园网可达入口 | TCP 30000–30199 | Traefik 实例入口 |
| 业务机 | 题目机192.168.1.160 | TCP 30000–30199 | 同号转发到容器实际宿主端口 |
| 业务机 | 题目机192.168.1.160 | TCP22 | 已配置的SSH Docker控制隧道 |
| 校园网学员 | 平台网站入口 | 现有网站端口 | 沿用原平台网站 |

若业务机前面有校园网路由器/NAT，还需让上述**整段端口按原端口号转到192.168.1.120**。HTTP网站的80/443反向代理不会自动转发这些TCP端口。平台 PublicEntry 必须填写学员实际可访问的入口IP或域名，不带协议、路径、端口；不能仅因为业务机是192.168.1.120就假设校园网可以直达它。

题目入口可以直接使用校园网可达IP，不必新增域名。建议题目入口与平台网站使用不同主机名或入口IP，浏览器Cookie不按端口隔离。

不开放未认证Docker2375。此方案提供TCP；UDP题目另行配置。

## 1. 维护与备份

安排维护窗口，暂停学员写入，通过平台管理页停止旧实例（包括管理员测试实例）。旧实例的327xx等端口不会自动变成300xx，必须在新配置生效后重新启动。不要直接手工删除数据库中的实例记录。

如果创建过单端口 Traefik 测试，先在其目录 `/root/nynuctf/traefik` 执行 `docker compose -p nynuctftraefik -f compose.yml stop traefik`，避免混淆。生成器会备份同目录中的旧配置，使用新的 `dynamic-pool` 目录，旧测试文件不会被加载。

## 2. 更新应用（在业务机以root执行）

从 Release 下载镜像包和校验文件，上传至 `/root/nynuctf/`：

- `gzctf-traefik-pool-amd64-20261027.tar.gz`
- `gzctf-traefik-pool-amd64-20261027.tar.gz.sha256`

```sh
bash <<'UPDATE'
set -euo pipefail
cd /root/nynuctf
sha256sum -c gzctf-traefik-pool-amd64-20261027.tar.gz.sha256
mkdir -p updates/traefik-pool-20261027
tar -xzf gzctf-traefik-pool-amd64-20261027.tar.gz -C updates/traefik-pool-20261027
cd updates/traefik-pool-20261027
python3 manage.py --root /root/nynuctf/runtime status
python3 manage.py --root /root/nynuctf/runtime update --bundle "$PWD" --maintenance-confirmed
UPDATE
```

更新工具会完成停机备份并替换app。数据库/Redis引擎镜像指纹保持不变，题目机Docker控制隧道保留。本包同时装载 `traefik:v3.7.16` 的amd64镜像。

## 3. 生成完整端口池并合并平台配置

仍在业务机执行。下面会提示输入**校园网学员实际可达的业务机入口IP或域名**。例如学生使用某校园网入口IP连接业务机，则填该入口IP，而不是题目机的192.168.1.160。

```sh
cd /root/nynuctf/updates/traefik-pool-20261027
read -r -p '校园网可达的业务机入口IP或域名（不带http及端口）：' TRAINING_ENTRY
python3 configure-traefik.py \
  --worker 192.168.1.160 \
  --port-start 30000 --port-end 30199 \
  --output /root/nynuctf/traefik \
  --appsettings /root/nynuctf/runtime/appsettings.json \
  --entry "$TRAINING_ENTRY"
```

脚本会：

- 生成200个TCP监听及同号转发规则；不是把整段端口转到同一个容器。
- 在原文件中仅合并 `HostPortRange`、`PublicEntry`、`PortMappingType=Default`，保留SMTP、Harbor、SSH socket Uri等其他配置。
- 修改前备份原文件，备份和私有配置权限为600，不输出密码。
- 生成独立Compose项目配置，使用Linux host网络，不挂载Docker socket或启用管理仪表盘。

生成后的两个关键字段示例（说明用途，不要用片段覆盖原文件）：

```json
{
  "ContainerProvider": {
    "PublicEntry": "学员可达的业务机入口IP或域名",
    "PortMappingType": "Default",
    "DockerConfig": {
      "Uri": "unix:///run/gzctf-challenges/docker.sock",
      "HostPortRange": "30000-30199"
    }
  }
}
```

## 4. 启动Traefik并重建应用容器

先在业务机检查 `sudo ss -lntp`：若30000–30199内有其他业务占用，先查明，不要停止不明服务。Traefik需要监听全部200个端口。

```sh
cd /root/nynuctf/traefik
docker image inspect traefik:v3.7.16 --format '{{.Os}}/{{.Architecture}}'
docker compose -p nynuctftraefik -f compose.yml config --quiet
docker compose -p nynuctftraefik -f compose.yml up -d --force-recreate --pull never
docker compose -p nynuctftraefik -f compose.yml ps
```

期望镜像为 `linux/amd64`。Traefik为常驻服务，重启策略为unless-stopped；业务机Docker服务应保持开机启用。生成器会原子替换静态配置，所以每次重新生成后都要用上述force-recreate重建网关，才能加载新的文件和监听端口。

因为appsettings通过单文件bind mount挂载，脚本以原子替换方式保存后，需要**重建**app容器，单纯restart不足以保证加载新文件：

```sh
cd /root/nynuctf/runtime
docker compose --env-file .env -p gzctfintranet \
  -f compose.yml -f compose.site.yml up -d --no-deps --force-recreate app
```

## 5. 验证全过程后再恢复学员访问

1. 在平台启动一题，新地址必须是 `入口IP或域名:30000–30199中的某个端口`。若仍是327xx，检查是否是旧实例、是否更新正确镜像/配置、是否已重建app。
2. 同时启动第二个实例，端口也在池内，且与第一个不同。
3. 在业务机用 `curl -I http://127.0.0.1:实际端口` 验证Web题；Pwn用TCP客户端。
4. 从校园网电脑访问平台返回的实际题目地址。若业务机本地能通、校园网不能通，检查校园网入口的整段TCP转发与防火墙。
5. 测试提交Flag、延长、停止、到期清理。停止后对应容器被移除、该端口可供后续新实例使用；Traefik的池监听会一直保留，没有对应容器时连接失败是预期行为。
6. 重启Traefik，实例连接应恢复；业务机SSH隧道仍应返回Docker `_ping`的OK。

正常情况无需每次开题改Traefik。分配、并发冲突和释放由Docker Engine完成，平台读取实际已分配端口返回给学员；旧实例地址在端口被后续复用后可能指向新实例，与常见端口池行为相同。

日志：

```sh
cd /root/nynuctf/traefik
docker compose -p nynuctftraefik -f compose.yml logs --tail=60 traefik
```

不要更改题目镜像内部的服务端口，例如8080；端口池限制的是题目机宿主端口。容量不足时，统一调整平台范围、Traefik配置和校园网转发范围，并在维护窗口重建网关；生成器限制单次最多1000个端口。

## 资料

- [Docker宿主端口范围分配](https://docs.docker.com/engine/network/links/)
- [Traefik非TLS TCP匹配](https://doc.traefik.io/traefik/reference/routing-configuration/tcp/routing/rules-and-priority/)
