# 自动化测试与推送门禁

> 完整流程：**重要节点（里程碑/功能）完成后，自动化测试全部通过，代码才进入仓库 `main`。**
> 本文是测试与推送流程的唯一参考；测试策略本身（契约测试 / golden 文件 / 性能门禁）见 [PORTING-PLAN.md](PORTING-PLAN.md) §9。

---

## 总览：四层门禁

```
本机写码 (macOS)          Windows 机器              GitHub
─────────────────        ─────────────────        ──────────────────────
scripts/test.sh           scripts/test.ps1          PR → CI (windows-latest)
  clang 编译检查 C 内核      native 构建 + 全量          build-test 作业：
  node 脚本语法检查          xUnit 测试 + TRX           构建 + 测试 + 发布产物
       │                        │                        │
       └────── pre-push 钩子（推送前强制执行本机可跑的全部检查）┘
                                                        │
                                          main 分支保护：只有 CI 通过的 PR 才能合入
```

| 层 | 在哪运行 | 检查内容 | 失败后果 |
|---|---|---|---|
| 1. 本机快速门禁 | macOS 开发机 | `scripts/test.sh`：C 内核 clang 编译检查、脚本语法 | `git push` 被阻止 |
| 2. 本机全量门禁 | Windows 构建机 | `scripts/test.ps1`：native 构建 + xUnit 全量（可选覆盖率） | `git push` 被阻止 |
| 3. CI 验证 | GitHub Actions | 与全量门禁相同 + 发布产物 + TRX/覆盖率报告上传 | check 失败，PR 无法合入 |
| 4. main 分支保护 | GitHub 服务端 | `build-test` check 必须通过；直接 push `main` 被拒绝 | 未经测试的代码进不了 `main` |

工作流按 PORTING-PLAN 的约定：**macOS 写码 + Windows 构建调试 + CI 把关**。macOS 上没有 .NET/WPF 工具链是常态，所以第 1 层只跑可移植部分，完整验证由 Windows 机器与 CI 承担。

## 一次性安装（每个新 clone 执行一次）

```sh
# macOS / Linux
./scripts/hooks/install.sh

# Windows (PowerShell)
.\scripts\hooks\install.ps1
```

安装的是 `core.hooksPath = scripts/hooks` 指向仓库内的 `pre-push` 钩子，钩子随仓库版本化、所有 clone 行为一致。

## 日常命令

```sh
# macOS：可移植门禁（约 2 秒）
./scripts/test.sh

# Windows：全量门禁（native 构建 + 全部测试 + TRX 报告）
.\scripts\test.ps1
.\scripts\test.ps1 -Coverage            # 附带 coverlet 覆盖率

# Windows：测试通过后构建发布产物
.\scripts\build.ps1                     # = test.ps1 + dotnet publish
.\scripts\build.ps1 -SkipTests          # 仅发布（谨慎使用）
```

测试报告落在 `src/Compositor.Tests/TestResults/`（已 gitignore）。

## 重要节点完成后的推送流程

每个里程碑 / 功能节点收尾时按此清单执行：

1. **更新 `docs/PORTING-MAP.md`** 对应文件的状态列（同步流程的核心资产）。
2. **本机门禁**：macOS 上 `./scripts/test.sh`；在 Windows 机器上则直接 `git push`（pre-push 会自动跑全量 `test.ps1`）。
3. **推功能分支并开 PR**：
   ```sh
   git switch -c <topic>
   git commit -am "<conventional message>"
   git push -u origin <topic>      # pre-push 钩子在此强制执行
   gh pr create --fill
   ```
4. **等 CI 绿**：`gh pr checks --watch`。失败时修复后继续 push 到同一分支，CI 自动重跑。
5. **合入**：`gh pr merge --squash --delete-branch`。main 分支保护要求 `build-test` check 通过才能合并——这一步就是"测试通过才进仓库"的服务端保证。
6. **（每里程碑一次）** 从 CI 产物下载 `Compositor-win-x64`，在 Windows 11 实机冒烟验证可安装包。

### 跳过与紧急绕过

- 临时推送 WIP 分支：`git push --no-verify` 或 `COMPOSITOR_SKIP_TESTS=1 git push`（只应作用于**功能分支**；main 只能经 PR 合入，绕不过 CI）。
- 紧急修复必须直推 main 时：临时关闭分支保护（`gh api -X DELETE repos/jz791357/CompositorForWin/branches/main/protection`），推完立即恢复（见下）。

### main 分支保护（服务端配置，已启用）

等价于在仓库 Settings → Branches 配置：

```sh
gh api -X PUT repos/jz791357/CompositorForWin/branches/main/protection -f \
  'required_status_checks[strict]=true' \
  -f 'required_status_checks[checks][]=build-test' \
  -f 'enforce_admins=true' \
  -f 'required_pull_request_reviews[required_approving_review_count]=0' \
  -F restrictions=null
```

规则：必须走 PR；`build-test` check 必须通过且与 main 同步；对管理员同样生效（单人开发，0 个批准即可自合）。

## CI 产物

| 产物 | 内容 | 保留 |
|---|---|---|
| `Compositor-win-x64` | 可运行的应用发布目录（含 Compositor.Native.dll） | 默认（90 天） |
| `test-results` | TRX 测试报告 + cobertura 覆盖率 XML，**失败时也上传** | 14 天 |

手动触发：Actions 页面 → CI → Run workflow，或 `gh workflow run CI`。

## 测试编写约定

- 上游 `CompositorTests/` 的纯逻辑用例（格式、混合模式、蒙版、调整等约 60%）**同名移植**到 `src/Compositor.Tests/`，作为两版行为一致的契约证据。
- 每个里程碑落地功能时同步移植/新增对应测试——没有测试的功能不算完成（M0 的 `NativeKernelTests.cs` 即范例：只断言上游头文件承诺的契约）。
- golden 文件比对与性能门禁按 PORTING-PLAN §9 在 M1 起接入本流程。
