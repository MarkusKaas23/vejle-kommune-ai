import { defineConfig } from 'vite';

export default defineConfig({
    build: {
        lib: {
            // Each feature gets its own entry → own output file.
            // Add more entries here as we build more backoffice extensions.
            entry: 'src/content-converter/index.ts',
            formats: ['es'],
            fileName: () => 'content-converter',
        },
        outDir: '../App_Plugins/VejleAi',
        // Do not wipe the whole folder — other extensions live alongside ours.
        emptyOutDir: false,
        rollupOptions: {
            // Umbraco serves Lit and all @umbraco-cms/* packages via its own importmap.
            // Marking them external keeps our bundle tiny and avoids duplicate runtime code.
            external: [/^@umbraco-cms\//, /^lit/, /^@lit\//],
        },
    },
});
