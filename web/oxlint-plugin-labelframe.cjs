// oxlint 自定义插件（迭代 108 · #241，决策 #164 ⑦「CI 防线」）：labelframe/no-bare-cjk-jsx——
// 拦截圈禁文件内的裸中文 JSX 文案（JSX 文本节点 + JSX 属性字符串字面量含 CJK 字符即报错），
// 已迁移 i18n 的文件不得再新增硬编码中文，新文案一律走 t()。
//
// 圈禁文件清单在 .oxlintrc.json 的 overrides.files 显式维护（本规则只对清单内文件生效）——
// 页面迁移迭代（111/112）迁移一个文件就往清单加一个，逐步圈入。
//
// 边界（有意为之）：
// - 不查注释 / 普通字符串（运行时消息、日志等非 JSX 文案归后续迭代）；
// - 不查 JSX 属性内的模板字符串 / 表达式（迁移到时再评估扩展）；
// - jsPlugins 能力为 oxlint alpha 特性，版本升级如失效须第一时间修复本防线。
// - 文件用 .cjs：web/package.json 为 ESM（"type": "module"），CommonJS 插件须显式后缀。

/** CJK 统一汉字 + 扩展 A + 兼容区（足以覆盖 UI 文案场景；全角标点单独出现不视为文案）。 */
const CJK = /[\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF]/

module.exports = {
  meta: { name: 'labelframe' },
  rules: {
    'no-bare-cjk-jsx': {
      meta: {
        type: 'problem',
        docs: { description: 'disallow bare CJK text in JSX of i18n-migrated files (use t() instead)' },
      },
      create(context) {
        const report = (node, text) => {
          context.report({
            node,
            message: '已迁移 i18n 的文件内禁止裸中文 JSX 文案（「{{text}}」）——请改用 t(\'<域>.<key>\') 并补齐 zh-CN / en 词条',
            data: { text: String(text).trim().slice(0, 40) },
          })
        }
        return {
          JSXText(node) {
            if (CJK.test(node.value)) report(node, node.value)
          },
          JSXAttribute(node) {
            const v = node.value
            if (v && v.type === 'Literal' && typeof v.value === 'string' && CJK.test(v.value)) {
              report(node, v.value)
            }
          },
        }
      },
    },
  },
}
