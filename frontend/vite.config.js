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
        manualChunks: (id) => {
          if (id.includes('node_modules')) {
            if (id.includes('antd') || id.includes('@ant-design')) {
              return 'vendor-antd';
            }
            if (id.includes('react-query')) {
              return 'vendor-query';
            }
            if (id.includes('recharts')) {
              return 'vendor-charts';
            }
            if (id.includes('react') || id.includes('scheduler')) {
              return 'vendor-react';
            }
            if (id.includes('zustand')) {
              return 'vendor-zustand';
            }
            if (id.includes('axios') || id.includes('dayjs') || id.includes('lodash')) {
              return 'vendor-utils';
            }
            return 'vendor-misc';
          }
        },
      },
    },

    // Source maps for production debugging (set to false for smaller bundle)
    sourcemap: false,

    // Minify options
    minify: 'esbuild',

    // Chunk size warning limit increased to 1500
    chunkSizeWarningLimit: 1500,
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
