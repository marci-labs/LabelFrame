// 下载中心页（迭代 59 决策 #119，Server UI 专用）：客户端安装包（client-packages）与 PDA 安装包（pda-packages）
// 同页分区统一分发——列表（文件名 / 大小 / 修改时间，最新在上）+ 上传（multipart）+ 下载 + 删除（确认）；
// 每条目旁展示二维码（内容 = 本页 origin + 该条目下载路径，即管理员正在访问的局域网地址）——PDA 与服务器
// 同网扫码即得下载 URL；PDA 区常驻 Android「未知来源 / 安装未知应用」授权步骤提示。
// client-packages 既有行为不动：客户端设置页「更新与安装包」卡片仍走 GET /api/client-packages 下载。

import { useCallback, useEffect, useState } from 'react'
import qrcode from 'qrcode-generator'
import { clientPackageDownloadUrl, pdaPackageDownloadUrl, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { ClientPackageInfo, PdaPackageInfo } from '../lib/api/types'
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

/** 条目通用展示信息（客户端 / PDA 共用行渲染；时间倒序「最新在上」由本页保证——服务端列表亦按修改时间倒序）。 */
interface PackageRow {
  fileName: string
  sizeBytes: number
  modifiedAt: string
  downloadHref: string
}

/** 二维码图片（qrcode-generator 生成 GIF data URL，无需 canvas；生成失败不渲染，不阻塞页面）。 */
function QrCodeImage({ text, size = 84 }: { text: string; size?: number }) {
  let src = ''
  try {
    const qr = qrcode(0, 'M')
    qr.addData(text)
    qr.make()
    src = qr.createDataURL(3, 6)
  } catch {
    return null
  }
  return (
    <img
      src={src}
      width={size}
      height={size}
      alt="扫码下载二维码"
      title={text}
      style={{ display: 'block', background: '#fff', border: '1px solid var(--border)', borderRadius: 4, imageRendering: 'pixelated' }}
    />
  )
}

/** 分区头：标题 + 说明 + 分区专属上传按钮（隐藏 file input 由按钮触发）。 */
function SectionHead({
  title,
  hint,
  uploading,
  uploadLabel,
  inputId,
  onPick,
}: {
  title: string
  hint: string
  uploading: boolean
  uploadLabel: string
  inputId: string
  onPick: (file: File) => void
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, margin: '0 0 8px' }}>
      <div className="page-title" style={{ fontSize: 15, margin: 0, flex: 1 }}>
        {title}
        <small>{hint}</small>
      </div>
      <button className="btn sm" onClick={() => document.getElementById(inputId)?.click()} disabled={uploading}>
        <Icon name="upload" size={13} />
        {uploading ? '上传中…' : uploadLabel}
      </button>
      <input
        id={inputId}
        type="file"
        accept={inputId === 'pdaPkgFile' ? '.apk' : undefined}
        style={{ display: 'none' }}
        onChange={(ev) => {
          const f = ev.target.files?.[0]
          if (f) onPick(f)
          ev.target.value = ''
        }}
      />
    </div>
  )
}

/** 分区条目表（列：文件名 / 大小 / 修改时间 / 二维码 / 操作）。 */
function PackageTable({
  rows,
  qrOrigin,
  deleting,
  onRemove,
}: {
  rows: PackageRow[]
  /** 二维码前缀（本页 origin）：二维码内容 = origin + 下载相对路径 = 局域网可访问的完整 URL。 */
  qrOrigin: string
  deleting: string | null
  onRemove: (row: PackageRow) => void
}) {
  return (
    <table className="table">
      <thead>
        <tr>
          <th>文件名</th>
          <th style={{ width: 100 }}>大小</th>
          <th style={{ width: 150 }}>修改时间</th>
          <th style={{ width: 104 }}>二维码</th>
          <th style={{ width: 170 }}></th>
        </tr>
      </thead>
      <tbody>
        {rows.map((p) => (
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
              <QrCodeImage text={`${qrOrigin}${p.downloadHref}`} />
            </td>
            <td>
              <div style={{ display: 'flex', gap: 6 }}>
                <a className="btn sm" href={p.downloadHref} title={`下载 ${p.fileName}`}>
                  <Icon name="download" size={12} />
                  下载
                </a>
                <button
                  className="btn sm danger"
                  onClick={() => onRemove(p)}
                  disabled={deleting === p.fileName}
                  title="删除该安装包（扫码与链接下载将失效）"
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
  )
}

export function DownloadCenter() {
  const [clientPackages, setClientPackages] = useState<ClientPackageInfo[] | null>(null)
  const [pdaPackages, setPdaPackages] = useState<PdaPackageInfo[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [uploadingClient, setUploadingClient] = useState(false)
  const [uploadingPda, setUploadingPda] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      const [clients, pdas] = await Promise.all([serverApi.listClientPackages(), serverApi.listPdaPackages()])
      setClientPackages(clients)
      setPdaPackages(pdas)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '获取安装包列表失败。')
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  const uploadClient = async (file: File) => {
    setUploadingClient(true)
    setNotice(null)
    setError(null)
    try {
      await serverApi.uploadClientPackage(file)
      setNotice(`安装包「${file.name}」已上传，客户端可在「设置 → 更新与安装包」中下载。`)
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '上传失败。')
    } finally {
      setUploadingClient(false)
    }
  }

  const uploadPda = async (file: File) => {
    setUploadingPda(true)
    setNotice(null)
    setError(null)
    try {
      await serverApi.uploadPdaPackage(file)
      setNotice(`APK「${file.name}」已上传，PDA 可在本页扫二维码下载安装。`)
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '上传失败。')
    } finally {
      setUploadingPda(false)
    }
  }

  const removeClient = async (p: PackageRow) => {
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

  const removePda = async (p: PackageRow) => {
    if (!window.confirm(`确认删除 APK「${p.fileName}」？删除后 PDA 扫码将无法再下载该文件。`)) return
    setDeleting(p.fileName)
    setError(null)
    setNotice(null)
    try {
      await serverApi.deletePdaPackage(p.fileName)
      setNotice(`APK「${p.fileName}」已删除。`)
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : '删除失败。')
    } finally {
      setDeleting(null)
    }
  }

  // 时间排序（决议 3：最新在上；页面内排序先行，latest.json 落地后再对齐「推荐 / 最新」标记）
  const byNewest = <T extends { modifiedAt?: string }>(list: T[] | null): T[] | null =>
    list === null ? null : [...list].sort((a, b) => (b.modifiedAt ?? '').localeCompare(a.modifiedAt ?? ''))

  const clientRows: PackageRow[] | null =
    byNewest(clientPackages)?.map((p) => ({
      fileName: p.fileName,
      sizeBytes: p.sizeBytes,
      modifiedAt: p.modifiedAt,
      downloadHref: clientPackageDownloadUrl(p.fileName),
    })) ?? null
  const pdaRows: PackageRow[] | null =
    byNewest(pdaPackages)?.map((p) => ({
      fileName: p.fileName,
      sizeBytes: p.sizeBytes,
      modifiedAt: p.modifiedAt,
      downloadHref: pdaPackageDownloadUrl(p.fileName),
    })) ?? null

  // 二维码前缀 = 管理员浏览器正在访问的地址（PDA 与服务器同网即可直达）
  const qrOrigin = typeof window !== 'undefined' ? window.location.origin : ''

  return (
    <div className="page">
      <div className="page-head">
        <div className="page-title">
          下载中心
          <small>客户端与 PDA 安装包统一分发（上传 / 下载 / 删除 / 扫码）</small>
        </div>
        <div className="spacer" />
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
        {/* ── 客户端下载（PC；client-packages 既有数据并入展示，行为不动）── */}
        <SectionHead
          title="客户端下载（PC）"
          hint="打印电脑的安装包（MSI / zip）——客户端也可在「设置 → 更新与安装包」中下载"
          uploading={uploadingClient}
          uploadLabel="上传客户端安装包"
          inputId="clientPkgFile"
          onPick={(f) => void uploadClient(f)}
        />
        {clientRows === null ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">正在加载客户端安装包…</div>
          </div>
        ) : clientRows.length === 0 ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">暂无客户端安装包</div>
            <div className="hint">
              点击「上传客户端安装包」上传安装文件（MSI / zip 等），或直接将文件放入服务端数据目录
              <span className="mono" style={{ margin: '0 4px' }}>
                client-packages
              </span>
              （重启后自动列出）。
            </div>
          </div>
        ) : (
          <PackageTable rows={clientRows} qrOrigin={qrOrigin} deleting={deleting} onRemove={removeClient} />
        )}

        {/* ── PDA 下载（Android 宿主 APK；pda-packages 新目录，迭代 59 决策 #119）── */}
        <div style={{ height: 16 }} />
        <SectionHead
          title="PDA 下载（Android）"
          hint="PDA 宿主安装包（APK）——扫条目旁二维码即可在 PDA 浏览器下载"
          uploading={uploadingPda}
          uploadLabel="上传 APK"
          inputId="pdaPkgFile"
          onPick={(f) => void uploadPda(f)}
        />
        <div
          style={{
            padding: '8px 12px',
            marginBottom: 8,
            background: 'var(--accent-soft)',
            color: 'var(--accent)',
            fontSize: 12,
            lineHeight: 1.7,
            borderRadius: 4,
          }}
        >
          <Icon name="alert" size={12} style={{ marginRight: 4, verticalAlign: '-2px' }} />
          PDA 安装提示：PDA 与服务器连同一局域网，用相机 / 扫码工具扫条目旁二维码即可下载。首次安装若提示「未知来源」或「禁止安装」，需一次性授权：系统设置 → 应用 → 特殊权限（安装未知应用）→ 找到浏览器 → 允许安装未知应用；各品牌入口略有差异（也可长按浏览器图标 → 应用信息 → 安装未知应用）。升级请使用同一服务器分发的 APK（签名一致可直接覆盖安装，配置与设备号保留）。
        </div>
        {pdaRows === null ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">正在加载 PDA 安装包…</div>
          </div>
        ) : pdaRows.length === 0 ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">暂无 PDA 安装包</div>
            <div className="hint">
              点击「上传 APK」上传 PDA 宿主安装包（仅 .apk），或直接将文件放入服务端数据目录
              <span className="mono" style={{ margin: '0 4px' }}>
                pda-packages
              </span>
              （重启后自动列出）；也可从 GitHub Release 下载 APK 后放入。
            </div>
          </div>
        ) : (
          <PackageTable rows={pdaRows} qrOrigin={qrOrigin} deleting={deleting} onRemove={removePda} />
        )}
      </div>
    </div>
  )
}
