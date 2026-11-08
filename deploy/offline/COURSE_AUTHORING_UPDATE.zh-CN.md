# 课程编排版离线更新（2026-11-08）

适用于已运行的离线版平台，尤其是 2026-10-28 Traefik + Logo 版本。包内包括此前课节编辑、四层技能结构、GZCTF/CTFd 导入、历史积分与排行榜管理、图表和旧前端资源恢复修复，以及本次课程编排与统一发布。

## 更新已有业务机

在业务机 `192.168.1.120` 执行。先暂停训练平台对外访问并告知用户维护；原 CTFd、比赛入口与题目镜像无需停止。以下命令不适用于首次安装。

```bash
mkdir -p /root/nynuctf/updates/course-authoring-20261108
cd /root/nynuctf/updates/course-authoring-20261108
curl -fL --retry 3 -O https://github.com/hougangbei/GZCTF/releases/download/intranet-course-authoring-20261108/gzctf-course-authoring-amd64-20261108.tar.gz
curl -fL --retry 3 -O https://github.com/hougangbei/GZCTF/releases/download/intranet-course-authoring-20261108/gzctf-course-authoring-amd64-20261108.tar.gz.sha256
sha256sum -c gzctf-course-authoring-amd64-20261108.tar.gz.sha256
tar -xzf gzctf-course-authoring-amd64-20261108.tar.gz
python3 manage.py --root /root/nynuctf/runtime update --bundle "$PWD" --maintenance-confirmed
python3 /root/nynuctf/runtime/manage.py --root /root/nynuctf/runtime status
```

如业务机无法下载，在其他电脑下载压缩包及校验文件后传到上述目录，从 `sha256sum` 开始执行。不要使用 `install` 代替 `update`，不要删除数据卷，也不需要重跑 `configure-traefik.py`。包内网关镜像与此前一致，更新器仅更新应用，保留 PostgreSQL/Redis 镜像、密码、邮件/Harbor 配置、端口池、SSH 通道挂载和已有站点覆盖配置。

更新器在停写后校验并备份数据库、文件、Redis 和部署配置，再启动新版并执行健康检查；失败会尝试恢复匹配的完整备份。备份在 `/root/nynuctf/runtime/backups/`，请保留，并确认磁盘空间充足。中断后若有 `recovery-required.json`，按包内恢复文档处理，不要手动绕过。

## 验收

1. 管理员和原学员可登录；平台设置、Logo、账号、排名与附件保留。
2. 打开原技能树，确认旧内容可读；管理端进入“课程编排”，修改后保存草稿，学员仍看到旧版；检查并统一发布后才切换。
3. 启动一道测试题，确认仍返回既有入口及 33000–33199 池内端口，浏览器/TCP 可达。
4. 确认排行榜、课节阅读、题目提交正常后恢复平台入口。

本次有数据库迁移。**不能只切回旧应用镜像**；回退需要恢复同一次备份中的数据库、文件、Redis 和配置。详细命令见 `OFFLINE-RECOVERY.zh-CN.md`。

CTFd ZIP 导入与 80 个题目镜像迁移是单独操作：更新平台不会自动导入真实账号、比赛、历史成绩或题目镜像，也不会修改原 CTFd。新导入仅在管理员确认导入方案后执行。
