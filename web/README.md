# React + TypeScript + Vite

This template provides a minimal setup to get React working in Vite with HMR and some Oxlint rules.

Currently, two official plugins are available:

- [@vitejs/plugin-react](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react) uses [Oxc](https://oxc.rs)
- [@vitejs/plugin-react-swc](https://github.com/vitejs/vite-plugin-react/blob/main/packages/plugin-react-swc) uses [SWC](https://swc.rs/)

## 双构建模式（迭代 20）

同一前端工程产出两个构建产物（`VITE_UI_MODE` 构建时环境变量，默认 `client`）：

- `pnpm build` —— 客户端界面产物 `web/dist`（由 LabelFrame Client / WinHost 托管，默认模式）。
- `pnpm build:server` —— 服务端管理界面插件产物 `web/dist-server`（放入服务端插件目录
  `%ProgramData%\LabelFrame\server\plugins\web-ui` 即生效，无需重启）。

Windows 下构建 server 产物需先设置环境变量（git-bash / cmd 不识别 PowerShell 语法）：

```powershell
$env:VITE_UI_MODE = 'server'
pnpm build:server
```

或使用 cross-env：`cross-env VITE_UI_MODE=server pnpm build:server`。

`VITE_UI_MODE=server` 构建（Server UI）：API 走同源相对路径（`getServerBaseUrl()` 返回 `''`，
不读 localStorage / 机器级配置）；菜单移除设置页与一切打印机相关内容，新增「在线设备」页；
数据与打印的目标设备改为在线设备选择器（仅在线可选，提交前现拉校验）；无单机降级分支。

dev 联调：vite dev（:5173）的 proxy 按模式分支——server 模式指向 `http://127.0.0.1:53961`（服务端），
client 模式指向 `http://127.0.0.1:53960`（本机 Client），覆盖 `/api` 与 `/healthz`。

## React Compiler

The React Compiler is not enabled on this template because of its impact on dev & build performances. To add it, see [this documentation](https://react.dev/learn/react-compiler/installation).

## Expanding the Oxlint configuration

If you are developing a production application, we recommend enabling type-aware lint rules by installing `oxlint-tsgolint` and editing `.oxlintrc.json`:

```json
{
  "$schema": "./node_modules/oxlint/configuration_schema.json",
  "plugins": ["react", "typescript", "oxc"],
  "options": {
    "typeAware": true
  },
  "rules": {
    "react/rules-of-hooks": "error",
    "react/only-export-components": ["warn", { "allowConstantExport": true }]
  }
}
```

See the [Oxlint rules documentation](https://oxc.rs/docs/guide/usage/linter/rules) for the full list of rules and categories.
