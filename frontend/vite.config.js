import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],

  // Base URL for production - set this to your production domain
  // In production, this is typically '/' or your subdirectory
  base: '/',

  // Build configuration
  build: {
    // Output directory
    outDir: 'dist',

    // Enable chunk splitting for better caching
    rollupOptions: {
      output: {
        // Manual chunks for vendor libraries
        manualChunks: {
          'vendor-react': ['react', 'react-dom', 'react-router-dom'],
          'vendor-antd': ['antd', '@ant-design/icons'],
          'vendor-utils': ['axios', 'dayjs', 'zustand'],
          'vendor-query': ['@tanstack/react-query'],
          'vendor-charts': ['recharts'],
        },
      },
    },

    // Source maps for production debugging (set to false for smaller bundle)
    sourcemap: false,

    // Minify options
    minify: 'esbuild',

    // Chunk size warning limit
    chunkSizeWarningLimit: 1000,
  },

  // Server configuration (development only)
  server: {
    port: 5173,
    host: true,
    // Enable HTTPS for development
    https: false, // Set to true if you want HTTPS for frontend too
    // CORS for development - allow all origins in dev
    cors: true,
  },

  // Preview configuration (production test)
  preview: {
    port: 4173,
    host: true,
  },

  // Optimize dependencies
  optimizeDeps: {
    include: ['react', 'react-dom', 'antd', 'axios', 'dayjs', 'zustand'],
  },

  // Environment variables prefix (VITE_ is required)
  envPrefix: 'VITE_',
})
