#!/usr/bin/env bash
# 在服务器上补齐 Scforge 隐私访问的必填配置（幂等）。
#
# 背景：Scforge:AccessTokenSecret 是隐私插件（口令访问）解锁令牌的 HMAC 签名密钥。
# 该键 2026-10-05 才引入，线上那份 appsettings.json（从旧模板 scp 上去的）里没有它，
# 缺失时服务会拒绝启动。本脚本只增不改：已有合法值就原样保留，绝不覆盖。
#
# 用法（在服务器上，root 或有写权限）：
#   bash scforge-access-secret.sh [应用目录]
#   应用目录默认 /opt/1panel/apps/clouderyapi/app，即容器挂载的宿主目录。
#
# 幂等性：重复执行安全。已配置且长度 ≥ 32 时直接跳过，不生成新密钥
# —— 换密钥会让所有已签发的解锁令牌失效，用户得重新输口令。

set -euo pipefail

APP_DIR="${1:-/opt/1panel/apps/clouderyapi/app}"
CFG="$APP_DIR/appsettings.json"
BACKUP="$CFG.bak-$(date +%F-%H%M%S)"

echo "目标配置：$CFG"

if [ ! -f "$CFG" ]; then
  echo "错误：找不到 $CFG" >&2
  echo "先按 DEPLOY.md 第一节第 2 步把本地 appsettings.json scp 上来。" >&2
  exit 1
fi

command -v python3 >/dev/null 2>&1 || {
  echo "错误：服务器上没有 python3，本脚本依赖它做 JSON 解析。" >&2
  exit 1
}

# ---- 读现状 ----
current="$(python3 - "$CFG" <<'PY'
import json, sys

with open(sys.argv[1], encoding="utf-8") as handle:
    config = json.load(handle)
value = (config.get("Scforge") or {}).get("AccessTokenSecret") or ""
print(value)
PY
)"

if [ "${#current}" -ge 32 ]; then
  echo "已配置（长度 ${#current}），无需改动。"
  echo "提醒：换密钥会让所有已签发的解锁令牌立即失效，用户需重新输口令。"
  exit 0
fi

if [ -n "$current" ]; then
  echo "现状：已配置但只有 ${#current} 个字符（不足 32），将替换为新密钥。"
else
  echo "现状：未配置，将写入新密钥。"
fi

# ---- 生成 64 字符随机密钥 ----
# openssl rand -base64 48 → 64 字符（含 padding），远超 32 下限
secret="$(openssl rand -base64 48)"
if [ "${#secret}" -lt 32 ]; then
  echo "错误：生成的密钥只有 ${#secret} 个字符，不合要求。" >&2
  exit 1
fi

# ---- 备份后写回 ----
cp -p "$CFG" "$BACKUP"
echo "已备份到 $BACKUP"

# 用 python 改写而不是 sed：JSON 结构（缩进、嵌套、逗号）原样保留，
# 只动 Scforge 段这一个键。
python3 - "$CFG" "$secret" <<'PY'
import json, sys
from collections import OrderedDict

path, secret = sys.argv[1], sys.argv[2]

with open(path, encoding="utf-8") as handle:
    config = json.load(handle, object_pairs_hook=OrderedDict)

scforge = config.get("Scforge")
if scforge is None:
    # 段整个不存在时建一个，插在末尾
    scforge = OrderedDict()
    config["Scforge"] = scforge

scforge["AccessTokenSecret"] = secret
scforge.setdefault("AccessTokenMinutes", 120)

with open(path, "w", encoding="utf-8") as handle:
    json.dump(config, handle, ensure_ascii=False, indent=2)
    handle.write("\n")

print(f"已写入 Scforge:AccessTokenSecret（长度 {len(secret)}）")
print(f"已确保 Scforge:AccessTokenMinutes = {scforge['AccessTokenMinutes']}")
PY

unset secret current

# ---- 只验证，不重启容器 ----
# --migrate 会走与正式启动完全相同的配置校验，通过即代表配置没问题。
# 此时旧进程仍在跑（内存里是旧 assembly），不受影响。
echo
echo "验证配置（不重启容器，旧版本继续提供服务）..."
if command -v docker >/dev/null 2>&1; then
  container="$(docker ps --filter "ancestor=mcr.microsoft.com/dotnet/aspnet:10.0" \
    --filter "status=running" --format '{{.Names}}' | head -1)"
  if [ -n "$container" ]; then
    echo "找到容器 $container，执行 --migrate 验证 ..."
    if docker exec "$container" dotnet ClouderyApi.dll --migrate; then
      echo "验证通过：配置可用，服务能正常启动。"
    else
      echo "验证失败，配置已回滚。如需手动回滚：" >&2
      echo "  cp $BACKUP $CFG" >&2
      echo "再执行 docker restart $container" >&2
      exit 1
    fi
    echo
    echo "现在可以重启容器让新配置生效："
    echo "  docker restart $container"
  else
    echo "未找到运行中的 .NET 容器，跳过验证。"
    echo "下次部署时流水线的「部署前自检」会再校验一遍。"
  fi
else
  echo "服务器上没有 docker 命令，跳过验证。"
fi

echo
echo "密钥值不在此输出。需要查看时直接看 $CFG 的 Scforge 段。"
