# 离线更新备份与恢复

本指南用于既有 `/root/nynuctf/runtime` 部署。数据库迁移后不能仅把 APP_IMAGE 换回旧版；必须恢复同次备份中的数据库、文件、Redis 与配置。恢复会丢弃备份时点之后的新数据，请先确认恢复时点并保留需要的新数据。

## 更新成功后

在业务机执行：

```bash
python3 /root/nynuctf/runtime/manage.py --root /root/nynuctf/runtime status
ls -lt /root/nynuctf/runtime/backups/
```

每次更新前的完整备份保存在 `backups/时间戳/`，包含 `database.dump`、文件、Redis、私密部署配置及 `backup.json`。目录含账号数据和密码，请保持私有权限并做加密异机备份。工具不会自动清理备份；请保留原应用镜像及已有 DB/Redis 镜像。

## 失败或中断

保持训练平台入口维护，不要放行新的真实写入。更新失败时工具会尝试自动恢复；若出现 `recovery-required.json`，不要手工删除或绕过它。先查看该文件中的备份目录和失败原因，并查看应用日志：

```bash
docker logs --tail 100 nynuctf-app
cat /root/nynuctf/runtime/recovery-required.json
```

日志或配置可能有私人信息，不要原样公开。原 CTFd 与其他业务无需停止。

## 显式恢复

将下面的 `BACKUP_DIR` 改成已核对的本实例完整备份目录，不能照抄占位值：

```bash
BACKUP_DIR=/root/nynuctf/runtime/backups/替换为真实备份目录
INSTANCE_ID=$(awk -F= '$1=="INSTANCE_ID" {print $2}' /root/nynuctf/runtime/.env)
python3 /root/nynuctf/runtime/manage.py --root /root/nynuctf/runtime restore --backup "$BACKUP_DIR" --confirm-instance "$INSTANCE_ID" --maintenance-confirmed
python3 /root/nynuctf/runtime/manage.py --root /root/nynuctf/runtime status
```

工具检查备份校验和、实例身份、原数据卷及镜像，不会改用新空卷。恢复重建本实例数据库、还原文件/Redis/配置并启动匹配的旧应用；当前文件目录会另存为 `files-before-restore-*`，这并不保存当前数据库的新写入。若当前运行目录的工具损坏，应先使用可信发行包中的 `manage.py`，保持相同 `--root` 和实例身份。

恢复后确认管理员/学员登录、原课程、附件、排名与题目实例入口，再恢复流量。仍失败时保留备份、恢复标记与日志，不要删除数据卷、强制绕过检查或只回退镜像。
