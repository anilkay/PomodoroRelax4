// Tailwind Play config for both layouts. Loaded right after
// lib/tailwindcss/tailwindcss-play-3.4.17.js, which defines window.tailwind.
// Kept in a file rather than inline so the Content-Security-Policy can be script-src 'self'.
tailwind.config = {
    theme: {
        extend: {
            fontFamily: {
                sans: ['Inter', 'system-ui', 'sans-serif'],
                mono: ['JetBrains Mono', 'monospace'],
            },
            colors: {
                'app-base': '#0c0c18',
                'focus-glow':   'rgba(248,113,113,0.18)',
                'sbreak-glow':  'rgba(52,211,153,0.18)',
                'lbreak-glow':  'rgba(129,140,248,0.18)',
                'focus-accent':  '#f87171',
                'sbreak-accent': '#34d399',
                'lbreak-accent': '#818cf8',
            }
        }
    }
};
