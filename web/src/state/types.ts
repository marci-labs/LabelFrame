// 应用级共享类型（独立文件避免 App ↔ 页面循环导入）
// 迭代 22：TabId 增加 'packages'（Server UI「客户端下载」页）。

export type TabId = 'workbench' | 'designer' | 'data' | 'devices' | 'jobs' | 'logs' | 'settings' | 'packages'

export interface DesignerRequest {
  kind: 'new' | 'edit'
  name?: string
}
