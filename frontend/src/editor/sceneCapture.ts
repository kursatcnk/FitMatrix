type SceneCapture = () => Promise<Blob | null>

let registered: SceneCapture | null = null

// The canvas registers how to grab a frame while it is mounted; callers outside the canvas use captureScene.
export function registerSceneCapture(capture: SceneCapture | null) {
  registered = capture
}

export const captureScene = () => registered?.() ?? Promise.resolve(null)
