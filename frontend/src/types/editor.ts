import type { RoomSettings } from '../editor/room'

export type EquipmentCategory =
  | 'display'
  | 'computer'
  | 'input'
  | 'audio'
  | 'imaging'
  | 'network'
  | 'gaming'
  | 'mobile'
  | 'furniture'
  | 'lighting'
  | 'power'
  | 'office'
  | 'accessory'

export type EquipmentModel =
  | 'monitor'
  | 'tower'
  | 'keyboard'
  | 'mouse'
  | 'laptop'
  | 'speaker'
  | 'headphone'
  | 'mat'
  | 'lamp'
  | 'stand'
  | 'desk'
  | 'chair'
  | 'router'
  | 'camera'
  | 'printer'
  | 'phone'
  | 'tablet'
  | 'console'
  | 'power'
  | 'generic'

export type MountMode = 'surface' | 'vesa'
export type ScreenOrientation = 'landscape' | 'portrait'

export interface EquipmentDefinition {
  id: string
  databaseId?: string
  name: string
  brand?: string
  category: EquipmentCategory
  width: number
  height: number
  depth: number
  footprintWidth: number
  footprintDepth: number
  defaultRotation: number
  color: string
  model: EquipmentModel
}

export interface Position2D {
  x: number
  z: number
}

export interface EquipmentDimensions {
  width: number
  height: number
  depth: number
  footprintWidth: number
  footprintDepth: number
}

export interface PlacedEquipment {
  instanceId: string
  definitionId: string
  position: Position2D
  rotation: number
  elevation?: number
  mountMode?: MountMode
  screenOrientation?: ScreenOrientation
  tilt?: number
  dimensions?: EquipmentDimensions
}

export interface DeskDimensions {
  width: number
  depth: number
  height: number
}

export interface SavedProject {
  version: 1
  projectId?: string | null
  projectName: string
  desk: DeskDimensions
  items: PlacedEquipment[]
  room?: RoomSettings
}
