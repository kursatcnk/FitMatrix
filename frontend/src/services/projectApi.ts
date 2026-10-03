import { resolveEquipmentDefinition } from '../data/equipmentCatalog'
import type { DeskDimensions, EquipmentDefinition, PlacedEquipment } from '../types/editor'

interface SaveProjectInput {
  projectId: string | null
  projectName: string
  desk: DeskDimensions
  items: PlacedEquipment[]
  catalog: EquipmentDefinition[]
}

interface ProjectResponse {
  id: string
}

export interface ProjectSummary {
  id: string
  name: string
  itemCount: number
  updatedAt: string
}

interface ApiProjectItem {
  id: string
  equipmentProductId: string | null
  name: string
  categorySlug: string
  editorModel: string
  displayColor: string
  widthCm: number
  heightCm: number
  depthCm: number
  footprintWidthCm: number
  footprintDepthCm: number
  positionXcm: number
  positionZCm: number
  rotationDegrees: number
  elevationCm: number
  mountMode: 'surface' | 'vesa'
  screenOrientation: 'landscape' | 'portrait'
  tiltDegrees: number
}

export interface ApiProject {
  id: string
  name: string
  deskWidthCm: number
  deskDepthCm: number
  deskHeightCm: number
  items: ApiProjectItem[]
}

export async function saveProjectToApi(input: SaveProjectInput): Promise<ProjectResponse> {
  const body = {
    name: input.projectName,
    deskWidthCm: input.desk.width,
    deskDepthCm: input.desk.depth,
    deskHeightCm: input.desk.height,
    items: input.items.map((item) => {
      const definition = resolveEquipmentDefinition(item, input.catalog)
      if (!definition) throw new Error(`Ekipman tanımı bulunamadı: ${item.definitionId}`)
      return {
        equipmentProductId: definition.databaseId ?? null,
        name: definition.name,
        categorySlug: definition.category,
        editorModel: definition.model,
        displayColor: definition.color,
        widthCm: definition.width,
        heightCm: definition.height,
        depthCm: definition.depth,
        footprintWidthCm: definition.footprintWidth,
        footprintDepthCm: definition.footprintDepth,
        positionXcm: item.position.x,
        positionZcm: item.position.z,
        rotationDegrees: item.rotation,
        elevationCm: item.elevation ?? 0,
        mountMode: item.mountMode ?? 'surface',
        screenOrientation: item.screenOrientation ?? 'landscape',
        tiltDegrees: item.tilt ?? 0,
      }
    }),
  }

  const url = input.projectId ? `/api/projects/${input.projectId}` : '/api/projects'
  const response = await fetch(url, {
    method: input.projectId ? 'PUT' : 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw new Error(`API kaydı başarısız: ${response.status}`)
  return response.json()
}

export async function listProjects(): Promise<ProjectSummary[]> {
  const response = await fetch('/api/projects')
  if (!response.ok) throw new Error(`Projeler alınamadı: ${response.status}`)
  return response.json()
}

export async function getProject(id: string): Promise<ApiProject> {
  const response = await fetch(`/api/projects/${id}`)
  if (!response.ok) throw new Error(`Proje alınamadı: ${response.status}`)
  return response.json()
}

export async function duplicateProject(id: string): Promise<ApiProject> {
  const response = await fetch(`/api/projects/${id}/duplicate`, { method: 'POST' })
  if (!response.ok) throw new Error(`Proje çoğaltılamadı: ${response.status}`)
  return response.json()
}

export async function deleteProject(id: string): Promise<void> {
  const response = await fetch(`/api/projects/${id}`, { method: 'DELETE' })
  if (!response.ok) throw new Error(`Proje silinemedi: ${response.status}`)
}
