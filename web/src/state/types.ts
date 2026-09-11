// 应用级共享类型（独立文件避免 App ↔ 页面循环导入）
// 迭代 22：TabId 增加 'packages'（Server UI 安装包分发页，迭代 59 起为统一「下载中心」）；迭代 23：增加 'plugin-packages'（Server UI「插件管理」页）。

export type TabId = 'workbench' | 'designer' | 'data' | 'devices' | 'jobs' | 'logs' | 'settings' | 'packages' | 'plugin-packages'

export interface DesignerRequest {
  kind: 'new' | 'edit'
  name?: string
}
