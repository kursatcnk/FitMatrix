import { useEffect, useState } from 'react'
import { Canvas, useThree } from '@react-three/fiber'
import { ContactShadows, Edges, Grid, Html, Line, OrbitControls, RoundedBox, TransformControls } from '@react-three/drei'
import { Quaternion, Vector3, type Group, type PerspectiveCamera } from 'three'
import { resolveEquipmentDefinition } from '../data/equipmentCatalog'
import { useEditorStore } from '../store/editorStore'
import { eyePoint } from './ergonomics'
import { getEdgeClearances, getPlacementFootprint, getPlacementStatus } from './placement'
import { registerSceneCapture } from './sceneCapture'
import { roomBounds, WALL_HEIGHT } from './room'
import type { EquipmentDefinition, PlacedEquipment } from '../types/editor'

function Desk() {
  const desk = useEditorStore((state) => state.desk)
  const legX = desk.width / 2 - 9
  const legZ = desk.depth / 2 - 7

  return (
    <group>
      <mesh position={[0, desk.height - 2, 0]} receiveShadow castShadow>
        <boxGeometry args={[desk.width, 4, desk.depth]} />
        <meshStandardMaterial color="#a48d6c" roughness={0.78} metalness={0.02} />
      </mesh>
      {[-legX, legX].flatMap((x, xIndex) => [-legZ, legZ].map((z, zIndex) => (
        <mesh key={`leg-${xIndex}-${zIndex}`} position={[x, desk.height / 2 - 4, z]} castShadow>
          <boxGeometry args={[5, desk.height - 8, 5]} />
          <meshStandardMaterial color="#252a32" roughness={0.38} metalness={0.58} />
        </mesh>
      )))}
    </group>
  )
}

function ArmSegment({
  from,
  to,
  variant,
}: {
  from: [number, number, number]
  to: [number, number, number]
  variant: 'link' | 'gas'
}) {
  const start = new Vector3(...from)
  const end = new Vector3(...to)
  const direction = end.clone().sub(start)
  const length = direction.length()
  const midpoint = start.clone().add(end).multiplyScalar(0.5)
  const quaternion = new Quaternion().setFromUnitVectors(new Vector3(1, 0, 0), direction.clone().normalize())
  const height = variant === 'gas' ? 5.7 : 4.2
  const depth = variant === 'gas' ? 4.6 : 4.1

  return (
    <group position={midpoint} quaternion={quaternion}>
      <RoundedBox args={[length, height, depth]} radius={1.05} smoothness={3} castShadow>
        <meshStandardMaterial color={variant === 'gas' ? '#262b30' : '#30363c'} roughness={0.3} metalness={0.72} />
      </RoundedBox>
      <RoundedBox
        args={[Math.max(2, length - 4.2), 0.42, depth + 0.04]}
        radius={0.18}
        smoothness={2}
        position={[0, -height / 2 + 0.38, 0]}
        castShadow
      >
        <meshStandardMaterial color="#171b1f" roughness={0.38} metalness={0.62} />
      </RoundedBox>
      {variant === 'gas' && (
        <mesh position={[length * 0.16, 0.5, -depth / 2 - 0.03]} rotation={[0, Math.PI, 0]}>
          <circleGeometry args={[0.72, 24]} />
          <meshStandardMaterial color="#d6a52e" roughness={0.34} metalness={0.3} />
        </mesh>
      )}
    </group>
  )
}

function PivotJoint({
  position,
  radius = 2.55,
  depth = 5,
  accent = false,
}: {
  position: [number, number, number]
  radius?: number
  depth?: number
  accent?: boolean
}) {
  return (
    <group position={position}>
      <mesh rotation={[Math.PI / 2, 0, 0]} castShadow>
        <cylinderGeometry args={[radius, radius, depth, 24]} />
        <meshStandardMaterial color="#20252a" roughness={0.28} metalness={0.76} />
      </mesh>
      <mesh position={[0, 0, -depth / 2 - 0.12]} rotation={[Math.PI / 2, 0, 0]} castShadow>
        <cylinderGeometry args={[0.72, 0.72, 0.32, 20]} />
        <meshStandardMaterial color={accent ? '#d6a52e' : '#b8bec3'} roughness={0.25} metalness={0.82} />
      </mesh>
    </group>
  )
}

function VesaHead({
  position,
  tilt,
}: {
  position: [number, number, number]
  tilt: number
}) {
  return (
    <group position={position} rotation={[tilt, 0, 0]}>
      <mesh position={[0, 0, 1.65]} rotation={[Math.PI / 2, 0, 0]} castShadow>
        <cylinderGeometry args={[2.9, 2.9, 3.6, 24]} />
        <meshStandardMaterial color="#22272c" roughness={0.28} metalness={0.76} />
      </mesh>
      <RoundedBox args={[11.2, 11.2, 0.85]} radius={0.65} smoothness={3} castShadow>
        <meshStandardMaterial color="#333940" roughness={0.32} metalness={0.7} />
      </RoundedBox>
      {[[-4.15, -4.15], [-4.15, 4.15], [4.15, -4.15], [4.15, 4.15]].map(([x, y]) => (
        <mesh key={`${x}-${y}`} position={[x, y, -0.52]} rotation={[Math.PI / 2, 0, 0]} castShadow>
          <cylinderGeometry args={[0.48, 0.48, 0.25, 18]} />
          <meshStandardMaterial color="#aeb4b8" roughness={0.22} metalness={0.88} />
        </mesh>
      ))}
    </group>
  )
}

function VesaMountHardware() {
  const { items, desk, catalog } = useEditorStore()
  const mountedDisplays = items.flatMap((item) => {
    const definition = resolveEquipmentDefinition(item, catalog)
    if (!definition || definition.model !== 'monitor' || item.mountMode !== 'vesa') return []
    const portrait = item.screenOrientation === 'portrait'
    return [{
      item,
      definition,
      screenWidth: portrait ? definition.height : definition.width,
      screenHeight: portrait ? definition.width : definition.height,
    }]
  })

  const groups: typeof mountedDisplays[] = []
  for (const display of mountedDisplays) {
    const group = groups.find((candidate) => {
      const first = candidate[0]
      return Math.abs(first.item.position.x - display.item.position.x) < 6 && Math.abs(first.item.position.z - display.item.position.z) < 6
    })
    if (group) group.push(display)
    else groups.push([display])
  }

  return (
    <group>
      {groups.map((group, groupIndex) => {
        const isDual = group.length > 1
        const rightEdge = Math.max(...group.map((display) => display.item.position.x + display.screenWidth / 2))
        const clampX = isDual
          ? Math.min(desk.width / 2 - 4, rightEdge + 1)
          : Math.min(desk.width / 2 - 5, group[0].item.position.x + 11)
        const clampZ = -desk.depth / 2 + 2.2
        const centers = group.map((display) => (display.item.elevation ?? 0) + display.screenHeight / 2)
        const collarHeights = centers.map((centerHeight) => Math.max(9, Math.min(43, centerHeight - (centerHeight > 38 ? 14 : 6))))
        const poleHeight = Math.max(43, ...collarHeights.map((height) => height + 2.4))

        return (
          <group key={`vesa-rig-${groupIndex}`}>
            <RoundedBox args={[11.5, 1.6, 9.5]} radius={0.8} smoothness={3} position={[clampX, desk.height + 0.8, clampZ]} castShadow>
              <meshStandardMaterial color="#282e34" roughness={0.3} metalness={0.72} />
            </RoundedBox>
            <mesh position={[clampX, desk.height + 2.15, clampZ]} castShadow>
              <cylinderGeometry args={[3.35, 3.75, 2.7, 28]} />
              <meshStandardMaterial color="#252b30" roughness={0.3} metalness={0.74} />
            </mesh>
            <RoundedBox args={[8.4, 9.4, 1.5]} radius={0.6} smoothness={2} position={[clampX, desk.height - 3.2, -desk.depth / 2 - 0.5]} castShadow>
              <meshStandardMaterial color="#252b30" roughness={0.34} metalness={0.72} />
            </RoundedBox>
            <RoundedBox args={[8.2, 1.5, 7.2]} radius={0.55} smoothness={2} position={[clampX, desk.height - 6.1, clampZ]} castShadow>
              <meshStandardMaterial color="#2c3238" roughness={0.32} metalness={0.74} />
            </RoundedBox>
            <mesh position={[clampX, desk.height - 8.7, clampZ]} castShadow>
              <cylinderGeometry args={[0.68, 0.68, 5.2, 16]} />
              <meshStandardMaterial color="#252a2f" roughness={0.26} metalness={0.82} />
            </mesh>
            <mesh position={[clampX, desk.height - 6.8, clampZ]} castShadow>
              <cylinderGeometry args={[2.2, 2.2, 0.55, 24]} />
              <meshStandardMaterial color="#353b41" roughness={0.34} metalness={0.68} />
            </mesh>
            <RoundedBox args={[6.8, 0.68, 0.68]} radius={0.28} smoothness={2} position={[clampX, desk.height - 11.45, clampZ]} castShadow>
              <meshStandardMaterial color="#20252a" roughness={0.28} metalness={0.78} />
            </RoundedBox>
            <mesh position={[clampX, desk.height + 2.8 + poleHeight / 2, clampZ]} castShadow>
              <cylinderGeometry args={[1.75, 1.75, poleHeight, 24]} />
              <meshStandardMaterial color="#30363b" roughness={0.25} metalness={0.8} />
            </mesh>
            <mesh position={[clampX, desk.height + poleHeight + 2.8, clampZ]} castShadow>
              <cylinderGeometry args={[1.92, 1.92, 0.75, 24]} />
              <meshStandardMaterial color="#1f2429" roughness={0.28} metalness={0.8} />
            </mesh>
            {group.map((display, displayIndex) => {
              const centerHeight = (display.item.elevation ?? 0) + display.screenHeight / 2
              const worldY = desk.height + centerHeight
              const collarHeight = collarHeights[displayIndex]
              const direction = Math.sign(display.item.position.x - clampX) || -1
              const start: [number, number, number] = [clampX + direction * 2.5, desk.height + collarHeight, clampZ]
              const panelDepth = Math.min(7, Math.max(2.2, display.definition.depth))
              const panelBackZ = display.item.position.z - panelDepth / 2 - 0.95
              const elbow: [number, number, number] = [
                clampX + (display.item.position.x - clampX) * 0.52,
                desk.height + collarHeight + 1.2,
                panelBackZ - 4.8 - displayIndex * 1.4,
              ]
              const end: [number, number, number] = [display.item.position.x, worldY, panelBackZ]
              const tilt = ((display.item.tilt ?? 0) * Math.PI) / 180
              return (
                <group key={display.item.instanceId}>
                  <RoundedBox args={[6.2, 4.7, 6.1]} radius={0.9} smoothness={3} position={[clampX, desk.height + collarHeight, clampZ]} castShadow>
                    <meshStandardMaterial color="#252b30" roughness={0.3} metalness={0.76} />
                  </RoundedBox>
                  <mesh position={[clampX, desk.height + collarHeight, clampZ - 3.18]} rotation={[Math.PI / 2, 0, 0]} castShadow>
                    <cylinderGeometry args={[0.66, 0.66, 0.35, 20]} />
                    <meshStandardMaterial color="#aeb4b8" roughness={0.22} metalness={0.88} />
                  </mesh>
                  <ArmSegment from={start} to={elbow} variant="link" />
                  <PivotJoint position={elbow} accent />
                  <ArmSegment from={elbow} to={end} variant="gas" />
                  <VesaHead position={end} tilt={tilt} />
                </group>
              )
            })}
          </group>
        )
      })}
    </group>
  )
}

function EquipmentGeometry({ definition, item, invalid }: { definition: EquipmentDefinition; item: PlacedEquipment; invalid: boolean }) {
  const color = invalid ? '#c55b4b' : definition.color
  const material = <meshStandardMaterial color={color} roughness={0.42} metalness={0.18} />

  switch (definition.model) {
    case 'monitor':
      if (item.mountMode === 'vesa') {
        const portrait = item.screenOrientation === 'portrait'
        const panelWidth = portrait ? definition.height : definition.width
        const panelHeight = portrait ? definition.width : definition.height
        const panelDepth = Math.min(7, Math.max(2.2, definition.depth))
        const tilt = ((item.tilt ?? 0) * Math.PI) / 180
        return (
          <group position={[0, panelHeight / 2, 0]} rotation={[tilt, 0, 0]}>
            <RoundedBox args={[panelWidth, panelHeight, panelDepth]} radius={1.15} smoothness={3} castShadow>{material}</RoundedBox>
            <mesh position={[0, 0, panelDepth / 2 + 0.03]}><planeGeometry args={[panelWidth - 2.4, panelHeight - 2.4]} /><meshStandardMaterial color="#0c1114" roughness={0.22} /></mesh>
          </group>
        )
      }
      return (
        <group>
          <RoundedBox args={[definition.width, definition.height * 0.66, 2.4]} radius={1.2} smoothness={3} position={[0, definition.height * 0.64, 0]} castShadow>{material}</RoundedBox>
          <mesh position={[0, definition.height * 0.64, -1.25]}><planeGeometry args={[definition.width - 3, definition.height * 0.66 - 3]} /><meshStandardMaterial color="#111719" roughness={0.25} /></mesh>
          <mesh position={[0, definition.height * 0.25, 0]} castShadow><boxGeometry args={[3, definition.height * 0.22, 3]} />{material}</mesh>
          <RoundedBox args={[definition.footprintWidth, 1.4, definition.footprintDepth]} radius={0.7} smoothness={2} position={[0, 0.7, 0]} castShadow>{material}</RoundedBox>
        </group>
      )
    case 'tower':
      return (
        <group>
          <RoundedBox args={[definition.width, definition.height, definition.depth]} radius={1.5} smoothness={3} position={[0, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
          <mesh position={[0, definition.height * 0.68, definition.depth / 2 + 0.03]}><circleGeometry args={[5.2, 28]} /><meshStandardMaterial color="#11191a" emissive="#2b6f66" emissiveIntensity={0.35} /></mesh>
        </group>
      )
    case 'laptop':
      return (
        <group>
          <RoundedBox args={[definition.width, 1.5, definition.depth]} radius={0.8} smoothness={2} position={[0, 0.75, 0]} castShadow>{material}</RoundedBox>
          <group position={[0, 10, -definition.depth / 2 + 1.5]} rotation={[-0.12, 0, 0]}>
            <RoundedBox args={[definition.width, definition.height, 1.4]} radius={0.8} smoothness={2} castShadow>{material}</RoundedBox>
            <mesh position={[0, 0, 0.72]}><planeGeometry args={[definition.width - 2, definition.height - 2]} /><meshStandardMaterial color="#162124" roughness={0.2} /></mesh>
          </group>
        </group>
      )
    case 'keyboard':
      return <RoundedBox args={[definition.width, definition.height, definition.depth]} radius={1.1} smoothness={3} position={[0, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
    case 'mouse':
      return <RoundedBox args={[definition.width, definition.height, definition.depth]} radius={2.5} smoothness={4} position={[0, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
    case 'speaker':
      return (
        <group>
          <RoundedBox args={[definition.width, definition.height, definition.depth]} radius={1.4} smoothness={3} position={[0, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
          <mesh position={[0, definition.height * 0.62, definition.depth / 2 + 0.04]}><circleGeometry args={[3.7, 28]} /><meshStandardMaterial color="#151a1c" /></mesh>
        </group>
      )
    case 'headphone': {
      const cupWidth = Math.max(2, definition.width * 0.2)
      const cupHeight = Math.max(3, definition.height * 0.38)
      return (
        <group>
          <mesh position={[0, definition.height * 0.62, 0]} rotation={[0, 0, Math.PI / 2]} castShadow>
            <torusGeometry args={[Math.max(2, definition.width * 0.32), Math.max(.5, definition.width * 0.045), 12, 36, Math.PI]} />
            {material}
          </mesh>
          {[-1, 1].map((side) => (
            <RoundedBox key={side} args={[cupWidth, cupHeight, Math.max(2, definition.depth * 0.62)]} radius={1.2} smoothness={3} position={[side * definition.width * 0.34, cupHeight / 2, 0]} castShadow>{material}</RoundedBox>
          ))}
        </group>
      )
    }
    case 'lamp':
      return (
        <group>
          <mesh position={[0, 1, 0]} castShadow><cylinderGeometry args={[8, 8, 2, 32]} />{material}</mesh>
          <mesh position={[0, definition.height * 0.48, 0]} castShadow><cylinderGeometry args={[1, 1.5, definition.height * 0.82, 20]} />{material}</mesh>
          <mesh position={[0, definition.height * 0.88, 0]} rotation={[Math.PI, 0, 0]} castShadow><coneGeometry args={[8, 12, 32, 1, true]} />{material}</mesh>
        </group>
      )
    case 'mat':
      return <RoundedBox args={[definition.width, definition.height, definition.depth]} radius={2.2} smoothness={4} position={[0, definition.height / 2, 0]} receiveShadow>{material}</RoundedBox>
    case 'stand':
      return (
        <group>
          <RoundedBox args={[definition.width, 2.2, definition.depth]} radius={1.1} smoothness={3} position={[0, definition.height, 0]} castShadow>{material}</RoundedBox>
          {[-definition.width / 2 + 3, definition.width / 2 - 3].map((x) => (
            <RoundedBox key={x} args={[3, definition.height, definition.depth - 2]} radius={0.8} smoothness={2} position={[x, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
          ))}
        </group>
      )
    case 'router':
      return (
        <group>
          <RoundedBox args={[definition.width, Math.max(1.5, definition.height * 0.52), definition.depth]} radius={1.2} smoothness={3} position={[0, Math.max(.75, definition.height * 0.26), 0]} castShadow>{material}</RoundedBox>
          {[-.38, -.12, .12, .38].map((offset) => (
            <mesh key={offset} position={[definition.width * offset, definition.height * 0.74, -definition.depth * 0.34]} rotation={[0.12, 0, 0]} castShadow>
              <cylinderGeometry args={[.35, .5, Math.max(3, definition.height * 0.72), 10]} />
              {material}
            </mesh>
          ))}
        </group>
      )
    case 'camera':
      return (
        <group>
          <RoundedBox args={[definition.width, definition.height * 0.72, definition.depth * 0.72]} radius={1.1} smoothness={3} position={[0, definition.height * 0.42, 0]} castShadow>{material}</RoundedBox>
          <mesh position={[0, definition.height * 0.42, definition.depth * 0.48]} rotation={[Math.PI / 2, 0, 0]} castShadow>
            <cylinderGeometry args={[definition.height * 0.22, definition.height * 0.3, Math.max(1.5, definition.depth * 0.42), 28]} />
            <meshStandardMaterial color="#171c20" roughness={0.24} metalness={0.36} />
          </mesh>
          <RoundedBox args={[definition.width * 0.28, definition.height * 0.18, definition.depth * 0.36]} radius={.5} smoothness={2} position={[-definition.width * 0.2, definition.height * 0.88, 0]} castShadow>{material}</RoundedBox>
        </group>
      )
    case 'printer':
      return (
        <group>
          <RoundedBox args={[definition.width, definition.height * 0.78, definition.depth]} radius={1.3} smoothness={3} position={[0, definition.height * 0.39, 0]} castShadow>{material}</RoundedBox>
          <RoundedBox args={[definition.width * 0.72, Math.max(.8, definition.height * 0.08), definition.depth * 0.56]} radius={.7} smoothness={2} position={[0, definition.height * 0.82, -definition.depth * 0.08]} castShadow>{material}</RoundedBox>
          <mesh position={[0, definition.height * 0.34, definition.depth / 2 + .04]}><planeGeometry args={[definition.width * 0.62, definition.height * 0.2]} /><meshStandardMaterial color="#20262b" /></mesh>
        </group>
      )
    case 'phone':
      return (
        <group>
          <RoundedBox args={[definition.width, definition.height, Math.max(.7, definition.depth)]} radius={Math.min(1.5, definition.width * 0.16)} smoothness={4} position={[0, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
          <mesh position={[0, definition.height / 2, Math.max(.7, definition.depth) / 2 + .03]}><planeGeometry args={[definition.width * 0.88, definition.height * 0.9]} /><meshStandardMaterial color="#111719" roughness={0.18} /></mesh>
        </group>
      )
    case 'tablet':
      return (
        <group>
          <RoundedBox args={[definition.width, Math.max(.8, definition.height), definition.depth]} radius={1.1} smoothness={4} position={[0, Math.max(.4, definition.height / 2), 0]} castShadow>{material}</RoundedBox>
          <mesh position={[0, Math.max(.8, definition.height) + .03, 0]} rotation={[-Math.PI / 2, 0, 0]}><planeGeometry args={[definition.width * 0.9, definition.depth * 0.88]} /><meshStandardMaterial color="#111719" roughness={0.18} /></mesh>
        </group>
      )
    case 'console':
      return (
        <group>
          <RoundedBox args={[definition.width, definition.height, definition.depth]} radius={1.6} smoothness={4} position={[0, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
          <mesh position={[definition.width * 0.12, definition.height * 0.62, definition.depth / 2 + .04]}><circleGeometry args={[Math.max(.3, definition.width * 0.035), 20]} /><meshStandardMaterial color="#d8dee5" emissive="#3158d5" emissiveIntensity={0.5} /></mesh>
        </group>
      )
    case 'power':
      return (
        <group>
          <RoundedBox args={[definition.width, definition.height, definition.depth]} radius={1} smoothness={3} position={[0, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
          <mesh position={[0, definition.height * 0.62, definition.depth / 2 + .04]}><planeGeometry args={[definition.width * 0.58, definition.height * 0.2]} /><meshStandardMaterial color="#181e22" emissive="#3158d5" emissiveIntensity={0.16} /></mesh>
        </group>
      )
    case 'desk':
      return (
        <group>
          <RoundedBox args={[definition.width, Math.max(2, definition.height * 0.055), definition.depth]} radius={1} smoothness={3} position={[0, definition.height - Math.max(1, definition.height * 0.0275), 0]} castShadow>{material}</RoundedBox>
          {[-1, 1].flatMap((xSide) => [-1, 1].map((zSide) => (
            <mesh key={`${xSide}-${zSide}`} position={[xSide * definition.width * 0.42, definition.height * 0.48, zSide * definition.depth * 0.4]} castShadow>
              <boxGeometry args={[Math.max(2, definition.width * 0.045), definition.height * 0.94, Math.max(2, definition.depth * 0.045)]} />
              {material}
            </mesh>
          )))}
        </group>
      )
    case 'chair':
      return (
        <group>
          <RoundedBox args={[definition.width * 0.72, Math.max(3, definition.height * 0.09), definition.depth * 0.68]} radius={1.4} smoothness={3} position={[0, definition.height * 0.48, 0]} castShadow>{material}</RoundedBox>
          <RoundedBox args={[definition.width * 0.72, definition.height * 0.48, Math.max(3, definition.depth * 0.09)]} radius={1.8} smoothness={3} position={[0, definition.height * 0.74, -definition.depth * 0.29]} castShadow>{material}</RoundedBox>
          {[-1, 1].flatMap((xSide) => [-1, 1].map((zSide) => (
            <mesh key={`${xSide}-${zSide}`} position={[xSide * definition.width * 0.26, definition.height * 0.23, zSide * definition.depth * 0.22]} castShadow>
              <cylinderGeometry args={[.8, 1, definition.height * 0.44, 12]} />
              {material}
            </mesh>
          )))}
        </group>
      )
    case 'generic':
      return <RoundedBox args={[definition.width, definition.height, definition.depth]} radius={1.2} smoothness={3} position={[0, definition.height / 2, 0]} castShadow>{material}</RoundedBox>
  }
}

function EquipmentItem({ item }: { item: PlacedEquipment }) {
  const [controlledObject, setControlledObject] = useState<Group | null>(null)
  const { items, desk, catalog, selectedId, snapEnabled, transformMode, selectItem, updateItem, beginHistoryBatch, endHistoryBatch } = useEditorStore()
  const definition = resolveEquipmentDefinition(item, catalog)
  if (!definition) return null

  const selected = selectedId === item.instanceId
  const placement = getPlacementStatus(item, items, desk, catalog)
  const footprint = getPlacementFootprint(item, definition)

  const syncTransform = () => {
    const object = controlledObject
    if (!object) return
    updateItem(item.instanceId, {
      position: { x: object.position.x, z: object.position.z },
      rotation: (object.rotation.y * 180) / Math.PI,
    })
  }

  return (
    <>
      <group
        ref={setControlledObject}
        position={[item.position.x, desk.height + (item.elevation ?? 0), item.position.z]}
        rotation={[0, (item.rotation * Math.PI) / 180, 0]}
        onClick={(event) => {
          event.stopPropagation()
          selectItem(item.instanceId)
        }}
      >
        <EquipmentGeometry definition={definition} item={item} invalid={!placement.isValid} />
        {(selected || !placement.isValid) && (
          <mesh position={[0, 0.22, 0]}>
            <boxGeometry args={[footprint.width, 0.35, footprint.depth]} />
            <meshBasicMaterial color={placement.isValid ? '#3158d5' : '#b34b3c'} transparent opacity={0.18} depthWrite={false} />
            <Edges color={placement.isValid ? '#3158d5' : '#b34b3c'} />
          </mesh>
        )}
      </group>
      {selected && controlledObject && (
        <TransformControls
          object={controlledObject}
          mode={transformMode}
          showX={transformMode === 'translate'}
          showY={transformMode === 'rotate'}
          showZ={transformMode === 'translate'}
          translationSnap={snapEnabled ? 2 : null}
          rotationSnap={snapEnabled ? Math.PI / 12 : null}
          onMouseDown={beginHistoryBatch}
          onMouseUp={endHistoryBatch}
          onObjectChange={syncTransform}
          size={0.65}
        />
      )}
    </>
  )
}

function CameraRig() {
  const cameraMode = useEditorStore((state) => state.cameraMode)
  const cameraRevision = useEditorStore((state) => state.cameraRevision)
  const desk = useEditorStore((state) => state.desk)
  const userHeight = useEditorStore((state) => state.userHeight)
  const posture = useEditorStore((state) => state.posture)
  const { camera } = useThree()
  const eye = eyePoint(desk, userHeight, posture)
  // In the eye-level view the orbit pivots around a point on the back half of the desk, slightly below
  // eye height, so the first frame is what the seated user sees and small drags look around from there.
  const target: [number, number, number] = cameraMode === 'eye'
    ? [0, eye.height - 14, -desk.depth / 4]
    : [0, desk.height + 22, 0]

  useEffect(() => {
    const perspective = camera as PerspectiveCamera
    perspective.fov = cameraMode === 'eye' ? 62 : 42
    if (cameraMode === 'top') camera.position.set(0, 310, 0.01)
    else if (cameraMode === 'eye') camera.position.set(0, eye.height, eye.z)
    else camera.position.set(170, 165, 180)
    camera.lookAt(...target)
    perspective.updateProjectionMatrix()
    // target is derived from the same inputs listed here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [camera, cameraMode, cameraRevision, desk.height, desk.depth, eye.height, eye.z])

  return cameraMode === 'eye'
    ? <OrbitControls makeDefault target={target} minDistance={20} maxDistance={140} maxPolarAngle={Math.PI / 1.6} />
    : <OrbitControls makeDefault target={target} minDistance={80} maxDistance={440} maxPolarAngle={Math.PI / 2.02} />
}

// Drafting-style dimension lines from the selected item to each desk edge, drawn just above the desktop.
function SelectionClearances() {
  const { items, desk, catalog, selectedId } = useEditorStore()
  const item = items.find((candidate) => candidate.instanceId === selectedId)
  const definition = item ? resolveEquipmentDefinition(item, catalog) : undefined
  if (!item || !definition || definition.model === 'desk') return null

  const { left, right, back, front, bounds } = getEdgeClearances(item, definition, desk)
  const y = desk.height + 0.35
  const centerX = (bounds.minX + bounds.maxX) / 2
  const centerZ = (bounds.minZ + bounds.maxZ) / 2
  const segments: Array<{ key: string; from: [number, number, number]; to: [number, number, number]; value: number }> = [
    { key: 'left', from: [-desk.width / 2, y, centerZ], to: [bounds.minX, y, centerZ], value: left },
    { key: 'right', from: [bounds.maxX, y, centerZ], to: [desk.width / 2, y, centerZ], value: right },
    { key: 'back', from: [centerX, y, -desk.depth / 2], to: [centerX, y, bounds.minZ], value: back },
    { key: 'front', from: [centerX, y, bounds.maxZ], to: [centerX, y, desk.depth / 2], value: front },
  ]

  return (
    <group>
      {segments.filter((segment) => segment.value >= 0.5).map((segment) => (
        <group key={segment.key}>
          <Line points={[segment.from, segment.to]} color="#161513" lineWidth={1.2} dashed dashSize={1.6} gapSize={1.1} />
          <Html
            position={[(segment.from[0] + segment.to[0]) / 2, y + 0.4, (segment.from[2] + segment.to[2]) / 2]}
            center
            zIndexRange={[5, 0]}
          >
            <span className="clearance-label">{Math.round(segment.value * 10) / 10} cm</span>
          </Html>
        </group>
      ))}
    </group>
  )
}

interface WallProps {
  length: number
  position: [number, number]
  rotation: number
}

// Walls are single-sided planes facing into the room, so a wall between the camera and the desk is
// culled and the room opens up like a dollhouse from whichever side you orbit to.
function Wall({ length, position: [x, z], rotation }: WallProps) {
  return (
    <group position={[x, 0, z]} rotation={[0, rotation, 0]}>
      <mesh position={[0, WALL_HEIGHT / 2, 0]} receiveShadow>
        <planeGeometry args={[length, WALL_HEIGHT]} />
        <meshStandardMaterial color="#ebe7de" roughness={0.95} />
      </mesh>
      <mesh position={[0, 4, 0.3]}>
        <planeGeometry args={[length, 8]} />
        <meshStandardMaterial color="#cfc8b9" roughness={0.8} />
      </mesh>
    </group>
  )
}

function Room() {
  const desk = useEditorStore((state) => state.desk)
  const room = useEditorStore((state) => state.room)
  if (!room.enabled) return null
  const { backWallZ, frontWallZ, leftWallX, rightWallX } = roomBounds(desk, room)
  const centerZ = (backWallZ + frontWallZ) / 2

  return (
    <group>
      <mesh position={[0, 0.01, centerZ]} rotation={[-Math.PI / 2, 0, 0]} receiveShadow>
        <planeGeometry args={[room.width, room.depth]} />
        <meshStandardMaterial color="#d3cab9" roughness={0.9} />
      </mesh>
      <Wall length={room.width} position={[0, backWallZ]} rotation={0} />
      <Wall length={room.width} position={[0, frontWallZ]} rotation={Math.PI} />
      <Wall length={room.depth} position={[leftWallX, centerZ]} rotation={Math.PI / 2} />
      <Wall length={room.depth} position={[rightWallX, centerZ]} rotation={-Math.PI / 2} />
    </group>
  )
}

function Scene() {
  const items = useEditorStore((state) => state.items)
  const selectItem = useEditorStore((state) => state.selectItem)

  return (
    <>
      <color attach="background" args={['#deddd7']} />
      <fog attach="fog" args={['#deddd7', 270, 510]} />
      <ambientLight intensity={1.1} color="#f7f5ef" />
      <directionalLight position={[90, 180, 110]} intensity={2.15} color="#fff9ec" castShadow shadow-mapSize={[2048, 2048]} shadow-camera-far={450} />
      <directionalLight position={[-100, 110, -80]} intensity={0.5} color="#d5dbe4" />
      <Room />
      <group onPointerMissed={() => selectItem(null)}>
        <Desk />
        <VesaMountHardware />
        {items.map((item) => <EquipmentItem key={item.instanceId} item={item} />)}
        <SelectionClearances />
      </group>
      <Grid args={[420, 420]} position={[0, 0.02, 0]} cellSize={10} cellThickness={0.35} cellColor="#c2b9a8" sectionSize={40} sectionThickness={0.72} sectionColor="#8c8474" fadeDistance={340} infiniteGrid />
      <ContactShadows position={[0, 0.04, 0]} opacity={0.28} scale={260} blur={2.6} far={220} />
      <CameraRig />
    </>
  )
}

// Renders a fresh frame and copies it out in the same task, so the canvas does not need
// preserveDrawingBuffer (a cost on every frame) for the occasional snapshot.
function CaptureRegistration() {
  const { gl, scene, camera } = useThree()

  useEffect(() => {
    registerSceneCapture(() => {
      gl.render(scene, camera)
      return new Promise((resolve) => gl.domElement.toBlob(resolve, 'image/png'))
    })
    return () => registerSceneCapture(null)
  }, [gl, scene, camera])

  return null
}

export function EditorCanvas() {
  return (
    <Canvas shadows="percentage" dpr={[1, 1.75]} camera={{ position: [170, 145, 170], fov: 42, near: 0.1, far: 1000 }}>
      <Scene />
      <CaptureRegistration />
    </Canvas>
  )
}
