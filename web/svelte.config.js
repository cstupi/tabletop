import adapter from '@sveltejs/adapter-static';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

// Where presigned uploads and downloads go: TABLETOP_S3_PUBLIC_ENDPOINT's origin, fixed at build time (`npm run build`).
// The default is the dev stack's MinIO. PUBLIC_API_BASE, when set, is the API's origin; unset, the API is same-origin.
const blobOrigin = process.env.TABLETOP_BLOB_ORIGIN ?? 'http://127.0.0.1:9000';
const apiOrigin = process.env.PUBLIC_API_BASE ? [new URL(process.env.PUBLIC_API_BASE).origin] : [];

/** @type {import('@sveltejs/kit').Config} */
export default {
  preprocess: vitePreprocess(),
  kit: {
    adapter: adapter({ pages: 'build', assets: 'build', fallback: 'index.html', strict: false }),
    alias: { $lib: 'src/lib' },
    // Hash mode: SvelteKit hashes its own inline bootstrap into script-src and writes the policy as a <meta> tag in the
    // static pages. frame-ancestors cannot be set by a meta tag; deploy/Caddyfile sends it as a header.
    csp: {
      mode: 'hash',
      directives: {
        'default-src': ['self'],
        'script-src': ['self', 'wasm-unsafe-eval'],            // meshoptimizer instantiates its WASM from bytes
        'worker-src': ['self', 'blob:'],                        // the mesh conversion worker
        'style-src': ['self', 'unsafe-inline'],                 // Svelte style: directives and three.js canvas styles
        'img-src': ['self', 'data:', 'blob:', blobOrigin],
        'connect-src': ['self', blobOrigin, ...apiOrigin],      // /v1, and the presigned PUT and GET
        'font-src': ['self'],
        'object-src': ['none'],
        'base-uri': ['self'],
        'form-action': ['self'],
      },
    },
  },
};
