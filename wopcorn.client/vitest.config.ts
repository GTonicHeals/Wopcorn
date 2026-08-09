import { fileURLToPath, URL } from 'node:url';

import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vitest/config';

// Deliberately standalone rather than merged with vite.config.ts. That config shells
// out to `dotnet dev-certs` and reads a PEM at load time to serve HTTPS in dev; unit
// tests need neither, and depending on it would make `npm run test:unit` require the
// .NET SDK.
export default defineConfig({
    plugins: [vue()],
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./src', import.meta.url))
        }
    },
    test: {
        environment: 'jsdom',
        include: ['src/**/__tests__/**/*.spec.ts'],
        restoreMocks: true
    }
});
