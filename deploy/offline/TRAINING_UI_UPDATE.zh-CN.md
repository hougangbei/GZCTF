# 20261027 技能树与题目弹窗更新

本包用于已有 `~/nynuctf/runtime` 实例，兼容 Ubuntu 20.04 x86_64、Docker 24、Compose 2.18.1、Python 3.8+。包内仅携带新的 amd64 应用镜像；PostgreSQL/Redis 内容指纹与上一版保持一致，需要旧版的引擎镜像仍在本机。

## 更新内容

- 实例剩余时间、到期状态同步、地址复制和 Web 入口；TCP 题保留客户端连接方式。
- 技能树、类别、题目/课时使用紧凑卡片网格；题目弹窗使用中性配色，保留 Flag、官方题解和社区 WP，去除重复快捷入口。
- 图标扩充到 40 个，支持搜索和选中预览；包含 `20261027030000_ExpandSkillTreeIcons` 数据库约束迁移。
- 代理模式返回容器 ID 时不生成无效的网页入口。

## 下载与上传

Release：<https://github.com/hougangbei/GZCTF/releases/tag/intranet-training-ui-20261027>

下载两个文件，通过 MobaXterm SFTP 上传至业务机 `/root/nynuctf/`：

- `gzctf-training-ui-amd64-20261027.tar.gz`
- `gzctf-training-ui-amd64-20261027.tar.gz.sha256`

不要使用 GitHub 自动生成的 Source code 压缩包代替镜像包。

## 在业务机执行

选择维护时段，暂停学员操作，确认没有其他进程向本实例写入。维护确认参数不会自动切换外部维护页面。本次更新会短暂停止应用，备份数据库、附件、Redis 和私有配置，然后执行数据库迁移。

```sh
bash <<'UPDATE'
set -euo pipefail
cd /root/nynuctf
sha256sum -c gzctf-training-ui-amd64-20261027.tar.gz.sha256
mkdir -p updates/training-ui-20261027
tar -xzf gzctf-training-ui-amd64-20261027.tar.gz -C updates/training-ui-20261027
cd updates/training-ui-20261027
python3 manage.py --root /root/nynuctf/runtime status
python3 manage.py --root /root/nynuctf/runtime update --bundle "$PWD" --maintenance-confirmed
python3 /root/nynuctf/runtime/manage.py --root /root/nynuctf/runtime status
UPDATE
```

完成后仍使用 `http://192.168.1.120:18080`，核对登录、技能树/类别卡片、题目倒计时、复制和打开题目、延长/停止及 Flag 提交。浏览器如仍显示旧样式，使用 Ctrl+F5 强制刷新。

这套命令保留现有账号、配置、密钥、数据卷、题目机连接配置和端口，不重新执行 install，也不部署公网 FRP。题目机当前的 SSH Docker socket 转发仍需保持运行。

## 失败恢复

工具会在候选版本启动失败时尝试从更新前快照恢复数据库、附件、Redis 和配置。出现 `Recovery required: True` 时，保持维护并保留完整日志、备份和旧镜像。不要删除状态标记，也不要直接将旧镜像连回已迁移的数据库。详细人工恢复步骤见 [离线部署说明](README.md#人工恢复)。

本地测试使用 ARM64 Mac 上的 amd64 模拟；原生内网环境仍需按上述清单验收。
