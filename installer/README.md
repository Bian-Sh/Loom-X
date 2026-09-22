# LoomX 安装包

GitHub Actions 在推送 `vX.Y.Z` Tag 后生成 Inno Setup 安装包，并将安装器和 SHA-256 校验文件上传到 GitHub Release。

## 日志

安装器启用了 Inno Setup 的 `SetupLogging`。安装或卸载时可以通过命令行显式指定日志文件：

```powershell
LoomX-1.2.3-setup.exe /LOG="$env:TEMP\LoomX-install.log"
"$env:LOCALAPPDATA\Programs\LoomX\unins000.exe" /LOG="$env:TEMP\LoomX-uninstall.log"
```

未指定 `/LOG` 时，Inno Setup 会按自身规则将日志写入当前用户的临时目录。应用运行期间的业务日志仍写入 `%LOCALAPPDATA%\LoomX\logs`。

卸载不会删除 `%LOCALAPPDATA%\LoomX`，因此配置库、活动库和应用日志会保留，便于升级和故障排查。

## 本地编译

先准备 `artifacts\publish` 目录，再使用 Inno Setup 6 的 `ISCC.exe`：

```powershell
New-Item -ItemType Directory -Force -Path "artifacts/installer" | Out-Null
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
  "/DAppVersion=0.0.0-local" `
  "/DSourceDir=$((Resolve-Path 'artifacts/publish').Path)" `
  "/DOutputDir=$((Resolve-Path 'artifacts/installer').Path)" `
  "installer/LoomX.iss"
```
