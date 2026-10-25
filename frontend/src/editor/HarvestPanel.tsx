import { useEffect, useState } from 'react'
import { Download, LoaderCircle, Play, Square, X } from 'lucide-react'
import {
  cancelHarvest,
  exportCatalogSnapshot,
  getHarvestStatus,
  getManufacturerSources,
  HarvestUnavailableError,
  startManufacturerHarvest,
  type HarvestStatus,
  type ManufacturerSource,
} from '../services/harvestApi'
import { t } from '../i18n'

// Translated where shown, so a language switch applies without rebuilding the map.
const outcomeLabels: Record<string, string> = {
  Imported: 'Yeni',
  Updated: 'Güncellendi',
  AlreadyInCatalog: 'Zaten var',
  NoVerifiedMatch: 'Ölçü yok',
  Failed: 'Hata',
}

// Drives the manufacturer-sitemap harvester from the editor instead of PowerShell.
export function HarvestPanel({ onClose, onCatalogChanged }: { onClose: () => void; onCatalogChanged: () => void }) {
  const [sources, setSources] = useState<ManufacturerSource[]>([])
  const [selected, setSelected] = useState<string[]>([])
  const [status, setStatus] = useState<HarvestStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [unavailable, setUnavailable] = useState(false)
  const [snapshotMessage, setSnapshotMessage] = useState<string | null>(null)

  useEffect(() => {
    getManufacturerSources()
      .then(setSources)
      .catch((reason) => reason instanceof HarvestUnavailableError ? setUnavailable(true) : setError(String(reason.message ?? reason)))
  }, [])

  const isRunning = status?.isRunning ?? false
  useEffect(() => {
    if (unavailable) return
    let cancelled = false
    let wasRunning = false
    const poll = async () => {
      try {
        const next = await getHarvestStatus()
        if (cancelled) return
        if (wasRunning && !next.isRunning) onCatalogChanged()
        wasRunning = next.isRunning
        setStatus(next)
      } catch (reason) {
        if (reason instanceof HarvestUnavailableError) setUnavailable(true)
      }
    }
    void poll()
    const timer = window.setInterval(poll, 4000)
    return () => {
      cancelled = true
      window.clearInterval(timer)
    }
  }, [unavailable, onCatalogChanged])

  const start = async () => {
    setError(null)
    try {
      setStatus(await startManufacturerHarvest(selected))
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : t('Toplama başlatılamadı.'))
    }
  }

  const exportSnapshot = async () => {
    try {
      const result = await exportCatalogSnapshot()
      setSnapshotMessage(t('{count} ürün anlık görüntüye yazıldı. Dosyayı commit etmeyi unutma.', { count: result.productCount }))
    } catch (reason) {
      setSnapshotMessage(reason instanceof Error ? reason.message : t('Anlık görüntü yazılamadı.'))
    }
  }

  const progress = status && status.total > 0 ? Math.round((status.processed / status.total) * 100) : 0

  return (
    <div className="project-overlay" role="dialog" aria-modal="true" aria-label={t('Katalog toplama')}>
      <button className="overlay-backdrop" aria-label={t('Katalog toplamayı kapat')} onClick={onClose} />
      <section className="project-drawer harvest-drawer">
        <div className="drawer-heading">
          <div><span>{t('Ürün haritaları')}</span><h2>{t('Katalog toplama')}</h2></div>
          <button aria-label={t('Kapat')} onClick={onClose}><X size={17} /></button>
        </div>

        {unavailable ? (
          <p className="drawer-empty">{t('Katalog toplama yalnızca API geliştirme ortamında (Development) çalışırken açıktır.')}</p>
        ) : (
          <>
            <p className="discovery-intro">
              {t('Seçili üreticilerin ürün haritalarındaki sayfalar okunur; yalnızca üç eksen ölçüsü ve kaynağı doğrulanan ürünler kataloğa yazılır. Daha önce okunan sayfalar 30 gün boyunca atlanır. Seçim yapılmazsa bütün kaynaklar çalışır.')}
            </p>
            <div className="harvest-sources">
              {sources.map((source) => (
                <label key={source.name} className={selected.includes(source.name) ? 'checked' : ''}>
                  <input
                    type="checkbox"
                    checked={selected.includes(source.name)}
                    disabled={isRunning}
                    onChange={(event) => setSelected((current) => event.target.checked
                      ? [...current, source.name]
                      : current.filter((name) => name !== source.name))}
                  />
                  {source.name}
                </label>
              ))}
            </div>
            <div className="project-backup-actions">
              {isRunning
                ? <button onClick={() => void cancelHarvest()}><Square size={14} /> {t('Durdur')}</button>
                : <button onClick={() => void start()}><Play size={14} /> {t('Başlat')}</button>}
              <button onClick={() => void exportSnapshot()} disabled={isRunning}><Download size={14} /> {t('Anlık görüntüyü yaz')}</button>
            </div>
            {error && <p className="drawer-error">{error}</p>}
            {snapshotMessage && <p className="drawer-empty">{snapshotMessage}</p>}

            {status && status.total > 0 && (
              <div className="harvest-status">
                <div className="harvest-progress" aria-label={t('İlerleme yüzde {progress}', { progress })}><span style={{ width: `${progress}%` }} /></div>
                <dl>
                  <div><dt>{t('İşlenen')}</dt><dd>{status.processed} / {status.total}</dd></div>
                  <div><dt>{t('Yeni')}</dt><dd>{status.imported}</dd></div>
                  <div><dt>{t('Güncellenen')}</dt><dd>{status.updated}</dd></div>
                  <div><dt>{t('Zaten var')}</dt><dd>{status.alreadyInCatalog}</dd></div>
                  <div><dt>{t('Ölçü yok')}</dt><dd>{status.noVerifiedMatch}</dd></div>
                  <div><dt>{t('Hata')}</dt><dd>{status.failed}</dd></div>
                </dl>
                {status.currentQuery && <p className="harvest-current"><LoaderCircle className="spin" size={13} /> {status.currentQuery}</p>}
                <ul className="harvest-recent">
                  {status.recent.slice(0, 25).map((entry) => (
                    <li key={`${entry.query}-${entry.finishedAt}`} className={entry.outcome}>
                      <b>{outcomeLabels[entry.outcome] ? t(outcomeLabels[entry.outcome]) : entry.outcome}</b>
                      <span>{entry.productName ?? entry.query.replace(/^https?:\/\/(www\.)?/, '')}</span>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </>
        )}
      </section>
    </div>
  )
}
