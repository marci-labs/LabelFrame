// 插件管理页（迭代 23 §2.1/§7.3，Server UI 专用）：服务端 plugin-packages 目录管理——
// 插件包列表（名称 / 版本 / pluginId / 大小 / 修改时间 / 解析状态）+ 上传（multipart）+ 下载（pluginPackageDownloadUrl）+ 删除（确认）。
// invalid 条目红标 + 原因，仍可删除；上传前按 64MB 上限预检（§2.2，与 WinHost 安装同限）。

import { useCallback, useEffect, useState } from 'react'
import { pluginPackageDownloadUrl, pluginPackageSizeError, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { PluginPackageInfo } from '../lib/api/types'
import { Icon } from '../components/Icon'
import { formatSize } from '../lib/download'

/** 修改时间：本地时间 MM-dd HH:mm:ss。 */
function formatTime(iso?: string): string {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  const p = (n: number) => String(n).padStart(2, '0')
  return `${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`
}

export function PluginPackages() {
  const [packages, setPackages] = useState<PluginPackageInfo[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setPackages(await serverApi.listPluginPackages())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '获取插件包列表失败。')
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const upload = async (file: File) => {
    const sizeErr = pluginPackageSizeError(file.size)
    if (sizeErr) {
      setError(`上传失败：${sizeErr}`)
      return
    }
    setUploading(true)
    setNotice(null)
    setError(null)
    try {
      await serverApi.uploadPluginPackage(file)
      setNotice(`插件包「${file.name}」已上传，客户端可在「设置 → 插件管理」中安装。`)
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '上传失败。')
    } finally {
      setUploading(false)
    }
  }

  const remove = async (p: PluginPackageInfo) => {
    if (!window.confirm(`确认删除插件包「${p.fileName}」？删除后客户端将无法再从服务端安装该插件。`)) return
    setDeleting(p.fileName)
    setError(null)
    setNotice(null)
    try {
      await serverApi.deletePluginPackage(p.fileName)
      setNotice(`插件包「${p.fileName}」已删除。`)
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '删除失败。')
    } finally {
      setDeleting(null)
    }
  }

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          插件管理
          <small>服务端统一分发传输插件包（上传 / 下载 / 删除）</small>
        </div>
        <div className="spacer" />
        <button className="btn" onClick={() => document.getElementById('pluginPkgFile')?.click()} disabled={uploading}>
          <Icon name="upload" size={13} />
          {uploading ? '上传中…' : '上传插件包'}
        </button>
        <input
          id="pluginPkgFile"
          type="file"
          accept=".lfplugin,.zip"
          style={{ display: 'none' }}
          onChange={(ev) => {
            const f = ev.target.files?.[0]
            if (f) void upload(f)
            ev.target.value = ''
          }}
        />
        <button className="btn" onClick={() => void load()} title="重新拉取插件包列表">
          <Icon name="refresh" size={13} />
          刷新
        </button>
      </div>

      {error && (
        <div style={{ padding: '6px 16px', background: 'var(--danger-soft)', color: 'var(--danger)', fontSize: 12 }}>{error}</div>
      )}
      {notice && (
        <div style={{ padding: '6px 16px', background: 'var(--accent-soft)', color: 'var(--accent)', fontSize: 12 }}>{notice}</div>
      )}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {packages === null ? (
          <div className="empty">
            <Icon name="puzzle" />
            <div className="empty-title">正在加载插件包列表…</div>
          </div>
        ) : packages.length === 0 ? (
          <div className="empty">
            <Icon name="puzzle" />
            <div className="empty-title">暂无插件包</div>
            <div className="hint">
              点击右上角「上传插件包」上传 .lfplugin 插件包（zip 格式：根 manifest.json + 插件 DLL），或直接将文件放入服务端数据目录
              <span className="mono" style={{ margin: '0 4px' }}>
                plugin-packages
              </span>
              （重启后自动列出）。
              <br />
              上传后客户端可在「设置 → 插件管理」中安装。
            </div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>插件</th>
                <th style={{ width: 90 }}>版本</th>
                <th style={{ width: 120 }}>pluginId</th>
                <th style={{ width: 100 }}>大小</th>
                <th style={{ width: 150 }}>修改时间</th>
                <th style={{ width: 100 }}>状态</th>
                <th style={{ width: 170 }}></th>
              </tr>
            </thead>
            <tbody>
              {packages.map((p) => (
                <tr key={p.fileName} style={{ cursor: 'default' }}>
                  <td style={{ maxWidth: 260 }}>
                    <div className="mono" style={{ fontSize: 12, wordBreak: 'break-all' }}>
                      {p.name || '—'}
                    </div>
                    <div className="mono" style={{ fontSize: 11, opacity: 0.7, wordBreak: 'break-all' }}>
                      {p.fileName}
                    </div>
                    {!p.valid && p.invalidReason && (
                      <div style={{ fontSize: 11, color: 'var(--danger)', marginTop: 2 }}>{p.invalidReason}</div>
                    )}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {p.version || '—'}
                  </td>
                  <td className="mono" style={{ fontSize: 12, wordBreak: 'break-all' }}>
                    {p.pluginId || '—'}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {formatSize(p.sizeBytes)}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {formatTime(p.modifiedAt)}
                  </td>
                  <td>{p.valid ? <span className="badge ok">正常</span> : <span className="badge err">解析失败</span>}</td>
                  <td>
                    <div style={{ display: 'flex', gap: 6 }}>
                      <a className="btn sm" href={pluginPackageDownloadUrl(p.fileName)} title={`下载 ${p.fileName}`}>
                        <Icon name="download" size={12} />
                        下载
                      </a>
                      <button
                        className="btn sm danger"
                        onClick={() => void remove(p)}
                        disabled={deleting === p.fileName}
                        title="删除该插件包（客户端将无法再安装）"
                      >
                        <Icon name="trash" size={12} />
                        {deleting === p.fileName ? '删除中…' : '删除'}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}