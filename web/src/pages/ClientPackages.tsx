// 客户端下载页（迭代 22 §2.3，Server UI 专用）：服务端 client-packages 目录管理——
// 安装包列表（文件名 / 大小 / 修改时间）+ 上传（multipart）+ 下载（{serverBaseUrl}/api/client-packages/{file}）+ 删除（确认）。
// 与客户端设置页「更新与安装包」卡片共用 GET /api/client-packages 与下载 URL。

import { useCallback, useEffect, useState } from 'react'
import { clientPackageDownloadUrl, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { ClientPackageInfo } from '../lib/api/types'
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

export function ClientPackages() {
  const [packages, setPackages] = useState<ClientPackageInfo[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [uploading, setUploading] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      setPackages(await serverApi.listClientPackages())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '获取安装包列表失败。')
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const upload = async (file: File) => {
    setUploading(true)
    setNotice(null)
    setError(null)
    try {
      await serverApi.uploadClientPackage(file)
      setNotice(`安装包「${file.name}」已上传，客户端可在「设置 → 更新与安装包」中下载。`)
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '上传失败。')
    } finally {
      setUploading(false)
    }
  }

  const remove = async (p: ClientPackageInfo) => {
    if (!window.confirm(`确认删除安装包「${p.fileName}」？删除后客户端将无法再从服务端下载该文件。`)) return
    setDeleting(p.fileName)
    setError(null)
    setNotice(null)
    try {
      await serverApi.deleteClientPackage(p.fileName)
      setNotice(`安装包「${p.fileName}」已删除。`)
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
          客户端下载
          <small>服务端统一分发客户端安装包（上传 / 下载 / 删除）</small>
        </div>
        <div className="spacer" />
        <button className="btn" onClick={() => document.getElementById('pkgFile')?.click()} disabled={uploading}>
          <Icon name="upload" size={13} />
          {uploading ? '上传中…' : '上传安装包'}
        </button>
        <input
          id="pkgFile"
          type="file"
          style={{ display: 'none' }}
          onChange={(ev) => {
            const f = ev.target.files?.[0]
            if (f) void upload(f)
            ev.target.value = ''
          }}
        />
        <button className="btn" onClick={() => void load()} title="重新拉取安装包列表">
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
            <Icon name="download" />
            <div className="empty-title">正在加载安装包列表…</div>
          </div>
        ) : packages.length === 0 ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">暂无安装包</div>
            <div className="hint">
              点击右上角「上传安装包」上传客户端安装文件（MSI / zip 等），或直接将文件放入服务端数据目录
              <span className="mono" style={{ margin: '0 4px' }}>
                client-packages
              </span>
              （重启后自动列出）。
              <br />
              上传后客户端可在「设置 → 更新与安装包」中下载。
            </div>
          </div>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>文件名</th>
                <th style={{ width: 110 }}>大小</th>
                <th style={{ width: 150 }}>修改时间</th>
                <th style={{ width: 170 }}></th>
              </tr>
            </thead>
            <tbody>
              {packages.map((p) => (
                <tr key={p.fileName} style={{ cursor: 'default' }}>
                  <td className="mono" style={{ fontSize: 12, wordBreak: 'break-all' }}>
                    {p.fileName}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {formatSize(p.sizeBytes)}
                  </td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {formatTime(p.modifiedAt)}
                  </td>
                  <td>
                    <div style={{ display: 'flex', gap: 6 }}>
                      <a className="btn sm" href={clientPackageDownloadUrl(p.fileName)} title={`下载 ${p.fileName}`}>
                        <Icon name="download" size={12} />
                        下载
                      </a>
                      <button
                        className="btn sm danger"
                        onClick={() => void remove(p)}
                        disabled={deleting === p.fileName}
                        title="删除该安装包（客户端将无法再下载）"
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
