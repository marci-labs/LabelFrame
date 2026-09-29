// 下载中心页（迭代 59 决策 #119，Server UI 专用）：客户端安装包（client-packages）与 PDA 安装包（pda-packages）
// 同页分区统一分发——列表（文件名 / 大小 / 修改时间，最新在上）+ 上传（multipart）+ 下载 + 删除（确认）；
// 每条目旁展示二维码（内容 = 本页 origin + 该条目下载路径，即管理员正在访问的局域网地址）——PDA 与服务器
// 同网扫码即得下载 URL；PDA 区常驻 Android「未知来源 / 安装未知应用」授权步骤提示。
// client-packages 既有行为不动：客户端设置页「更新与安装包」卡片仍走 GET /api/client-packages 下载。
// 迭代 112（#246）：文案 key 化（downloadCenter 域；zh-CN 值与原硬编码逐字一致）。

import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import qrcode from 'qrcode-generator'
// i18n 初始化副作用导入：本页组件树不传递依赖 i18n/index（api client 在测试中被 mock），
// 显式引入保证 useTranslation 在任何入口（含单测）下都有已注册的语言包资源。
import '../i18n'
import { clientPackageDownloadUrl, pdaPackageDownloadUrl, serverApi } from '../lib/api/client'
import { ApiError } from '../lib/api/types'
import type { ClientPackageInfo, PdaPackageInfo } from '../lib/api/types'
import { Icon } from '../components/Icon'
import { Modal } from '../components/Modal'
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
function QrCodeImage({ text, size = 84, alt }: { text: string; size?: number; alt: string }) {
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
      alt={alt}
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
  uploadingLabel,
}: {
  title: string
  hint: string
  uploading: boolean
  uploadLabel: string
  inputId: string
  onPick: (file: File) => void
  uploadingLabel: string
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, margin: '0 0 8px' }}>
      <div className="page-title" style={{ fontSize: 15, margin: 0, flex: 1 }}>
        {title}
        <small>{hint}</small>
      </div>
      <button className="btn sm" onClick={() => document.getElementById(inputId)?.click()} disabled={uploading}>
        <Icon name="upload" size={13} />
        {uploading ? uploadingLabel : uploadLabel}
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
  const { t } = useTranslation('downloadCenter')
  return (
    <table className="table">
      <thead>
        <tr>
          <th>{t('columns.fileName')}</th>
          <th style={{ width: 100 }}>{t('columns.size')}</th>
          <th style={{ width: 150 }}>{t('columns.modifiedAt')}</th>
          <th style={{ width: 104 }}>{t('columns.qrCode')}</th>
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
              <QrCodeImage text={`${qrOrigin}${p.downloadHref}`} alt={t('qrAlt')} />
            </td>
            <td>
              <div style={{ display: 'flex', gap: 6 }}>
                <a className="btn sm" href={p.downloadHref} title={t('downloadTitle', { name: p.fileName })}>
                  <Icon name="download" size={12} />
                  {t('action.download')}
                </a>
                <button
                  className="btn sm danger"
                  onClick={() => onRemove(p)}
                  disabled={deleting === p.fileName}
                  title={t('deleteTitle')}
                >
                  <Icon name="trash" size={12} />
                  {deleting === p.fileName ? t('action.deleting') : t('action.delete')}
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
  const { t } = useTranslation('downloadCenter')
  const [clientPackages, setClientPackages] = useState<ClientPackageInfo[] | null>(null)
  const [pdaPackages, setPdaPackages] = useState<PdaPackageInfo[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [uploadingClient, setUploadingClient] = useState(false)
  const [uploadingPda, setUploadingPda] = useState(false)
  const [deleting, setDeleting] = useState<string | null>(null)
  // 迭代 93（#151 F-04）：删除确认改自研 Modal（复用工作台删除确认模式），替代原生 confirm 弹窗
  const [pendingRemove, setPendingRemove] = useState<{ kind: 'client' | 'pda'; row: PackageRow } | null>(null)

  const load = useCallback(async () => {
    setError(null)
    try {
      const [clients, pdas] = await Promise.all([serverApi.listClientPackages(), serverApi.listPdaPackages()])
      setClientPackages(clients)
      setPdaPackages(pdas)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('loadFailed'))
    }
  }, [t])

  useEffect(() => {
    void load()
  }, [load])

  const uploadClient = async (file: File) => {
    setUploadingClient(true)
    setNotice(null)
    setError(null)
    try {
      await serverApi.uploadClientPackage(file)
      setNotice(t('client.uploadOk', { name: file.name }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('uploadFailed'))
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
      setNotice(t('pda.uploadOk', { name: file.name }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('uploadFailed'))
    } finally {
      setUploadingPda(false)
    }
  }

  const removeClient = async (p: PackageRow) => {
    setDeleting(p.fileName)
    setError(null)
    setNotice(null)
    try {
      await serverApi.deleteClientPackage(p.fileName)
      setNotice(t('client.deleteOk', { name: p.fileName }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('deleteFailed'))
    } finally {
      setDeleting(null)
    }
  }

  const removePda = async (p: PackageRow) => {
    setDeleting(p.fileName)
    setError(null)
    setNotice(null)
    try {
      await serverApi.deletePdaPackage(p.fileName)
      setNotice(t('pda.deleteOk', { name: p.fileName }))
      void load()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : t('deleteFailed'))
    } finally {
      setDeleting(null)
    }
  }

  /** 删除确认 Modal 的「确认删除」：按分区派发并立即关闭确认框（进行中状态由行内按钮呈现）。 */
  const confirmRemove = () => {
    if (!pendingRemove) return
    const { kind, row } = pendingRemove
    setPendingRemove(null)
    if (kind === 'client') void removeClient(row)
    else void removePda(row)
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
          {t('page.title')}
          <small>{t('page.subtitle')}</small>
        </div>
        <div className="spacer" />
        <button className="btn" onClick={() => void load()} title={t('refreshTitle')}>
          <Icon name="refresh" size={13} />
          {t('action.refresh')}
        </button>
      </div>

      {error && <div className="banner error">{error}</div>}
      {notice && <div className="banner notice">{notice}</div>}

      <div style={{ flex: 1, overflow: 'auto', padding: 12 }}>
        {/* ── 客户端下载（PC；client-packages 既有数据并入展示，行为不动）── */}
        <SectionHead
          title={t('client.title')}
          hint={t('client.hint')}
          uploading={uploadingClient}
          uploadLabel={t('client.uploadLabel')}
          inputId="clientPkgFile"
          onPick={(f) => void uploadClient(f)}
          uploadingLabel={t('action.uploading')}
        />
        {clientRows === null ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">{t('client.loading')}</div>
          </div>
        ) : clientRows.length === 0 ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">{t('client.empty')}</div>
            <div className="hint">
              {t('client.emptyHint')}
            </div>
          </div>
        ) : (
          <PackageTable rows={clientRows} qrOrigin={qrOrigin} deleting={deleting} onRemove={(row) => setPendingRemove({ kind: 'client', row })} />
        )}

        {/* ── PDA 下载（Android 宿主 APK；pda-packages 新目录，迭代 59 决策 #119）── */}
        <div style={{ height: 16 }} />
        <SectionHead
          title={t('pda.title')}
          hint={t('pda.hint')}
          uploading={uploadingPda}
          uploadLabel={t('pda.uploadLabel')}
          inputId="pdaPkgFile"
          onPick={(f) => void uploadPda(f)}
          uploadingLabel={t('action.uploading')}
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
          {t('pda.installHint')}
        </div>
        {pdaRows === null ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">{t('pda.loading')}</div>
          </div>
        ) : pdaRows.length === 0 ? (
          <div className="empty">
            <Icon name="download" />
            <div className="empty-title">{t('pda.empty')}</div>
            <div className="hint">
              {t('pda.emptyHint')}
            </div>
          </div>
        ) : (
          <PackageTable rows={pdaRows} qrOrigin={qrOrigin} deleting={deleting} onRemove={(row) => setPendingRemove({ kind: 'pda', row })} />
        )}
      </div>

      {pendingRemove && (
        <Modal
          title={pendingRemove.kind === 'client' ? t('deleteModal.titleClient') : t('deleteModal.titlePda')}
          onClose={() => setPendingRemove(null)}
          footer={
            <>
              <button className="btn" onClick={() => setPendingRemove(null)}>
                {t('action.cancel')}
              </button>
              <button className="btn danger" onClick={confirmRemove} disabled={deleting !== null}>
                <Icon name="trash" size={13} />
                {t('action.confirmDelete')}
              </button>
            </>
          }
        >
          <p>
            {pendingRemove.kind === 'client'
              ? t('deleteModal.bodyClient', { name: pendingRemove.row.fileName })
              : t('deleteModal.bodyPda', { name: pendingRemove.row.fileName })}
          </p>
        </Modal>
      )}
    </div>
  )
}
