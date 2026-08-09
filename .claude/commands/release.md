---
description: 构建并发布 ExtractionRun 到 GitHub Release
argument-hint: <v0.2.0> [--yes] [--draft]
allowed-tools: Bash, Read, Write, Edit, Glob, Grep
---

# 发布 ExtractionRun Release

`$ARGUMENTS` 是目标版本号（`vX.Y.Z`，可带 `-beta/-preview/-rc` 后缀），可选开关 `--yes`（跳过发布前确认）、`--draft`（建草稿不直接发布）；可选透传构建参数 `-ApiRoot <目录>`、`-Versions <0.107.1,0.110.1>`、`-Sts2Path <游戏目录>`。

在本 mod 仓库根（当前工作目录 `gameplay/STS2-ExtractionRun`，独立 git 仓库）执行。发布前工作区应干净（除 `ExtractionRun.json`、`ExtractionRun.csproj`、`CHANGELOG.md` 外无改动）。**一次发版只产生一条 `release: <tag>` 提交**：manifest 与 csproj 版本号、CHANGELOG.md，以及发版期间对脚本/指令的必要修复（提前暂存即可并入）都进这一条。

## 1. 预检

- 版本号必须匹配 `^v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$`，否则报错。
- `gh auth status` 确认已登录；未登录则提示用户先 `gh auth login`。
- `git tag -l <tag>` 无同名本地 tag；`gh release view <tag>` 无同名 Release（报错即视为不存在）。
- 取上一版本号：`git tag --sort=-v:refname` 的第一个（它不含新 tag）；无 tag 则从首个提交开始。
- 计算提交区间 `git log <prev>..HEAD --oneline`。**为空则中止**，说明「没有未发布提交，先提交改动再发版」。

## 2. 生成发布信息

- 按 conventional 前缀分组 `git log <prev>..HEAD --oneline` 的提交：`feat`→功能、`fix`→修复、`perf`→性能、`refactor`→重构、`docs`→文档、`build`/`chore`→杂项；去掉前缀、保留 `(scope)`；无法归类的进「其他」。各组按提交顺序（旧的在上），标题与正文用中文。
- 把分组结果 prepend 到仓库根 `CHANGELOG.md`：文件头部是 `# CHANGELOG` 标题，其下新增 `## <tag> - <当天日期>` 小节（如 `## v0.1.2 - 2026-08-09`），后续所有版本小节都带发布日期；文件不存在则直接新建。
- 同样内容写入 `dist/release-notes-<tag>.md`（`dist/` 已被 gitignore，不入库）。
- 把这段草稿展示给用户，允许其提出修改（正文以用户意见为准）。

## 3. 执行确定性核心

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File tools/publish-release.ps1 -Tag <tag> [-ApiRoot ...] [-Versions ...] [-Sts2Path ...]
```

该脚本会升级 `ExtractionRun.json` 与 `ExtractionRun.csproj` 的版本号、`dotnet publish`（重出 pck）→ `build-variants.ps1`（全部快照）、白名单打包到 `dist/ExtractionRun-<tag>.zip`、把版本号 + CHANGELOG 提交为唯一的一条 `release: <tag>` 并打**本地** tag。它不推送、不碰 GitHub。

从输出 `RELEASE SUMMARY` 读取 `ZIP=`（zip 路径）、`COVERED=`（逗号分隔的兼容版本）。失败即中止，按脚本报错处理，不要绕过。

## 4. 补全发布信息

- 在 `dist/release-notes-<tag>.md` 末尾追加 `### 兼容游戏版本`，列出 `COVERED=` 的版本（如 `0.107.1、0.110.1`）。
- 向用户汇报：tag、zip 路径与大小、覆盖版本、生成的笔记。

## 5. 发布前确认

除非参数含 `--yes`，否则**必须**明确询问用户「推送并发布？」。未经确认不得执行第 6 步。

## 6. 推送 + 发布

- `git push origin <当前分支>`，然后 `git push origin <tag>`。任一步失败先诊断（`git ls-remote origin` 核对远端状态），不盲目重试。
- `gh release create <tag> dist/ExtractionRun-<tag>.zip --notes-file dist/release-notes-<tag>.md`
  - tag 含 `-beta`/`-preview`/`-rc` → 加 `--prerelease`；参数含 `--draft` → 加 `-d`。
- 成功后输出 Release URL 与 zip 资产路径。
