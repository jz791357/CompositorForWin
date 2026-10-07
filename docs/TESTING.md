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
       │                        │                  （纯文档变更 → 快速通道，跳过构建）
       │                        │                        │
       └────── pre-push 钩子（推送前强制执行本机可跑的全部检查）┘
       └────── pre-push 钩子（直推 main 仅限文档：代码直推被拦截）┘
                                                        │
                                          main 分支保护：代码必须走 CI 通过的 PR
                                          文档变更可直接推送（管理员放行 + 钩子核对）
```

| 层 | 在哪运行 | 检查内容 | 失败后果 |
|---|---|---|---|
| 1. 本机快速门禁 | macOS 开发机 | `scripts/test.sh`：C 内核 clang 编译检查、脚本语法 | `git push` 被阻止 |
| 2. 本机全量门禁 | Windows 构建机 | `scripts/test.ps1`：native 构建 + xUnit 全量（可选覆盖率） | `git push` 被阻止 |
| 3. CI 验证 | GitHub Actions | 与全量门禁相同 + 发布产物 + TRX/覆盖率报告上传；纯文档变更走快速通道（秒级绿） | check 失败，PR 无法合入 |
| 4. main 分支保护 | GitHub 服务端 + pre-push 钩子 | 代码：必须 PR 且 `build-test` 通过；文档（`*.md`、`docs/`、`LICENSE`）：核对无误后可直接推送 | 未经测试的代码进不了 `main` |

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

## 推送流程

### 代码类改动（`src/`、`scripts/`、`.github/`、解决方案文件）——必须走 PR + CI

每个里程碑 / 功能节点收尾时按此清单执行：

1. **更新 `docs/PORTING-MAP.md`** 对应文件的状态列（同步流程的核心资产）。
2. **本机门禁**：macOS 上 `./scripts/test.sh`；在 Windows 机器上则直接 `git push`（pre-push 会自动跑全量 `test.ps1`）。
3. **推功能分支并开 PR**：
   ```sh
   git switch -c <topic>
   git add <明确列出文件>            # 避免把未完成改动卷进提交
   git commit -m "<conventional message>"
   git push -u origin <topic>      # pre-push 钩子在此强制执行
   gh pr create --fill
   ```
4. **等 CI 绿**：`gh pr checks --watch`。失败时修复后继续 push 到同一分支，CI 自动重跑。
5. **合入**：`gh pr merge --squash --delete-branch`。main 分支保护要求 `build-test` check 通过才能合并——这一步就是"测试通过才进仓库"的服务端保证。
6. **（每里程碑一次）** 从 CI 产物下载 `Compositor-win-x64`，在 Windows 11 实机冒烟验证可安装包。

### 文档类改动（`*.md`、`docs/`、`LICENSE`）——核对后直接推送

1. 核对内容正确（CI 对纯文档变更只走快速通道，不做构建验证，正确性由人工把关）。
2. `git push origin main`。pre-push 钩子仍会跑本机可移植检查，并确认改动确实只含文档；混入任何代码文件则被拦截，改走 PR 流程。

### 跳过与紧急绕过

- 临时推送 WIP 分支：`git push --no-verify` 或 `COMPOSITOR_SKIP_TESTS=1 git push`（只应作用于**功能分支**；代码进 main 只能经 PR 合入）。
- 紧急修复必须直推代码到 main 时：`COMPOSITOR_ALLOW_DIRECT=1 git push`（本地放行；服务端对管理员放行直推，但 CI 仍会在推送后运行并暴露问题）。事后应尽快补一个小 PR 说明原因。

### main 分支保护（服务端配置，已启用）

等价于在仓库 Settings → Branches 配置，可用以下命令随时恢复：

```sh
cat <<'EOF' | gh api -X PUT repos/jz791357/CompositorForWin/branches/main/protection --input -
{
  "required_status_checks": { "strict": true, "checks": [ { "context": "build-test" } ] },
  "enforce_admins": false,
  "required_pull_request_reviews": { "required_approving_review_count": 0 },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_linear_history": true
}
EOF
```

规则：**代码必须走 PR** 且 `build-test` check 通过且与 main 同步。**对管理员不启用强制的 PR 限制**（`enforce_admins: false`）——这是文档直推通道的前提；"管理员不直推代码"由 pre-push 钩子按改动路径本地拦截（单人开发，0 个批准即可自合）。若日后多人协作，改回 `enforce_admins: true` 并撤销文档直推通道。

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

> 推送策略：代码改动必须走 PR + CI（`build-test` 通过）；文档改动核对后可直接推送 main。
