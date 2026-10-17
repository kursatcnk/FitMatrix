import { useEffect, useRef, useState } from 'react'
import { Box, Check, Copy, Database, Download, FolderOpen, LoaderCircle, Plus, Redo2, RotateCcw, Ruler, Save, Search, Trash2, Undo2, Upload, Wifi, X } from 'lucide-react'
import { EditorCanvas } from './editor/EditorCanvas'
import { equipmentCatalog, resolveEquipmentDefinition } from './data/equipmentCatalog'
import { findAvailablePlacement, getPlacementStatus } from './editor/placement'
import { analyzeErgonomics, clampUserHeight, DEFAULT_USER_HEIGHT } from './editor/ergonomics'
import { CATALOG_PAGE_SIZE, countByCategory, filterCatalog } from './data/catalogFilter'
import { deleteCatalogProduct, getCatalog, getCatalogProductDetail, importDiscoveredProduct, type ApiCatalogProductDetail, type DiscoveryCatalogInput } from './services/catalogApi'
import { deleteProject, duplicateProject, getProject, listProjects, saveProjectToApi, type ProjectSummary } from './services/projectApi'
import { discoverProduct, type ExternalProductCandidate, type ProductDiscoveryResponse } from './services/productDiscoveryApi'
import { useEditorStore } from './store/editorStore'
import type { EquipmentCategory, EquipmentDefinition, EquipmentModel, PlacedEquipment, SavedProject } from './types/editor'

const categoryLabels: Record<EquipmentCategory, string> = {
  display: 'Ekran',
  computer: 'Bilgisayar',
  input: 'Kontrol',
  audio: 'Ses',
  imaging: 'Görüntü',
  network: 'Ağ',
  gaming: 'Oyun',
  mobile: 'Mobil',
  furniture: 'Mobilya',
  lighting: 'Aydınlatma',
  power: 'Güç',
  office: 'Ofis',
  accessory: 'Aksesuar',
}

const formatMeasure = (value: number | null, maximumFractionDigits = 2) =>
  value === null ? '—' : value.toLocaleString('tr-TR', { maximumFractionDigits })

interface DisplaySpecification {
  group: string
  name: string
  value: string
  unit: string | null
}

function SpecificationList({ specifications }: { specifications: DisplaySpecification[] }) {
  const groups = Array.from(specifications.reduce((result, item) => {
    const group = result.get(item.group) ?? []
    group.push(item)
    result.set(item.group, group)
    return result
  }, new Map<string, DisplaySpecification[]>()))
  if (groups.length === 0) return <p className="spec-empty">Bu kaynakta ek teknik özellik yayımlanmamış.</p>

  return (
    <div className="spec-groups">
      {groups.map(([group, items]) => (
        <section className="spec-group" key={group}>
          <h4>{group}</h4>
          {items.map((item, index) => (
            <div className="spec-row" key={`${item.name}-${index}`}>
              <span>{item.name}</span>
              <strong>{item.value}{item.unit ? ` ${item.unit}` : ''}</strong>
            </div>
          ))}
        </section>
      ))}
    </div>
  )
}

interface ExternalImportDraft {
  category: EquipmentCategory
  model: EquipmentModel
  footprintWidth: number
  footprintDepth: number
  color: string
}

interface ProjectBackup {
  format: 'fitmatrix-project'
  version: 1
  exportedAt: string
  projectName: string
  desk: { width: number; depth: number; height: number }
  items: PlacedEquipment[]
  catalog: EquipmentDefinition[]
}

interface LocalProjectSnapshot extends SavedProject {
  catalog?: EquipmentDefinition[]
}

const defaultModelByCategory: Record<EquipmentCategory, EquipmentModel> = {
  display: 'monitor',
  computer: 'tower',
  input: 'keyboard',
  audio: 'speaker',
  imaging: 'camera',
  network: 'router',
  gaming: 'console',
  mobile: 'phone',
  furniture: 'desk',
  lighting: 'lamp',
  power: 'power',
  office: 'printer',
  accessory: 'generic',
}

const supportedModels: EquipmentModel[] = [
  'monitor', 'tower', 'keyboard', 'mouse', 'laptop', 'speaker', 'headphone', 'mat', 'lamp', 'stand',
  'desk', 'chair', 'router', 'camera', 'printer', 'phone', 'tablet', 'console', 'power', 'generic',
]

const isEquipmentCategory = (value: string): value is EquipmentCategory => value in categoryLabels
const isEquipmentModel = (value: string): value is EquipmentModel => supportedModels.includes(value as EquipmentModel)

function inferExternalImport(productName: string, width: number, depth: number): ExternalImportDraft {
  const name = productName.toLocaleLowerCase('en-US')
  let category: EquipmentCategory = 'accessory'
  let model: EquipmentModel = 'generic'

  if (/monitor|monitör|display|screen|ekran|panel size/.test(name)) [category, model] = ['display', 'monitor']
  else if (/laptop|notebook|dizüstü/.test(name)) [category, model] = ['computer', 'laptop']
  else if (/case|tower|chassis|desktop pc|bilgisayar kasası/.test(name)) [category, model] = ['computer', 'tower']
  else if (/mousepad|mouse pad|desk mat/.test(name)) [category, model] = ['accessory', 'mat']
  else if (/keyboard|klavye/.test(name)) [category, model] = ['input', 'keyboard']
  else if (/mouse|fare/.test(name)) [category, model] = ['input', 'mouse']
  else if (/headphone|headset|earphone|earbud|kulaklık/.test(name)) [category, model] = ['audio', 'headphone']
  else if (/speaker|hoparlör|microphone|mikrofon|soundbar/.test(name)) [category, model] = ['audio', 'speaker']
  else if (/webcam|camera|kamera|capture card/.test(name)) [category, model] = ['imaging', 'camera']
  else if (/router|modem|network switch|access point|mesh wifi|nas/.test(name)) [category, model] = ['network', 'router']
  else if (/playstation|xbox|nintendo|console|konsol|steam deck|vr headset/.test(name)) [category, model] = ['gaming', 'console']
  else if (/tablet|ipad|kindle|e-reader/.test(name)) [category, model] = ['mobile', 'tablet']
  else if (/smartphone|phone|telefon|smartwatch/.test(name)) [category, model] = ['mobile', 'phone']
  else if (/chair|sandalye/.test(name)) [category, model] = ['furniture', 'chair']
  else if (/desk|table|masa|shelf|raf|cabinet|dolap/.test(name)) [category, model] = ['furniture', 'desk']
  else if (/lamp|light|lamba|aydınlatma/.test(name)) [category, model] = ['lighting', 'lamp']
  else if (/ups|power strip|surge protector|charger|şarj|adapter/.test(name)) [category, model] = ['power', 'power']
  else if (/printer|yazıcı|scanner|tarayıcı|laminator|shredder/.test(name)) [category, model] = ['office', 'printer']
  else if (/stand|riser|yükseltici/.test(name)) [category, model] = ['accessory', 'stand']

  return {
    category,
    model,
    footprintWidth: width,
    footprintDepth: depth,
    color: model === 'monitor' ? '#252a2e' : '#3b4240',
  }
}

function toCatalogInput(product: ExternalProductCandidate, draft: ExternalImportDraft): DiscoveryCatalogInput {
  return {
    provider: product.provider,
    productName: product.productName,
    brand: product.brand ?? 'Unknown',
    modelName: product.modelName ?? product.manufacturerPartNumber ?? product.productName,
    manufacturerPartNumber: product.manufacturerPartNumber,
    gtin: product.gtin,
    widthCm: product.widthCm!,
    heightCm: product.heightCm!,
    depthCm: product.depthCm!,
    footprintWidthCm: draft.footprintWidth,
    footprintDepthCm: draft.footprintDepth,
    weightKg: product.weightKg,
    categorySlug: draft.category,
    editorModel: draft.model,
    displayColor: draft.color,
    sourceUrl: product.sourceUrl!,
    measurementStatus: product.measurementStatus,
    specifications: product.specifications,
  }
}

function NumberField({
  label,
  value,
  suffix,
  onChange,
}: {
  label: string
  value: number
  suffix: string
  onChange: (value: number) => void
}) {
  return (
    <label className="number-field">
      <span>{label}</span>
      <div>
        <input
          type="number"
          step="1"
          value={Math.round(value * 10) / 10}
          onChange={(event) => onChange(Number(event.target.value))}
        />
        <small>{suffix}</small>
      </div>
    </label>
  )
}

const USER_HEIGHT_KEY = 'fitmatrix.userHeightCm'

function readStoredUserHeight() {
  try {
    const stored = Number(localStorage.getItem(USER_HEIGHT_KEY))
    return stored ? clampUserHeight(stored) : DEFAULT_USER_HEIGHT
  } catch {
    return DEFAULT_USER_HEIGHT
  }
}

function App() {
  const {
    projectId,
    catalog,
    projectName,
    desk,
    items,
    selectedId,
    snapEnabled,
    cameraMode,
    transformMode,
    historyPast,
    historyFuture,
    addEquipment,
    duplicateItem,
    updateItem,
    updateDesk,
    deleteItem,
    selectItem,
    setProjectName,
    setProjectId,
    setCatalog,
    setCameraMode,
    resetCamera,
    setTransformMode,
    toggleSnap,
    undo,
    redo,
    resetProject,
    loadProject,
  } = useEditorStore()
  const [savedAt, setSavedAt] = useState<string | null>(null)
  const [saveTarget, setSaveTarget] = useState<'sql' | 'local' | null>(null)
  const [isSaving, setIsSaving] = useState(false)
  const [showProjects, setShowProjects] = useState(false)
  const [showProductFinder, setShowProductFinder] = useState(false)
  const [projects, setProjects] = useState<ProjectSummary[]>([])
  const [projectsError, setProjectsError] = useState<string | null>(null)
  const [activeCategory, setActiveCategory] = useState<EquipmentCategory | 'all'>('all')
  const [catalogSearch, setCatalogSearch] = useState('')
  const [catalogLimit, setCatalogLimit] = useState(CATALOG_PAGE_SIZE)
  const [catalogMessage, setCatalogMessage] = useState<string | null>(null)
  const [discoveryQuery, setDiscoveryQuery] = useState('')
  const [discovery, setDiscovery] = useState<ProductDiscoveryResponse | null>(null)
  const [discoveryError, setDiscoveryError] = useState<string | null>(null)
  const [isDiscovering, setIsDiscovering] = useState(false)
  const [externalDraft, setExternalDraft] = useState<ExternalImportDraft | null>(null)
  const [autoCatalogStatus, setAutoCatalogStatus] = useState<'idle' | 'saving' | 'saved' | 'failed'>('idle')
  const [isImportingExternal, setIsImportingExternal] = useState(false)
  const [externalImportError, setExternalImportError] = useState<string | null>(null)
  const [productDetails, setProductDetails] = useState<Record<string, ApiCatalogProductDetail>>({})
  const [loadingDetailId, setLoadingDetailId] = useState<string | null>(null)
  const backupInputRef = useRef<HTMLInputElement>(null)

  const selectedItem = items.find((item) => item.instanceId === selectedId)
  const selectedDefinition = selectedItem ? resolveEquipmentDefinition(selectedItem, catalog) : undefined
  const placement = selectedItem ? getPlacementStatus(selectedItem, items, desk, catalog) : null
  const invalidItemCount = items.filter((item) => !getPlacementStatus(item, items, desk, catalog).isValid).length
  const [userHeight, setUserHeight] = useState(readStoredUserHeight)
  const ergonomics = analyzeErgonomics(items, desk, userHeight, catalog)

  const updateUserHeight = (height: number) => {
    const next = clampUserHeight(height)
    setUserHeight(next)
    try {
      localStorage.setItem(USER_HEIGHT_KEY, String(next))
    } catch {
      // Storage can be unavailable in private windows; the value still applies for this session.
    }
  }
  const externalProduct = discovery?.externalMatch
  const externalIsDesk = externalDraft?.model === 'desk'
  const externalPreviewDefinition: EquipmentDefinition | null = externalProduct && externalDraft && externalProduct.widthCm && externalProduct.heightCm && externalProduct.depthCm
    ? {
        id: '__external-preview__',
        name: externalProduct.productName,
        category: externalDraft.category,
        width: externalProduct.widthCm,
        height: externalProduct.heightCm,
        depth: externalProduct.depthCm,
        footprintWidth: externalDraft.footprintWidth,
        footprintDepth: externalDraft.footprintDepth,
        defaultRotation: 0,
        color: externalDraft.color,
        model: externalDraft.model,
      }
    : null
  const externalEmptyDeskFit = externalPreviewDefinition
    ? findAvailablePlacement(externalPreviewDefinition, [], desk, catalog)
    : null
  const externalCurrentLayoutFit = externalPreviewDefinition
    ? findAvailablePlacement(externalPreviewDefinition, items, desk, catalog)
    : null
  const localFitById = new Map((discovery?.localMatches ?? []).map((result) => {
    const definition = catalog.find((item) => item.databaseId === result.id)
    return [result.id, definition ? {
      definition,
      isDesk: definition.model === 'desk',
      emptyDesk: definition.model === 'desk' ? null : findAvailablePlacement(definition, [], desk, catalog),
      currentLayout: definition.model === 'desk' ? null : findAvailablePlacement(definition, items, desk, catalog),
    } : null] as const
  }))

  useEffect(() => {
    const savedProject = localStorage.getItem('fitmatrix-project')
    let snapshot: LocalProjectSnapshot | null = null
    try {
      if (savedProject) {
        snapshot = JSON.parse(savedProject) as LocalProjectSnapshot
        loadProject(snapshot)
      }
    } catch {
      localStorage.removeItem('fitmatrix-project')
    }
    const localDefinitions = snapshot?.catalog ?? []
    getCatalog()
      .then((apiCatalog) => setCatalog([
        ...apiCatalog.filter((definition) => !localDefinitions.some((local) => local.id === definition.id)),
        ...localDefinitions,
      ]))
      .catch(() => setCatalog([
        ...equipmentCatalog.filter((definition) => !localDefinitions.some((local) => local.id === definition.id)),
        ...localDefinitions,
      ]))
  }, [loadProject, setCatalog])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      const definitionIds = new Set(items.map((item) => item.definitionId))
      const projectCatalog = catalog.filter((definition) => definitionIds.has(definition.id))
      localStorage.setItem('fitmatrix-project', JSON.stringify({ version: 1, projectId, projectName, desk, items, catalog: projectCatalog }))
    }, 500)
    return () => window.clearTimeout(timer)
  }, [projectId, projectName, desk, items, catalog])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null
      const editingText = target?.matches('input, textarea, select, [contenteditable="true"]')
      if (event.key === 'Escape') {
        if (showProductFinder) setShowProductFinder(false)
        else if (showProjects) setShowProjects(false)
        else selectItem(null)
        return
      }
      if (editingText) return

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'z') {
        event.preventDefault()
        if (event.shiftKey) redo()
        else undo()
      } else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'y') {
        event.preventDefault()
        redo()
      } else if ((event.key === 'Delete' || event.key === 'Backspace') && selectedId) {
        event.preventDefault()
        deleteItem(selectedId)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [deleteItem, redo, selectItem, selectedId, showProductFinder, showProjects, undo])

  const saveProject = async () => {
    setIsSaving(true)
    const definitionIds = new Set(items.map((item) => item.definitionId))
    const localSnapshot = {
      version: 1 as const,
      projectId,
      projectName,
      desk,
      items,
      catalog: catalog.filter((definition) => definitionIds.has(definition.id)),
    }
    localStorage.setItem('fitmatrix-project', JSON.stringify(localSnapshot))

    try {
      const result = await saveProjectToApi({ projectId, projectName, desk, items, catalog })
      setProjectId(result.id)
      localStorage.setItem('fitmatrix-project', JSON.stringify({ ...localSnapshot, projectId: result.id }))
      setSaveTarget('sql')
    } catch {
      setSaveTarget('local')
    } finally {
      setSavedAt(new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }))
      setIsSaving(false)
    }
  }

  const startNewProject = () => {
    if (items.length > 0 && !window.confirm('Mevcut yerleşimi temizleyip yeni bir proje başlatılsın mı?')) {
      return
    }
    localStorage.removeItem('fitmatrix-project')
    resetProject()
    setSavedAt(null)
  }

  const openProjectLibrary = async () => {
    setShowProjects(true)
    setProjectsError(null)
    try {
      setProjects(await listProjects())
    } catch {
      setProjectsError('API çalışmıyor veya proje listesine ulaşılamadı.')
    }
  }

  const loadDatabaseProject = async (id: string) => {
    try {
      const project = await getProject(id)
      const recoveredDefinitions: typeof catalog = []
      const placedItems = project.items.map<PlacedEquipment>((item) => {
        let definition = catalog.find((candidate) => candidate.databaseId === item.equipmentProductId)
        if (!definition) {
          definition = {
            id: item.equipmentProductId ? `database-${item.equipmentProductId}` : `project-${item.id}`,
            databaseId: item.equipmentProductId ?? undefined,
            name: item.name,
            category: isEquipmentCategory(item.categorySlug) ? item.categorySlug : 'accessory',
            model: isEquipmentModel(item.editorModel) ? item.editorModel : 'generic',
            color: /^#[0-9a-f]{6}$/i.test(item.displayColor) ? item.displayColor : '#3b4240',
            width: item.widthCm,
            height: item.heightCm,
            depth: item.depthCm,
            footprintWidth: item.footprintWidthCm,
            footprintDepth: item.footprintDepthCm,
            defaultRotation: 0,
          }
          recoveredDefinitions.push(definition)
        }
        return {
          instanceId: crypto.randomUUID(),
          definitionId: definition.id,
          position: { x: item.positionXcm, z: item.positionZCm },
          rotation: item.rotationDegrees,
          elevation: item.elevationCm ?? 0,
          mountMode: item.mountMode ?? 'surface',
          screenOrientation: item.screenOrientation ?? 'landscape',
          tilt: item.tiltDegrees ?? 0,
          dimensions: {
            width: item.widthCm,
            height: item.heightCm,
            depth: item.depthCm,
            footprintWidth: item.footprintWidthCm,
            footprintDepth: item.footprintDepthCm,
          },
        }
      })
      if (recoveredDefinitions.length > 0) setCatalog([
        ...catalog.filter((definition) => !recoveredDefinitions.some((recovered) => recovered.id === definition.id)),
        ...recoveredDefinitions,
      ])
      loadProject({
        version: 1,
        projectId: project.id,
        projectName: project.name,
        desk: { width: project.deskWidthCm, depth: project.deskDepthCm, height: project.deskHeightCm },
        items: placedItems,
      })
      setShowProjects(false)
      setSaveTarget('sql')
    } catch {
      setProjectsError('Proje yüklenemedi.')
    }
  }

  const duplicateDatabaseProject = async (id: string) => {
    setProjectsError(null)
    try {
      await duplicateProject(id)
      setProjects(await listProjects())
    } catch {
      setProjectsError('Proje çoğaltılamadı.')
    }
  }

  const deleteDatabaseProject = async (id: string, name: string) => {
    if (!window.confirm(`“${name}” kalıcı olarak silinsin mi?`)) return
    setProjectsError(null)
    try {
      await deleteProject(id)
      if (projectId === id) setProjectId(null)
      setProjects((current) => current.filter((project) => project.id !== id))
    } catch {
      setProjectsError('Proje silinemedi.')
    }
  }

  const exportProjectBackup = () => {
    const definitionIds = new Set(items.map((item) => item.definitionId))
    const backup: ProjectBackup = {
      format: 'fitmatrix-project',
      version: 1,
      exportedAt: new Date().toISOString(),
      projectName,
      desk,
      items,
      catalog: catalog.filter((definition) => definitionIds.has(definition.id)),
    }
    const blob = new Blob([JSON.stringify(backup, null, 2)], { type: 'application/json' })
    const href = URL.createObjectURL(blob)
    const anchor = document.createElement('a')
    anchor.href = href
    anchor.download = `${projectName.trim().replace(/[^\p{L}\p{Nd}]+/gu, '-').replace(/^-|-$/g, '') || 'fitmatrix-proje'}.fitmatrix.json`
    anchor.click()
    URL.revokeObjectURL(href)
  }

  const importProjectBackup = async (file: File) => {
    setProjectsError(null)
    try {
      if (file.size > 5_000_000) throw new Error('Yedek dosyası 5 MB sınırını aşıyor.')
      const backup = JSON.parse(await file.text()) as Partial<ProjectBackup>
      if (backup.format !== 'fitmatrix-project' || backup.version !== 1 || typeof backup.projectName !== 'string' ||
          !backup.desk || !Array.isArray(backup.items) || !Array.isArray(backup.catalog))
        throw new Error('Bu dosya geçerli bir FitMatrix proje yedeği değil.')
      if (backup.items.length > 200 || backup.catalog.length > 200)
        throw new Error('Yedek desteklenen ürün sayısını aşıyor.')
      const validNumber = (value: unknown, min: number, max: number) => typeof value === 'number' && Number.isFinite(value) && value >= min && value <= max
      if (!validNumber(backup.desk.width, 40, 500) || !validNumber(backup.desk.depth, 30, 300) || !validNumber(backup.desk.height, 30, 150))
        throw new Error('Yedekteki masa ölçüleri desteklenen aralıkta değil.')
      const validDefinitions = backup.catalog.filter((definition) =>
        definition && typeof definition.id === 'string' && typeof definition.name === 'string' &&
        isEquipmentCategory(definition.category) && isEquipmentModel(definition.model) &&
        validNumber(definition.width, .1, 1000) && validNumber(definition.height, .1, 1000) &&
        validNumber(definition.depth, .1, 1000) && validNumber(definition.footprintWidth, .1, definition.width) &&
        validNumber(definition.footprintDepth, .1, definition.depth) && /^#[0-9a-f]{6}$/i.test(definition.color),
      )
      if (validDefinitions.length !== backup.catalog.length)
        throw new Error('Yedekte geçersiz bir ürün tanımı var.')
      const knownIds = new Set([...catalog, ...validDefinitions].map((definition) => definition.id))
      const validItems = backup.items.every((item) => item && typeof item.instanceId === 'string' && knownIds.has(item.definitionId) &&
        validNumber(item.position?.x, -5000, 5000) && validNumber(item.position?.z, -5000, 5000) && validNumber(item.rotation, -36000, 36000) &&
        validNumber(item.elevation ?? 0, 0, 300) && validNumber(item.tilt ?? 0, -45, 45) &&
        (item.mountMode === undefined || item.mountMode === 'surface' || item.mountMode === 'vesa') &&
        (item.screenOrientation === undefined || item.screenOrientation === 'landscape' || item.screenOrientation === 'portrait'))
      if (!validItems) throw new Error('Yedekte geçersiz veya eksik bir yerleşim kaydı var.')
      setCatalog([...catalog.filter((definition) => !validDefinitions.some((incoming) => incoming.id === definition.id)), ...validDefinitions])
      loadProject({
        version: 1,
        projectId: null,
        projectName: backup.projectName.trim().slice(0, 160) || 'İçe aktarılan proje',
        desk: backup.desk,
        items: backup.items,
      })
      setShowProjects(false)
      setSaveTarget('local')
      setSavedAt(new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' }))
    } catch (error) {
      setProjectsError(error instanceof Error ? error.message : 'Proje yedeği açılamadı.')
    } finally {
      if (backupInputRef.current) backupInputRef.current.value = ''
    }
  }

  const filteredCatalog = filterCatalog(catalog, activeCategory, catalogSearch)
  const categoryCounts = countByCategory(catalog)
  const visibleCatalog = filteredCatalog.slice(0, catalogLimit)

  const updateSelectedDimension = (dimension: 'width' | 'height' | 'depth', rawValue: number) => {
    if (!selectedItem || !selectedDefinition || !Number.isFinite(rawValue)) return
    const value = Math.max(0.1, rawValue)
    const dimensions = {
      width: selectedDefinition.width,
      height: selectedDefinition.height,
      depth: selectedDefinition.depth,
      footprintWidth: selectedDefinition.footprintWidth,
      footprintDepth: selectedDefinition.footprintDepth,
    }

    if (dimension === 'width') {
      dimensions.footprintWidth *= value / selectedDefinition.width
    }
    if (dimension === 'depth') {
      dimensions.footprintDepth *= value / selectedDefinition.depth
    }
    dimensions[dimension] = value
    updateItem(selectedItem.instanceId, { dimensions })
  }

  const searchProduct = async (query: string) => {
    if (query.trim().length < 2) return
    setIsDiscovering(true)
    setDiscoveryError(null)
    setExternalImportError(null)
    setExternalDraft(null)
    setAutoCatalogStatus('idle')
    try {
      const result = await discoverProduct(query.trim())
      setDiscovery(result)
      const product = result.externalMatch
      if (product?.widthCm && product.heightCm && product.depthCm) {
        const identityText = [
          result.query,
          product.productName,
          product.modelName,
          ...product.specifications.flatMap((specification) => [specification.name, specification.value]),
        ].filter(Boolean).join(' ')
        const inferred = inferExternalImport(identityText, product.widthCm, product.depthCm)
        const draft: ExternalImportDraft = {
          category: isEquipmentCategory(product.suggestedCategory) ? product.suggestedCategory : inferred.category,
          model: isEquipmentModel(product.suggestedEditorModel) ? product.suggestedEditorModel : inferred.model,
          footprintWidth: inferred.footprintWidth,
          footprintDepth: inferred.footprintDepth,
          color: /^#[0-9a-f]{6}$/i.test(product.suggestedColor) ? product.suggestedColor : inferred.color,
        }
        setExternalDraft(draft)
        if (product.sourceUrl && product.measurementStatus !== 'identity-only') {
          setAutoCatalogStatus('saving')
          try {
            const definition = await importDiscoveredProduct(toCatalogInput(product, draft))
            setCatalog([...catalog.filter((item) => item.databaseId !== definition.databaseId), definition])
            setAutoCatalogStatus('saved')
          } catch {
            setAutoCatalogStatus('failed')
          }
        }
      }
    } catch (error) {
      setDiscovery(null)
      setDiscoveryError(error instanceof Error ? error.message : 'Ürün bilgileri bulunamadı.')
    } finally {
      setIsDiscovering(false)
    }
  }

  const runProductDiscovery = async (event: React.FormEvent) => {
    event.preventDefault()
    await searchProduct(discoveryQuery)
  }

  const inspectExternalCandidate = async (product: ExternalProductCandidate) => {
    const exactQuery = product.measurementStatus === 'identity-only'
      ? product.productName || [product.brand, product.modelName].filter(Boolean).join(' ') || product.gtin || product.sourceUrl || ''
      : product.gtin || product.sourceUrl || product.productName || [product.brand, product.modelName].filter(Boolean).join(' ')
    setDiscoveryQuery(product.productName || exactQuery)
    await searchProduct(exactQuery)
  }

  const searchCatalogOnline = async () => {
    const query = catalogSearch.trim()
    if (query.length < 2) return
    setDiscoveryQuery(query)
    setShowProductFinder(true)
    await searchProduct(query)
  }

  const addDiscoveredProduct = (databaseId: string) => {
    const definition = catalog.find((item) => item.databaseId === databaseId)
    if (!definition) return
    if (definition.model === 'desk') {
      updateDesk({ width: definition.width, depth: definition.depth, height: definition.height })
      setShowProductFinder(false)
      return
    }
    if (addEquipment(definition.id)) setShowProductFinder(false)
    else setDiscoveryError(`“${definition.name}” mevcut ${desk.width} × ${desk.depth} cm masa düzenine çakışmadan sığmıyor.`)
  }

  const activateCatalogItem = (definition: EquipmentDefinition) => {
    if (definition.model === 'desk') {
      updateDesk({ width: definition.width, depth: definition.depth, height: definition.height })
      setCatalogMessage(`Masa ölçüleri “${definition.name}” ürününe göre güncellendi.`)
      return
    }
    setCatalogMessage(addEquipment(definition.id)
      ? null
      : `“${definition.name}” için çakışmasız boş alan bulunamadı.`)
  }

  const loadProductSpecifications = async (databaseId: string) => {
    if (productDetails[databaseId]) return
    setLoadingDetailId(databaseId)
    try {
      const detail = await getCatalogProductDetail(databaseId)
      setProductDetails((current) => ({ ...current, [databaseId]: detail }))
    } catch (error) {
      setDiscoveryError(error instanceof Error ? error.message : 'Ürün özellikleri alınamadı.')
    } finally {
      setLoadingDetailId(null)
    }
  }

  const removeCatalogProduct = async (databaseId: string, name: string) => {
    if (!window.confirm(`“${name}” yerel ürün kataloğundan kaldırılsın mı? Kayıtlı projelerdeki yerleşimler korunur.`)) return
    setDiscoveryError(null)
    try {
      await deleteCatalogProduct(databaseId)
      setCatalog(catalog.filter((item) => item.databaseId !== databaseId))
      setDiscovery((current) => current ? { ...current, localMatches: current.localMatches.filter((item) => item.id !== databaseId) } : current)
      setProductDetails((current) => {
        const next = { ...current }
        delete next[databaseId]
        return next
      })
    } catch (error) {
      setDiscoveryError(error instanceof Error ? error.message : 'Ürün katalogdan kaldırılamadı.')
    }
  }

  const importExternalProduct = async () => {
    const product = discovery?.externalMatch
    if (!product || !externalDraft || !product.widthCm || !product.heightCm || !product.depthCm || !product.sourceUrl) return
    setIsImportingExternal(true)
    setExternalImportError(null)
    try {
      const definition = await importDiscoveredProduct(toCatalogInput(product, externalDraft))
      const nextCatalog = [...catalog.filter((item) => item.databaseId !== definition.databaseId), definition]
      setCatalog(nextCatalog)
      if (definition.model === 'desk') {
        updateDesk({ width: definition.width, depth: definition.depth, height: definition.height })
        setShowProductFinder(false)
      }
      else if (addEquipment(definition.id)) setShowProductFinder(false)
      else setExternalImportError(`Ürün kataloğa kaydedildi ancak mevcut ${desk.width} × ${desk.depth} cm masa düzenine çakışmadan sığmıyor.`)
    } catch (error) {
      setExternalImportError(error instanceof Error ? error.message : 'Ürün kataloğa alınamadı.')
    } finally {
      setIsImportingExternal(false)
    }
  }

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand-block">
          <div className="brand-mark" aria-hidden="true">
            {Array.from({ length: 9 }, (_, index) => <i key={index} className={index === 4 ? 'filled' : ''} />)}
          </div>
          <div>
            <strong>FitMatrix</strong>
            <span>1:1 · cm</span>
          </div>
        </div>

        <div className="project-title">
          <span>Proje</span>
          <input
            aria-label="Proje adı"
            value={projectName}
            onChange={(event) => setProjectName(event.target.value)}
          />
        </div>

        <div className="topbar-actions">
          {savedAt && <span className="save-state"><Check size={14} /> {savedAt} · {saveTarget === 'sql' ? 'SQL' : 'YEREL'}</span>}
          <button className="icon-action" aria-label="Geri al" title="Geri al (Ctrl+Z)" disabled={historyPast.length === 0} onClick={undo}><Undo2 size={15} /></button>
          <button className="icon-action" aria-label="İleri al" title="İleri al (Ctrl+Y)" disabled={historyFuture.length === 0} onClick={redo}><Redo2 size={15} /></button>
          <button className="button ghost" onClick={() => setShowProductFinder(true)}><Search size={15} /> Ürün bul</button>
          <button className="button ghost" onClick={openProjectLibrary}><FolderOpen size={15} /> Projeler</button>
          <button className="button ghost" onClick={startNewProject}><Plus size={16} /> Yeni düzen</button>
          <button className="button primary" onClick={saveProject} disabled={isSaving}><Save size={16} /> {isSaving ? 'Kaydediliyor' : 'Kaydet'}</button>
        </div>
      </header>

      <div className="editor-layout">
        <aside className="sidebar library-panel">
          <div className="panel-heading">
            <div><span>Katalog</span><h2>Ekipmanlar</h2></div>
            <span className="count-badge">{catalog.length}</span>
          </div>

          <div className="category-tabs" aria-label="Ekipman kategorileri">
            <button className={activeCategory === 'all' ? 'active' : ''} onClick={() => { setActiveCategory('all'); setCatalogLimit(CATALOG_PAGE_SIZE) }}>Tümü</button>
            {Object.entries(categoryLabels).map(([key, label]) => (
              <button
                key={key}
                className={activeCategory === key ? 'active' : ''}
                onClick={() => { setActiveCategory(key as EquipmentCategory); setCatalogLimit(CATALOG_PAGE_SIZE) }}
              >
                {label}<small>{categoryCounts.get(key as EquipmentCategory) ?? 0}</small>
              </button>
            ))}
          </div>

          <label className="catalog-search">
            <Search size={14} />
            <input
              aria-label="Katalogda ara"
              placeholder="Katalogda veya internette ara"
              value={catalogSearch}
              onChange={(event) => { setCatalogSearch(event.target.value); setCatalogLimit(CATALOG_PAGE_SIZE) }}
              onKeyDown={(event) => {
                if (event.key === 'Enter') {
                  event.preventDefault()
                  void searchCatalogOnline()
                }
              }}
            />
            {catalogSearch && <button type="button" aria-label="Katalog aramasını temizle" onClick={() => setCatalogSearch('')}><X size={13} /></button>}
          </label>
          {catalogMessage && <p className="catalog-message" role="status">{catalogMessage}</p>}

          <div className="catalog-list">
            {visibleCatalog.map((item) => (
              <button className="catalog-item" key={item.id} onClick={() => activateCatalogItem(item)}>
                <span className="catalog-icon" style={{ '--equipment-color': item.color } as React.CSSProperties}>
                  <Box size={19} strokeWidth={1.6} />
                </span>
                <span className="catalog-copy">
                  <strong>{item.name}</strong>
                  <small>{item.width} × {item.depth} × {item.height} cm</small>
                </span>
                {item.model === 'desk' ? <Ruler size={16} /> : <Plus size={16} />}
              </button>
            ))}
            {filteredCatalog.length === 0 && <p className="catalog-empty">Yerel katalogda eşleşme yok.</p>}
            {catalogSearch.trim().length >= 2 && (
              <button className="catalog-online-search" type="button" onClick={() => void searchCatalogOnline()} disabled={isDiscovering}>
                {isDiscovering ? <LoaderCircle className="spin" size={14} /> : <Wifi size={14} />}
                İnternette tüm ürünlerde ara
              </button>
            )}
            {filteredCatalog.length > visibleCatalog.length && (
              <button className="catalog-more" type="button" onClick={() => setCatalogLimit((limit) => limit + CATALOG_PAGE_SIZE)}>
                Daha fazla göster <small>{visibleCatalog.length} / {filteredCatalog.length}</small>
              </button>
            )}
          </div>
        </aside>

        <section className="workspace" aria-label="3D masa editörü">
          <div className="workspace-meta">
            <span><i className="status-dot" /> 3D çalışma alanı</span>
            <span className={invalidItemCount > 0 ? 'scene-warning' : ''}>{invalidItemCount > 0 ? `${invalidItemCount} geçersiz yerleşim` : 'Yerleşim uygun'} · Gerçek ölçek</span>
          </div>
          <EditorCanvas />
          <div className="canvas-hint">Nesneyi seçerek taşıyabilir veya döndürebilirsin</div>
          <div className="bottom-toolbar">
            <button onClick={() => setTransformMode('translate')} className={transformMode === 'translate' ? 'active' : ''}>Taşı</button>
            <button onClick={() => setTransformMode('rotate')} className={transformMode === 'rotate' ? 'active' : ''}>Döndür</button>
            <span className="toolbar-divider" />
            <button onClick={() => setCameraMode('perspective')} className={cameraMode === 'perspective' ? 'active' : ''}>Perspektif</button>
            <button onClick={() => setCameraMode('top')} className={cameraMode === 'top' ? 'active' : ''}>Üstten görünüm</button>
            <button onClick={resetCamera}>Kamerayı sıfırla</button>
            <span className="toolbar-divider" />
            <button onClick={toggleSnap} className={snapEnabled ? 'active' : ''}>Grid {snapEnabled ? 'açık' : 'kapalı'}</button>
            <span className="unit-chip">CM</span>
          </div>
        </section>

        <aside className="sidebar inspector-panel">
          <div className="panel-heading">
            <div><span>Düzenleyici</span><h2>{selectedDefinition?.name ?? 'Masa ayarları'}</h2></div>
          </div>

          {selectedItem && selectedDefinition ? (
            <>
              <div className={`placement-card ${placement?.isValid ? 'valid' : 'invalid'}`}>
                <span>{placement?.isValid ? <Check size={15} /> : <span className="warning-mark">!</span>}</span>
                <div>
                  <strong>{placement?.isValid ? 'Yerleşim uygun' : 'Yerleşim geçersiz'}</strong>
                  <small>{placement?.message}</small>
                </div>
              </div>

              <section className="inspector-section">
                <h3>Konum</h3>
                <div className="field-grid">
                  <NumberField label="X" value={selectedItem.position.x} suffix="cm" onChange={(x) => updateItem(selectedItem.instanceId, { position: { ...selectedItem.position, x } })} />
                  <NumberField label="Z" value={selectedItem.position.z} suffix="cm" onChange={(z) => updateItem(selectedItem.instanceId, { position: { ...selectedItem.position, z } })} />
                </div>
                <NumberField label="Rotasyon" value={selectedItem.rotation} suffix="°" onChange={(rotation) => updateItem(selectedItem.instanceId, { rotation })} />
              </section>

              {selectedDefinition.model === 'monitor' && (
                <section className="inspector-section">
                  <h3>Monitör montajı</h3>
                  <span className="control-label">Taşıma biçimi</span>
                  <div className="segmented-control" role="group" aria-label="Monitör taşıma biçimi">
                    <button
                      className={(selectedItem.mountMode ?? 'surface') === 'surface' ? 'active' : ''}
                      onClick={() => updateItem(selectedItem.instanceId, { mountMode: 'surface', elevation: 0, tilt: 0 })}
                    >Masa standı</button>
                    <button
                      className={selectedItem.mountMode === 'vesa' ? 'active' : ''}
                      onClick={() => updateItem(selectedItem.instanceId, { mountMode: 'vesa', elevation: Math.max(5, selectedItem.elevation ?? 0) })}
                    >VESA kolu</button>
                  </div>
                  <span className="control-label">Ekran yönü</span>
                  <div className="segmented-control" role="group" aria-label="Ekran yönü">
                    <button
                      className={(selectedItem.screenOrientation ?? 'landscape') === 'landscape' ? 'active' : ''}
                      onClick={() => updateItem(selectedItem.instanceId, { screenOrientation: 'landscape' })}
                    >Yatay</button>
                    <button
                      className={selectedItem.screenOrientation === 'portrait' ? 'active' : ''}
                      onClick={() => updateItem(selectedItem.instanceId, { screenOrientation: 'portrait' })}
                    >Dikey</button>
                  </div>
                  {selectedItem.mountMode === 'vesa' && (
                    <>
                      <div className="field-grid mount-fields">
                        <NumberField label="Alt kenar" value={selectedItem.elevation ?? 0} suffix="cm" onChange={(elevation) => updateItem(selectedItem.instanceId, { elevation: Math.max(0, elevation) })} />
                        <NumberField label="Öne/arkaya eğim" value={selectedItem.tilt ?? 0} suffix="°" onChange={(tilt) => updateItem(selectedItem.instanceId, { tilt: Math.max(-45, Math.min(45, tilt)) })} />
                      </div>
                      <div className="dimensions-table mount-summary">
                        <span>VESA merkezi <strong>{Math.round(((selectedItem.elevation ?? 0) + ((selectedItem.screenOrientation === 'portrait' ? selectedDefinition.width : selectedDefinition.height) / 2)) * 10) / 10} cm</strong></span>
                      </div>
                    </>
                  )}
                </section>
              )}

              <section className="inspector-section">
                <h3>Fiziksel ölçüler</h3>
                <div className="field-grid">
                  <NumberField label="Genişlik" value={selectedDefinition.width} suffix="cm" onChange={(value) => updateSelectedDimension('width', value)} />
                  <NumberField label="Derinlik" value={selectedDefinition.depth} suffix="cm" onChange={(value) => updateSelectedDimension('depth', value)} />
                </div>
                <NumberField label="Yükseklik" value={selectedDefinition.height} suffix="cm" onChange={(value) => updateSelectedDimension('height', value)} />
                <div className="dimensions-table"><span>Temas alanı <strong>{Math.round(selectedDefinition.footprintWidth * 10) / 10} × {Math.round(selectedDefinition.footprintDepth * 10) / 10} cm</strong></span></div>
              </section>

              <div className="inspector-actions">
                <button onClick={() => duplicateItem(selectedItem.instanceId)}><Copy size={14} /> Çoğalt</button>
                <button className="delete-button" onClick={() => deleteItem(selectedItem.instanceId)}><Trash2 size={14} /> Kaldır</button>
              </div>
            </>
          ) : (
            <>
              <div className="empty-selection">
                <RotateCcw size={22} strokeWidth={1.5} />
                <p>Sahnedeki bir ekipmanı seçerek konumunu ve ölçülerini düzenleyebilirsin.</p>
              </div>
              <section className="inspector-section">
                <h3>Masa ölçüleri</h3>
                <div className="field-grid">
                  <NumberField label="Genişlik" value={desk.width} suffix="cm" onChange={(width) => updateDesk({ width })} />
                  <NumberField label="Derinlik" value={desk.depth} suffix="cm" onChange={(depth) => updateDesk({ depth })} />
                </div>
                <NumberField label="Yükseklik" value={desk.height} suffix="cm" onChange={(height) => updateDesk({ height })} />
              </section>
              <section className="inspector-section ergonomics-section" aria-label="Ergonomi kontrolü">
                <div className="ergonomics-heading">
                  <h3>Ergonomi kontrolü</h3>
                  <span className={`ergonomics-score ${ergonomics.score >= 80 ? 'good' : 'warning'}`}>%{ergonomics.score}</span>
                </div>
                <NumberField label="Kullanıcı boyu" value={userHeight} suffix="cm" onChange={updateUserHeight} />
                <ul className="ergonomics-list">
                  {ergonomics.checks.map((check) => (
                    <li key={check.id} className={`ergonomics-check ${check.status}`}>
                      <span className="ergonomics-mark" aria-hidden="true">{check.status === 'good' ? <Check size={11} /> : check.status === 'warning' ? '!' : '–'}</span>
                      <div>
                        <strong>{check.title}{check.value && <em>{check.value}</em>}</strong>
                        <small>{check.message}</small>
                      </div>
                    </li>
                  ))}
                </ul>
              </section>
            </>
          )}

          <div className="storage-note">
            <Database size={16} />
            <span>{projectId ? 'SQL Server kaydı bağlı. Yerel taslak koruması da etkin.' : 'İlk kayıtta SQL Server projesi oluşturulur; yerel taslak da korunur.'}</span>
          </div>
        </aside>
      </div>

      {showProjects && (
        <div className="project-overlay" role="dialog" aria-modal="true" aria-label="Projeler">
          <button className="overlay-backdrop" aria-label="Projeleri kapat" onClick={() => setShowProjects(false)} />
          <section className="project-drawer">
            <div className="drawer-heading">
              <div><span>Proje arşivi</span><h2>Kayıtlı projeler</h2></div>
              <button aria-label="Kapat" onClick={() => setShowProjects(false)}><X size={18} /></button>
            </div>
            <div className="project-backup-actions">
              <button onClick={exportProjectBackup}><Download size={15} /> Açık projeyi yedekle</button>
              <button onClick={() => backupInputRef.current?.click()}><Upload size={15} /> Yedekten aç</button>
              <input
                ref={backupInputRef}
                type="file"
                accept=".json,.fitmatrix.json,application/json"
                onChange={(event) => {
                  const file = event.target.files?.[0]
                  if (file) void importProjectBackup(file)
                }}
              />
            </div>
            {projectsError && <p className="drawer-error">{projectsError}</p>}
            {!projectsError && projects.length === 0 && <p className="drawer-empty">Henüz kayıtlı proje yok.</p>}
            <div className="project-list">
              {projects.map((project) => (
                <article className="project-row" key={project.id}>
                  <button className="project-open" onClick={() => loadDatabaseProject(project.id)}>
                    <span className="project-code">{project.id.slice(0, 8).toUpperCase()}</span>
                    <strong>{project.name}</strong>
                    <small>{project.itemCount} ekipman · {new Date(project.updatedAt).toLocaleString('tr-TR')}</small>
                    <FolderOpen size={16} />
                  </button>
                  <div className="project-actions">
                    <button aria-label={`${project.name} projesini çoğalt`} title="Projeyi çoğalt" onClick={() => duplicateDatabaseProject(project.id)}><Copy size={14} /></button>
                    <button className="danger" aria-label={`${project.name} projesini sil`} title="Projeyi sil" onClick={() => deleteDatabaseProject(project.id, project.name)}><Trash2 size={14} /></button>
                  </div>
                </article>
              ))}
            </div>
          </section>
        </div>
      )}

      {showProductFinder && (
        <div className="project-overlay" role="dialog" aria-modal="true" aria-label="Ürün bilgisi bul ve masaya yerleştir">
          <button className="overlay-backdrop" aria-label="Ürün bulucuyu kapat" onClick={() => setShowProductFinder(false)} />
          <section className="project-drawer discovery-drawer">
            <div className="drawer-heading">
              <div><span>Ürün veritabanı</span><h2>İnternette ürün ve ölçü ara</h2></div>
              <button aria-label="Kapat" onClick={() => setShowProductFinder(false)}><X size={18} /></button>
            </div>

            <p className="discovery-intro">“Klavye”, “mouse” veya “monitör” gibi genel bir ürün türü yazabilir; marka, model kodu, GTIN ya da ürün bağlantısıyla doğrudan kesin ürünü tarayabilirsin. Yerel SQL, açık ürün dizinleri, üretici sayfaları ve PDF teknik föyleri birlikte aranır.</p>

            <form className="discovery-search" onSubmit={runProductDiscovery}>
              <Search size={18} />
              <input
                autoFocus
                aria-label="Ürün adı, model kodu, GTIN veya ürün bağlantısı"
                placeholder="Örn. klavye, Logitech MX Keys, MSI MAG 274QF veya ürün bağlantısı"
                value={discoveryQuery}
                onChange={(event) => setDiscoveryQuery(event.target.value)}
              />
              <button type="submit" disabled={isDiscovering || discoveryQuery.trim().length < 2}>
                {isDiscovering ? <LoaderCircle className="spin" size={16} /> : <Ruler size={16} />}
                Ara
              </button>
            </form>

            {discoveryError && <p className="drawer-error">{discoveryError}</p>}

            {discovery && (
              <>
                <div className={`provider-state ${discovery.provider.isConfigured ? 'ready' : ''}`}>
                  <Wifi size={15} />
                  <div><strong>{discovery.provider.provider}</strong><span>{discovery.provider.message}</span></div>
                </div>

                {!discovery.externalMatch && discovery.externalMatches.length > 0 && (
                  <section className="web-index-section">
                    <div className="discovery-summary">
                      <span>Çevrimiçi ürün dizini</span>
                      <strong>{discovery.externalMatches.length} gerçek model</strong>
                    </div>
                    <div className="web-index-results">
                      {discovery.externalMatches.map((product, index) => (
                        <article className="web-index-result" key={`${product.sourceUrl ?? product.productName}-${index}`}>
                          <div className="web-index-heading">
                            <div>
                              <span>{product.brand || product.provider}</span>
                              <h3>{product.productName}</h3>
                              <small>{[product.modelName, product.gtin].filter(Boolean).join(' · ') || 'Ürün sayfası eşleşmesi'}</small>
                            </div>
                            <span className={`measurement-trust ${product.measurementStatus}`}>
                              {product.measurementStatus === 'identity-only' ? 'Model bulundu' : 'Ölçü bulundu'}
                            </span>
                          </div>
                          <div className="compact-measures">
                            <span>G {formatMeasure(product.widthCm)}</span>
                            <span>Y {formatMeasure(product.heightCm)}</span>
                            <span>D {formatMeasure(product.depthCm)}</span>
                          </div>
                          <div className="web-index-footer">
                            <small>{product.provider}</small>
                            <button onClick={() => inspectExternalCandidate(product)} disabled={isDiscovering}>
                              {isDiscovering ? <LoaderCircle className="spin" size={14} /> : <Search size={14} />}
                              Tüm ölçüleri tara
                            </button>
                          </div>
                        </article>
                      ))}
                    </div>
                  </section>
                )}

                {discovery.externalMatch && (
                  <article className="external-match">
                    <span className="result-eyebrow">Çevrimiçi eşleşme · {discovery.externalMatch.provider}</span>
                    <h3>{discovery.externalMatch.productName}</h3>
                    <small>{[discovery.externalMatch.brand, discovery.externalMatch.manufacturerPartNumber, discovery.externalMatch.gtin].filter(Boolean).join(' · ')}</small>
                    <div className="measure-strip">
                      <span><b>Genişlik</b>{formatMeasure(discovery.externalMatch.widthCm)} cm</span>
                      <span><b>Yükseklik</b>{formatMeasure(discovery.externalMatch.heightCm)} cm</span>
                      <span><b>Derinlik</b>{formatMeasure(discovery.externalMatch.depthCm)} cm</span>
                      <span><b>Ağırlık</b>{formatMeasure(discovery.externalMatch.weightKg, 3)} kg</span>
                    </div>
                    {externalPreviewDefinition && externalIsDesk && (
                      <div className="fit-assessment fits">
                        <Check size={18} />
                        <div>
                          <strong>Proje masası olarak kullanılabilir</strong>
                          <span>Çalışma alanı {formatMeasure(externalPreviewDefinition.width)} × {formatMeasure(externalPreviewDefinition.depth)} cm, masa yüksekliği {formatMeasure(externalPreviewDefinition.height)} cm olarak ayarlanır.</span>
                        </div>
                      </div>
                    )}
                    {externalPreviewDefinition && !externalIsDesk && (
                      <div className={`fit-assessment ${externalEmptyDeskFit && externalCurrentLayoutFit ? 'fits' : 'blocked'}`}>
                        {externalEmptyDeskFit ? <Check size={18} /> : <X size={18} />}
                        <div>
                          <strong>{!externalEmptyDeskFit
                            ? `${desk.width} × ${desk.depth} cm masaya sığmıyor`
                            : externalCurrentLayoutFit
                              ? `${desk.width} × ${desk.depth} cm masaya sığıyor`
                              : 'Masa ölçüsüne sığıyor, mevcut düzende boş alan yok'}</strong>
                          <span>{externalCurrentLayoutFit
                            ? `Çakışmasız konum bulundu · ${externalCurrentLayoutFit.rotation === 90 ? '90° çevrilmiş' : 'normal yön'} · gerçek kaplama ${formatMeasure(externalDraft?.footprintWidth ?? null)} × ${formatMeasure(externalDraft?.footprintDepth ?? null)} cm`
                            : externalEmptyDeskFit
                              ? 'Mevcut ekipmanlardan birini taşıyarak veya kaldırarak yer açabilirsin.'
                              : `Ürünün masa üzerindeki kaplaması ${formatMeasure(externalDraft?.footprintWidth ?? null)} × ${formatMeasure(externalDraft?.footprintDepth ?? null)} cm.`}</span>
                        </div>
                      </div>
                    )}
                    <p>{discovery.externalMatch.measurementNote}</p>
                    <div className={`measurement-trust ${discovery.externalMatch.measurementStatus}`}>
                      {discovery.externalMatch.measurementStatus === 'official-structured' && 'Resmî kaynak · yüksek güven'}
                      {discovery.externalMatch.measurementStatus === 'source-structured' && 'Yapılandırılmış kaynak'}
                      {discovery.externalMatch.measurementStatus === 'provider-order' && 'Doğrulama gerekir'}
                      {discovery.externalMatch.measurementStatus === 'identity-only' && 'Yalnızca ürün kimliği'}
                    </div>
                    <small>{discovery.externalMatch.specifications.length} kaynak alanı · Stand/temas alanı ayrı yayımlanmadıysa otomatik tahmin edilmez.</small>
                    <details className="spec-details">
                      <summary>Tüm teknik özellikleri göster <span>{discovery.externalMatch.specifications.length}</span></summary>
                      <SpecificationList specifications={discovery.externalMatch.specifications} />
                    </details>
                    {discovery.externalMatch.sourceUrl && (
                      <a className="source-link" href={discovery.externalMatch.sourceUrl} target="_blank" rel="noreferrer">Kaynağı aç</a>
                    )}
                    {externalDraft ? (
                      <div className="external-import-panel">
                        <div className="import-panel-heading">
                          <div><span>3D katalog kaydı</span><strong>{externalIsDesk ? 'Masa ölçülerini doğrula' : 'Masa kaplama alanını doğrula'}</strong></div>
                          <small>{externalIsDesk ? 'Bu ürün mevcut masanın üstüne eklenmez; projenin çalışma masası ölçülerini değiştirir.' : 'Güvenli sığma hesabı için gövdenin tam genişlik ve derinliği kullanılır. Üreticinin ayrı stand tabanı yayımlanmışsa düzenleyebilirsin.'}</small>
                        </div>
                        {autoCatalogStatus !== 'idle' && (
                          <div className={`auto-catalog-status ${autoCatalogStatus}`}>
                            {autoCatalogStatus === 'saving' && 'Doğrulanmış ürün otomatik kataloğa kaydediliyor…'}
                            {autoCatalogStatus === 'saved' && 'Doğrulanmış ürün SQL kataloğuna otomatik kaydedildi.'}
                            {autoCatalogStatus === 'failed' && 'Ürün bulundu; otomatik kayıt tamamlanamadı. Aşağıdaki düğmeyle yeniden deneyebilirsin.'}
                          </div>
                        )}
                        <label className="select-field">
                          <span>Kategori</span>
                          <select
                            value={externalDraft.category}
                            onChange={(event) => {
                              const category = event.target.value as EquipmentCategory
                              setExternalDraft({ ...externalDraft, category, model: defaultModelByCategory[category] })
                            }}
                          >
                            {Object.entries(categoryLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
                          </select>
                        </label>
                        <div className="field-grid">
                          <NumberField label="Temas genişliği" value={externalDraft.footprintWidth} suffix="cm" onChange={(footprintWidth) => setExternalDraft({ ...externalDraft, footprintWidth })} />
                          <NumberField label="Temas derinliği" value={externalDraft.footprintDepth} suffix="cm" onChange={(footprintDepth) => setExternalDraft({ ...externalDraft, footprintDepth })} />
                        </div>
                        <label className="color-field">
                          <span>3D yüzey rengi</span>
                          <input type="color" value={externalDraft.color} onChange={(event) => setExternalDraft({ ...externalDraft, color: event.target.value })} />
                        </label>
                        {externalImportError && <p className="drawer-error">{externalImportError}</p>}
                        <button className="import-product-button" onClick={importExternalProduct} disabled={isImportingExternal}>
                          {isImportingExternal ? <LoaderCircle className="spin" size={16} /> : <Plus size={16} />}
                          {isImportingExternal ? 'Kataloğa alınıyor' : externalIsDesk ? 'Kataloğa al ve masa olarak kullan' : externalCurrentLayoutFit ? 'Kataloğa al ve otomatik yerleştir' : 'Kataloğa kaydet'}
                        </button>
                      </div>
                    ) : (
                      <p className="external-import-unavailable">Eksiksiz W/H/D ölçüsü bulunmadan ürün 3D kataloğa eklenmez.</p>
                    )}
                  </article>
                )}

                <div className="discovery-summary">
                  <span>Yerel katalog</span>
                  <strong>{discovery.localMatches.length} eşleşme</strong>
                </div>
                <div className="discovery-results">
                  {discovery.localMatches.map((result) => (
                    <article key={result.id} className="discovery-result">
                      <div className="result-heading">
                        <div>
                          <span>{result.brand}</span>
                          <h3>{result.displayName}</h3>
                          <small>{result.manufacturerPartNumber ?? result.modelName}{result.gtin ? ` · ${result.gtin}` : ''}</small>
                        </div>
                        <span className={result.isVerified ? 'quality verified' : 'quality'}>{result.isVerified ? 'Doğrulanmış' : result.dataQuality}</span>
                      </div>
                      <div className="measure-strip">
                        <span><b>Genişlik</b>{result.widthCm} cm</span>
                        <span><b>Yükseklik</b>{result.heightCm} cm</span>
                        <span><b>Derinlik</b>{result.depthCm} cm</span>
                        <span><b>Ağırlık</b>{result.weightKg ?? '—'} kg</span>
                      </div>
                      {localFitById.get(result.id) && !localFitById.get(result.id)?.isDesk && (
                        <div className={`fit-assessment ${localFitById.get(result.id)?.emptyDesk && localFitById.get(result.id)?.currentLayout ? 'fits' : 'blocked'}`}>
                          {localFitById.get(result.id)?.emptyDesk ? <Check size={18} /> : <X size={18} />}
                          <div>
                            <strong>{!localFitById.get(result.id)?.emptyDesk
                              ? `${desk.width} × ${desk.depth} cm masaya sığmıyor`
                              : localFitById.get(result.id)?.currentLayout
                                ? `${desk.width} × ${desk.depth} cm masaya sığıyor`
                                : 'Masa ölçüsüne sığıyor, mevcut düzende boş alan yok'}</strong>
                            <span>{localFitById.get(result.id)?.currentLayout
                              ? 'Çakışmasız konum bulundu; “Masaya ekle” otomatik yerleştirir.'
                              : 'Yer açmak için mevcut ekipmanları taşı veya masa ölçülerini değiştir.'}</span>
                          </div>
                        </div>
                      )}
                      <div className="result-footer">
                        <span>{localFitById.get(result.id)?.isDesk ? `Çalışma alanı ${result.widthCm} × ${result.depthCm} cm` : `Temas alanı ${result.footprintWidthCm} × ${result.footprintDepthCm} cm`}</span>
                        <div className="result-actions">
                          <button className="secondary" onClick={() => loadProductSpecifications(result.id)} disabled={loadingDetailId === result.id}>
                            {loadingDetailId === result.id ? <LoaderCircle className="spin" size={14} /> : <Database size={14} />}
                            Tüm özellikler
                          </button>
                          <button className="secondary danger" aria-label={`${result.displayName} ürününü katalogdan kaldır`} onClick={() => removeCatalogProduct(result.id, result.displayName)}>
                            <Trash2 size={14} />
                          </button>
                          <button onClick={() => addDiscoveredProduct(result.id)}><Plus size={14} /> {localFitById.get(result.id)?.isDesk ? 'Masa olarak kullan' : 'Masaya ekle'}</button>
                        </div>
                      </div>
                      {productDetails[result.id] && (
                        <details className="spec-details local-specs" open>
                          <summary>Ürün veri sayfası <span>{productDetails[result.id].specifications.length + productDetails[result.id].attributes.length}</span></summary>
                          <SpecificationList specifications={[
                            ...productDetails[result.id].attributes.map((attribute) => ({
                              group: 'Kategorik özellikler',
                              name: attribute.name,
                              value: attribute.numericValue?.toString() ?? attribute.textValue ?? (attribute.booleanValue === null ? '—' : attribute.booleanValue ? 'Evet' : 'Hayır'),
                              unit: attribute.unit,
                            })),
                            ...productDetails[result.id].specifications,
                          ]} />
                          <div className="source-stack">
                            {productDetails[result.id].sources.map((source) => (
                              <a key={`${source.provider}-${source.sourceUrl}`} href={source.sourceUrl} target="_blank" rel="noreferrer">{source.provider}</a>
                            ))}
                          </div>
                        </details>
                      )}
                    </article>
                  ))}
                  {discovery.localMatches.length === 0 && !discovery.externalMatch && discovery.externalMatches.length === 0 && (
                    <p className="drawer-empty">Bu model için doğrulanabilir fiziksel ölçü yayımlanmış bir kaynak bulunamadı. Marka ve model kodunu kontrol edip yeniden deneyebilirsin; üyelik gerekmez.</p>
                  )}
                </div>
              </>
            )}
          </section>
        </div>
      )}
    </main>
  )
}

export default App
