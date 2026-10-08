// 应用级共享类型（独立文件避免 App ↔ 页面循环导入）
// 迭代 22：TabId 增加 'packages'（Server UI 安装包分发页，迭代 59 起为统一「下载中心」）；迭代 23：增加 'plugin-packages'（Server UI「插件管理」页）。
// 迭代 75（#112）：移除 'logs'（「PDA 日志 / 设备日志」页下线，未来回传立项时恢复）。
// 迭代 121（#280）：增加 'help'（帮助功能索引页，client 构建专属——SERVER_TABS 不含此 tab 即完成裁剪）。

export type TabId = 'workbench' | 'designer' | 'data' | 'devices' | 'jobs' | 'settings' | 'packages' | 'plugin-packages' | 'help'

export interface DesignerRequest {
  kind: 'new' | 'edit'
  name?: string
}
