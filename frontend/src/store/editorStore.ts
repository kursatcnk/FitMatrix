import { create } from 'zustand'
import { equipmentCatalog, resolveEquipmentDefinition } from '../data/equipmentCatalog'
import { findAvailablePlacement } from '../editor/placement'
import { clampUserHeight, DEFAULT_USER_HEIGHT, type Posture } from '../editor/ergonomics'
import { clampRoom, DEFAULT_ROOM, type RoomSettings } from '../editor/room'
import { applyLanguage, getLanguage, t, type Language } from '../i18n'
import type { DeskDimensions, EquipmentDefinition, PlacedEquipment, SavedProject } from '../types/editor'

export type CameraMode = 'perspective' | 'top' | 'eye'
type TransformMode = 'translate' | 'rotate'

interface EditorSnapshot {
  projectName: string
  desk: DeskDimensions
  items: PlacedEquipment[]
  selectedId: string | null
}

interface EditorState {
  projectId: string | null
  catalog: EquipmentDefinition[]
  projectName: string
  desk: DeskDimensions
  items: PlacedEquipment[]
  selectedId: string | null
  snapEnabled: boolean
  cameraMode: CameraMode
  cameraRevision: number
  userHeight: number
  posture: Posture
  room: RoomSettings
  language: Language
  transformMode: TransformMode
  historyPast: EditorSnapshot[]
  historyFuture: EditorSnapshot[]
  historyBatchStart: EditorSnapshot | null
  addEquipment: (definitionId: string) => boolean
  duplicateItem: (instanceId: string) => void
  updateItem: (instanceId: string, patch: Partial<PlacedEquipment>) => void
  updateDesk: (patch: Partial<DeskDimensions>) => void
  deleteItem: (instanceId: string) => void
  selectItem: (instanceId: string | null) => void
  setProjectName: (name: string) => void
  setProjectId: (projectId: string | null) => void
  setCatalog: (catalog: EquipmentDefinition[]) => void
  setCameraMode: (mode: CameraMode) => void
  resetCamera: () => void
  setUserHeight: (height: number) => void
  setPosture: (posture: Posture) => void
  setRoom: (patch: Partial<RoomSettings>) => void
  setLanguage: (language: Language) => void
  setTransformMode: (mode: TransformMode) => void
  toggleSnap: () => void
  beginHistoryBatch: () => void
  endHistoryBatch: () => void
  undo: () => void
  redo: () => void
  resetProject: () => void
  loadProject: (project: SavedProject) => void
}

const USER_HEIGHT_KEY = 'fitmatrix.userHeightCm'
const POSTURE_KEY = 'fitmatrix.posture'

function readStoredPosture(): Posture {
  try {
    return localStorage.getItem(POSTURE_KEY) === 'standing' ? 'standing' : 'sitting'
  } catch {
    return 'sitting'
  }
}

function readStoredUserHeight() {
  try {
    const stored = Number(localStorage.getItem(USER_HEIGHT_KEY))
    return stored ? clampUserHeight(stored) : DEFAULT_USER_HEIGHT
  } catch {
    return DEFAULT_USER_HEIGHT
  }
}

const defaultDesk: DeskDimensions = { width: 160, depth: 80, height: 75 }
const historyLimit = 60

const createInitialItems = (): PlacedEquipment[] => [
  { instanceId: crypto.randomUUID(), definitionId: 'monitor-27', position: { x: 0, z: -19 }, rotation: 0 },
  { instanceId: crypto.randomUUID(), definitionId: 'keyboard-tkl', position: { x: 0, z: 13 }, rotation: 0 },
]

const copyItems = (items: PlacedEquipment[]) => items.map((item) => ({
  ...item,
  position: { ...item.position },
  dimensions: item.dimensions ? { ...item.dimensions } : undefined,
}))

const snapshot = (state: Pick<EditorState, 'projectName' | 'desk' | 'items' | 'selectedId'>): EditorSnapshot => ({
  projectName: state.projectName,
  desk: { ...state.desk },
  items: copyItems(state.items),
  selectedId: state.selectedId,
})

const appendHistory = (history: EditorSnapshot[], entry: EditorSnapshot) =>
  [...history.slice(-(historyLimit - 1)), entry]

export const useEditorStore = create<EditorState>((set, get) => ({
  projectId: null,
  catalog: equipmentCatalog,
  projectName: t('Ana masa düzenim'),
  desk: defaultDesk,
  items: createInitialItems(),
  selectedId: null,
  snapEnabled: true,
  cameraMode: 'perspective',
  cameraRevision: 0,
  userHeight: readStoredUserHeight(),
  posture: readStoredPosture(),
  room: DEFAULT_ROOM,
  language: getLanguage(),
  transformMode: 'translate',
  historyPast: [],
  historyFuture: [],
  historyBatchStart: null,

  addEquipment: (definitionId) => {
    const current = get()
    const definition = current.catalog.find((item) => item.id === definitionId)
    if (!definition) return false
    const placement = findAvailablePlacement(definition, current.items, current.desk, current.catalog)
    if (!placement) return false

    set((state) => {
      const item: PlacedEquipment = {
        instanceId: crypto.randomUUID(),
        definitionId,
        position: placement.position,
        rotation: placement.rotation,
      }
      return {
        items: [...state.items, item],
        selectedId: item.instanceId,
        historyPast: appendHistory(state.historyPast, snapshot(state)),
        historyFuture: [],
      }
    })
    return true
  },

  duplicateItem: (instanceId) => set((state) => {
    const source = state.items.find((item) => item.instanceId === instanceId)
    if (!source) return state
    const definition = resolveEquipmentDefinition(source, state.catalog)
    if (!definition) return state
    const placement = findAvailablePlacement(definition, state.items, state.desk, state.catalog)
    if (!placement) return state
    const duplicate: PlacedEquipment = {
      ...source,
      instanceId: crypto.randomUUID(),
      position: placement.position,
      rotation: placement.rotation,
      dimensions: source.dimensions ? { ...source.dimensions } : undefined,
    }
    return {
      items: [...state.items, duplicate],
      selectedId: duplicate.instanceId,
      historyPast: appendHistory(state.historyPast, snapshot(state)),
      historyFuture: [],
    }
  }),

  updateItem: (instanceId, patch) => set((state) => {
    const items = state.items.map((item) => {
      if (item.instanceId !== instanceId) return item
      const next = { ...item, ...patch }
      if (patch.position && state.snapEnabled) {
        next.position = {
          x: Math.round(patch.position.x / 2) * 2,
          z: Math.round(patch.position.z / 2) * 2,
        }
      }
      return next
    })
    return {
      items,
      historyPast: state.historyBatchStart ? state.historyPast : appendHistory(state.historyPast, snapshot(state)),
      historyFuture: state.historyBatchStart ? state.historyFuture : [],
    }
  }),
  updateDesk: (patch) => set((state) => ({
    desk: { ...state.desk, ...patch },
    historyPast: appendHistory(state.historyPast, snapshot(state)),
    historyFuture: [],
  })),

  deleteItem: (instanceId) => set((state) => ({
    items: state.items.filter((item) => item.instanceId !== instanceId),
    selectedId: state.selectedId === instanceId ? null : state.selectedId,
    historyPast: appendHistory(state.historyPast, snapshot(state)),
    historyFuture: [],
  })),
  selectItem: (selectedId) => set({ selectedId }),
  setProjectName: (projectName) => set((state) => projectName === state.projectName ? state : ({
    projectName,
    historyPast: appendHistory(state.historyPast, snapshot(state)),
    historyFuture: [],
  })),
  setProjectId: (projectId) => set({ projectId }),
  setCatalog: (catalog) => set({ catalog }),
  setCameraMode: (cameraMode) => set({ cameraMode }),
  resetCamera: () => set((state) => ({ cameraMode: 'perspective', cameraRevision: state.cameraRevision + 1 })),
  setPosture: (posture) => {
    try {
      localStorage.setItem(POSTURE_KEY, posture)
    } catch {
      // Storage can be unavailable in private windows; the value still applies for this session.
    }
    set({ posture })
  },
  setUserHeight: (height) => {
    const userHeight = clampUserHeight(height)
    try {
      localStorage.setItem(USER_HEIGHT_KEY, String(userHeight))
    } catch {
      // Storage can be unavailable in private windows; the value still applies for this session.
    }
    set({ userHeight })
  },
  // The room belongs to the project: it is saved with the draft, backups, share links and SQL projects.
  setRoom: (patch) => set((state) => ({ room: clampRoom({ ...state.room, ...patch }) })),
  setLanguage: (language) => {
    applyLanguage(language)
    set({ language })
  },
  setTransformMode: (transformMode) => set({ transformMode }),
  toggleSnap: () => set((state) => ({ snapEnabled: !state.snapEnabled })),

  beginHistoryBatch: () => set((state) => state.historyBatchStart ? state : ({ historyBatchStart: snapshot(state) })),
  endHistoryBatch: () => set((state) => {
    if (!state.historyBatchStart) return state
    const didChange = JSON.stringify(state.historyBatchStart) !== JSON.stringify(snapshot(state))
    return {
      historyBatchStart: null,
      historyPast: didChange ? appendHistory(state.historyPast, state.historyBatchStart) : state.historyPast,
      historyFuture: didChange ? [] : state.historyFuture,
    }
  }),
  undo: () => set((state) => {
    const previous = state.historyPast.at(-1)
    if (!previous) return state
    return {
      ...previous,
      desk: { ...previous.desk },
      items: copyItems(previous.items),
      historyPast: state.historyPast.slice(0, -1),
      historyFuture: [snapshot(state), ...state.historyFuture].slice(0, historyLimit),
      historyBatchStart: null,
    }
  }),
  redo: () => set((state) => {
    const next = state.historyFuture[0]
    if (!next) return state
    return {
      ...next,
      desk: { ...next.desk },
      items: copyItems(next.items),
      historyPast: appendHistory(state.historyPast, snapshot(state)),
      historyFuture: state.historyFuture.slice(1),
      historyBatchStart: null,
    }
  }),

  resetProject: () => set((state) => ({
    projectName: t('Yeni masa düzeni'),
    projectId: null,
    desk: defaultDesk,
    items: createInitialItems(),
    selectedId: null,
    cameraMode: 'perspective',
    cameraRevision: 0,
    transformMode: 'translate',
    historyPast: appendHistory(state.historyPast, snapshot(state)),
    historyFuture: [],
    historyBatchStart: null,
  })),
  loadProject: (project) => {
    if (project.version !== 1 || !Array.isArray(project.items)) return
    set({
      projectId: project.projectId ?? null,
      projectName: project.projectName,
      desk: { ...project.desk },
      room: clampRoom(project.room ?? DEFAULT_ROOM),
      items: copyItems(project.items),
      selectedId: null,
      historyPast: [],
      historyFuture: [],
      historyBatchStart: null,
    })
  },
}))
