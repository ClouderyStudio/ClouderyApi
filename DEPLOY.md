# 推送即部署（GitHub Actions → 1Panel）

推送到 `master` 后自动完成「构建 → 测试 → 发布 → 覆盖服务器应用目录 → 重启容器 → 健康检查」，
不需要手工登录服务器。1Panel 本身没有内置的构建触发机制，这条流水线补上了缺的那一环。

```
git push origin master
      │
      ▼
GitHub Actions (ubuntu-latest)
  dotnet restore / build / test / publish
  清理 appsettings.json、pdb、uploads
      │  scp 覆盖（不删除目标目录其它文件）
      ▼
1Panel 服务器  宿主目录  DEPLOY_TARGET
      │         └─ 挂载进容器 /app
      ▼
docker restart DEPLOY_CONTAINER  →  curl 健康检查
```

---

## 一、服务器侧一次性准备

### 1. 确认运行时是 .NET 10

项目目标框架是 `net10.0`，**用 .NET 8 的运行环境会直接启动失败**（`You must install .NET 10`）。

在 1Panel「容器 → 镜像」里拉取：

```
mcr.microsoft.com/dotnet/aspnet:10.0
```

### 2. 准备宿主应用目录

假设用 `/opt/1panel/apps/clouderyapi/app`（对应下面的 `DEPLOY_TARGET`）：

```bash
mkdir -p /opt/1panel/apps/clouderyapi/app/uploads/avatars
mkdir -p /opt/1panel/apps/clouderyapi/app/uploads/posts
```

把本地的 `appsettings.json` 传一份上去（**该文件被 `.gitignore` 忽略、不进仓库，流水线也不会覆盖它**）：

```bash
scp ClouderyApi/appsettings.json root@<服务器>:/opt/1panel/apps/clouderyapi/app/
```

### 3. 创建容器

在 1Panel「网站 → 运行环境」或「容器 → 创建容器」中，按下表配置：

| 项 | 值 |
| -- | -- |
| 镜像 | `mcr.microsoft.com/dotnet/aspnet:10.0` |
| 挂载 | 宿主 `/opt/1panel/apps/clouderyapi/app` → 容器 `/app` |
| 工作目录 | `/app` |
| 启动命令 | `dotnet ClouderyApi.dll` |
| 环境变量 | `ASPNETCORE_ENVIRONMENT=Production`、`ASPNETCORE_URLS=http://+:8080` |
| 端口映射 | 容器 `8080` → 宿主 `17288`（示例，可自行调整） |
| 重启策略 | `always` |

> 本项目图片处理用的是 ImageSharp（纯托管），**不需要**给容器装 `libgdiplus`。

### 4. 反向代理

1Panel「网站 → 创建网站」→ 反向代理到 `http://127.0.0.1:17288`，再申请证书。
容器只监听 HTTP，TLS 交给 1Panel 的 Nginx。

---

## 二、GitHub 侧一次性准备

### 1. 生成部署专用 SSH 密钥

```bash
ssh-keygen -t ed25519 -C "github-actions-deploy" -f deploy_key -N ""
# 公钥追加到服务器（1Panel → 终端）
cat deploy_key.pub >> ~/.ssh/authorized_keys
```

私钥 `deploy_key` 的**全部内容**（含 `-----BEGIN` / `-----END` 行）填进下面的 Secret。
建议给这个密钥单独开一个受限用户，而不是长期用 root。

> **`-N ""` 不能省。** 带密码短语的私钥会让流水线报
> `ssh: this private key is passphrase protected` → `unable to authenticate, attempted methods [none]`。
> 要么生成时留空密码短语（推荐），要么把密码短语填进 `DEPLOY_PASSPHRASE`。

### 2. 配置 Secrets

仓库 → **Settings → Secrets and variables → Actions → New repository secret**：

| Secret | 必填 | 说明 | 示例 |
| ------ | :--: | ---- | ---- |
| `DEPLOY_HOST` | ✅ | 服务器 IP 或域名 | `203.0.113.10` |
| `DEPLOY_PORT` | ⬜ | SSH 端口，留空按 22 | `22` |
| `DEPLOY_USER` | ✅ | SSH 用户名 | `root` |
| `DEPLOY_SSH_KEY` | ✅ | 部署私钥全文 | `-----BEGIN OPENSSH PRIVATE KEY-----...` |
| `DEPLOY_PASSPHRASE` | ⬜ | 私钥的密码短语；**推荐用无密码短语的部署密钥并留空** | （留空） |
| `DEPLOY_TARGET` | ✅ | 宿主应用目录（容器挂载源），**必须绝对路径** | `/opt/1panel/apps/clouderyapi/app` |
| `DEPLOY_CONTAINER` | ✅ | 容器名 | `clouderyapi` |
| `DEPLOY_HEALTH_URL` | ⬜ | 健康检查地址，留空则跳过 | `https://api.example.com/mhop/health` |

---

## 三、日常使用

- **部署**：`git push origin master`，进度见 **Actions → Deploy to 1Panel**。
- **手动触发**：Actions → Deploy to 1Panel → Run workflow。
- **回滚**：本地 `git revert <坏提交>` 后推送即可（最简单、且留痕）；
  或建一个指向旧提交的分支，用 Run workflow 选择该分支运行。

流水线做了这些保护：

- `concurrency` 保证同一分支不会并发部署，避免两次推送互相覆盖文件。
- 上传时 `rm: false` —— **不删除目标目录中的其它文件**，所以 `appsettings.json` 和 `uploads/` 不会丢。
- 上传前显式删除 `publish/appsettings.json`，杜绝用仓库里的配置覆盖线上配置。
- 重启后校验 `docker inspect ... .State.Running`，没起来就打印最近 100 行日志并以失败结束。
- 配置了 `DEPLOY_HEALTH_URL` 时会 `curl` 一次，确认服务真的可用。

---

## 四、注意事项

- **目标框架必须匹配运行时**：`net10.0` ↔ `aspnet:10.0`。
- **首次部署**：目录里还没有 `ClouderyApi.dll` 时容器起不来属正常，第一次 push 后即恢复。
- **上传文件持久化**：`Mhop:UploadDir` 默认是「内容根 /uploads」即 `/app/uploads`，落在挂载目录里，容器重启不丢。
  若改用 OSS，见 README 的图片存储章节。
- **HTTPS 重定向**：`Program.cs` 里有 `UseHttpsRedirection()`；容器只监听 HTTP 且未设置
  `ASPNETCORE_HTTPS_PORT` 时它会自动跳过（启动日志里有一条 warning），这是预期行为，TLS 由 Nginx 终止。
- **传输体积约 80MB**：其中 `ClouderyApi.dll` 约 22MB（Costura 内嵌依赖），另有约 15MB 设计期程序集
  （Roslyn / EF Core Design）。想瘦身可以给 `Microsoft.VisualStudio.Web.CodeGeneration.Design` 补上
  `PrivateAssets="all"`，但不是必须的。

---

## 五、相关文件

| 文件 | 作用 |
| ---- | ---- |
| `.github/workflows/deploy.yml` | 推送 `master` 时的构建 + 部署流水线 |
| `.github/workflows/dotnet.yml` | PR 检查（已改为仅 PR 触发，避免与部署流水线重复构建） |
| `ClouderyApi/appsettings.example.json` | 线上 `appsettings.json` 的字段参考 |
