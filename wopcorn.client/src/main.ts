import '@/assets/tokens.css';
import '@/assets/base.css';

import { createApp } from 'vue';
import { createPinia } from 'pinia';

import App from '@/App.vue';
import router from '@/router';
import { useAuthStore } from '@/stores/auth';
import { useConfigStore } from '@/stores/config';
import { useThemeStore } from '@/stores/theme';

async function bootstrap(): Promise<void> {
  const app = createApp(App);
  app.use(createPinia());
  app.use(router);

  useThemeStore().init();

  // Nothing is rendered until auth.status === 'ready': a signed-in user must
  // never see the login screen flash. router.isReady() also waits for the
  // guard, which boots auth itself on the first navigation.
  await Promise.all([useAuthStore().boot(), useConfigStore().load()]);
  await router.isReady();

  app.mount('#app');
}

/**
 * The service worker only makes the app installable (FR-H7). It is registered in
 * production builds only — in development it would sit between Vite and the
 * browser for no benefit.
 */
function registerServiceWorker(): void {
  if (!import.meta.env.PROD || !('serviceWorker' in navigator)) return;

  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // Not being installable is not worth an error in the console.
    });
  });
}

void bootstrap().then(registerServiceWorker);
