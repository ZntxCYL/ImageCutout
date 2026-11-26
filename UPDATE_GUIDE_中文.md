# 签名热更新与国内镜像

## 0. GitHub Actions 自动发布（推荐）

普通构建不需要任何密钥。先上传源码到公开 GitHub 仓库，在 Actions 中手动构建并测试。私有仓库也能构建，但客户端不能匿名读取其 Release；必须配置公开可访问的 HTTPS 镜像才能作为正式更新源。

正式发布只需一次性配置：在 Windows 安装 Git for Windows 后用 Git Bash 执行下面第 1 节的 OpenSSL 命令（也可用其他可信 OpenSSL 环境）。妥善离线保存私钥备份，不要将其提交到 Git 仓库。在仓库 Settings → Secrets and variables → Actions 中配置：

| 类型 | 名称 | 值 |
|---|---|---|
| Variable | `UPDATE_PUBLIC_KEY` | public.pem 完整内容（含 BEGIN/END） |
| Secret | `UPDATE_SIGNING_PRIVATE_KEY` | private.pem 完整内容（含 BEGIN/END） |
| Variable（可选） | `UPDATE_MIRRORS` | 国内软件 HTTPS 镜像根 URL，每行一个 |
| Variable（可选） | `MODEL_MIRRORS` | 国内模型 HTTPS 根 URL，每行一个 |
| Variable（可选） | `MODEL_SHA256` | 每行一个 `模型ID=64位SHA256` |

签名作业使用名为 `release` 的 GitHub Environment；可以在 Settings → Environments 创建并设置允许发布的版本标签、审批保护（视仓库方案支持情况）。私钥也可以放在该环境的 Secret 中，而非仓库 Secret。建议保护 `v*` 标签，只允许可信维护者创建，并审核 workflow/script 变更。构建作业不接触私钥，仅正式签名步骤读取 Secret。若不配置公开密钥，普通构建默认不启用更新；版本标签发布则明确失败。若仓库是公开的，自动使用当前仓库作为 GitHub 备用源。配置镜像不等于已经部署镜像。首次构建前设置好变量，后续客户端才会信任发布签名并知道镜像位置。

测试完毕后，在仓库根目录（当前版本为 0.1.1）执行：

```bash
git tag v0.1.1
git push origin v0.1.1
```

Actions 会自动检查、在 Windows Runner 编译、下载产物到独立发布作业，使用 Secret 签名，并创建正式 GitHub Release。该版本的 Release 不能覆盖。下一版先修改两个 csproj 的 Version、FileVersion，再提交并推送相应标签。手动 Run workflow 只构建，不创建 Release。若某次发布失败，先检查日志和是否已经创建了 Release，不能覆盖已被客户端使用的版本。

国内镜像：正式发布后在对应的 Actions 运行下载 `ImageCutout-domestic-mirror-v0.1.1` Artifact。将其版本目录与 latest 目录按第 3 节的顺序上传至自己的 OSS/COS/CDN 或其他 HTTPS 静态存储；无需 Debian 或常驻服务器。当前没有用户云存储账号或凭据，镜像同步是手动步骤，不会自动上传。可后续按实际存储商增加 CI 同步。Actions Artifact 有保留期限，不是长期更新源。GitHub Release 与镜像应永久保留对应版本文件。

## 1. 生成签名密钥（只需一次）

```bash
mkdir -p "$HOME/imagecutout-signing"
chmod 700 "$HOME/imagecutout-signing"
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out "$HOME/imagecutout-signing/private.pem"
chmod 600 "$HOME/imagecutout-signing/private.pem"
openssl pkey -in "$HOME/imagecutout-signing/private.pem" -pubout -out "$HOME/imagecutout-signing/public.pem"
```

私钥必须离线妥善备份，不能上传到 Git 仓库或镜像。不要每次发布重新生成密钥。更新器不会以管理员身份运行。

## 2. 手动配置源并构建（备用，CI 用户可跳过）

以下域名及仓库是占位示例，需替换为你自己的真实地址。国内推荐有中国大陆访问能力的 HTTPS 对象存储/CDN、自有服务器，遵守服务商及当地要求；未部署前不保证可用性。可仅配置国内镜像，不需要 GitHub 仓库。`--mirror` 可重复，按顺序检查；GitHub 作为最后备选。

```bash
python3 scripts/configure-updates.py \
  --repo YOUR_OWNER/YOUR_REPO \
  --mirror https://YOUR-DOMAIN/imagecutout/ \
  --model-mirror https://YOUR-DOMAIN/imagecutout-models/ \
  --public-key "$HOME/imagecutout-signing/public.pem"
./build-linux.sh  # 可替换为 Windows PowerShell: ./build-windows.ps1
python3 scripts/make-update-release.py --version 0.1.1 \
  --private-key "$HOME/imagecutout-signing/private.pem"
```

不使用 GitHub 时删除 `--repo` 那一行。工具从已签名清单判断版本，不依赖 GitHub API。国内更新配置不是公共 GitHub 代理。

## 3. 发布目录结构

`dist/release/` 输出：

```text
v0.1.1/
  ImageCutout-win-x64-Bundled.zip
  update-manifest.json
  update-manifest.sig
latest/
  update-manifest.json
  update-manifest.sig
```

将整个 release 树同步到镜像根目录；先上传不可变的版本目录并验证下载，最后发布 latest 下的一对文件，尽可能原子替换；CDN 对 latest 不缓存或短缓存，更新后刷新。不要覆盖已发布版本。签名清单与包不一致时客户端拒绝更新。只使用 GitHub 时，在公开仓库创建正式 Release，标签 `v0.1.1`，将版本目录的三个文件作为附件上传。若两个渠道同时使用，两边必须发布同一签名清单及 ZIP。

下一版请同时修改两个 csproj 的 Version 和 FileVersion（如 0.1.2 和 0.1.2.0），构建，再以 `--version 0.1.2` 签名发布。启动自动检查，用户确认后下载。RSA-SHA256 验签、长度和 SHA256 验证后才交给独立更新器。更新器再次验证 SHA256、安全解压、等待旧进程退出、切换应用目录；新程序窗口启动写健康标记，失败尝试回滚。健康标记不是完整推理自检；断电恢复/多进程并行更新尚未验证。模型缓存位于安装目录外，不随更新替换。

## 4. 模型国内下载

镜像目录中放置与 rembg 上游完全一致的模型文件（不要重命名）：

- `BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx`（Lite）
- `BiRefNet-general-epoch_244.onnx`（General）

上游地址为 `https://github.com/danielgatis/rembg/releases/download/v0.0.0/` 加文件名。默认校验使用 rembg 公布的固定 MD5，仅用于兼容和损坏检测，MD5 不等同于安全签名。正式部署应从可信上游获取并独立核对权重，计算 SHA256 (`sha256sum FILE`)，在配置命令中分别追加 `--model-sha256 birefnet-general-lite=实际64位哈希` 和 `--model-sha256 birefnet-general=实际64位哈希`，重新构建。模型 CDN 与软件 CDN 可以分开。缓存文件损坏时，通过导入正确文件覆盖，或手动删除缓存后重新下载。

## 5. 验收清单（待真实环境验证）

- GitHub Actions Windows publish 无报错、ZIP 中包含所有原生 DLL，Windows 启动无异常。
- 通过已下载的 rembg 模型导入，Lite/General 分别处理同一套真实素材，和 rembg 输出比对边缘、透明度及耗时。
- CPU 与 GPU 各测试 1 张和 20 张；取消、失败继续、不覆盖原图、重名和大图测试。
- 国内网络禁用 GitHub 后测试模型和软件更新；镜像不可用测试备用源；篡改 ZIP、签名、版本测试拒绝。
- 在专用测试目录使用测试签名密钥发布 0.1.2，验证升级、无健康标记时回滚。不要直接对工作机正式安装做破坏性测试。
