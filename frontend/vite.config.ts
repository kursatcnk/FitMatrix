import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            {
              name: 'three-core',
              test: /node_modules[\\/]three/,
              priority: 30,
              maxSize: 420_000,
            },
            {
              name: 'react-three',
              test: /node_modules[\\/]@react-three/,
              priority: 20,
              maxSize: 420_000,
            },
            {
              name: 'react-core',
              test: /node_modules[\\/](react|react-dom|scheduler)/,
              priority: 10,
            },
          ],
        },
        strictExecutionOrder: true,
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://127.0.0.1:5080',
    },
  },
})
