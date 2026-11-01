// Tailwind config for both layouts. The CLI reads it at build time (TailwindCss target in the
// .csproj) and writes wwwroot/css/tailwind.css with only the classes the files below use.
// A class name built from pieces at runtime is not found; write each one out in full.
module.exports = {
    content: [
        './Components/**/*.razor',
        './Pages/**/*.cshtml',
        './wwwroot/js/**/*.js',
    ],
    theme: {
        extend: {
            fontFamily: {
                sans: ['Inter', 'system-ui', 'sans-serif'],
                mono: ['JetBrains Mono', 'monospace'],
            },
            colors: {
                // Foreground of the current theme (white on dark, ink on light), from app.css.
                // Use text-fg / bg-fg/10 / border-fg/20 etc. instead of *-white.
                'fg': 'rgb(var(--fg) / <alpha-value>)',
                // Cards (bg-surface) and text inputs (bg-field): translucent white on dark,
                // near-white panels on light, where tinted ink would look muddy grey
                'surface': 'var(--surface)',
                'field': 'var(--field)',
                // Validation and error text, readable on either background
                'danger': 'rgb(var(--danger) / <alpha-value>)',
                // Dark ink for text on accent-coloured fills, the same in both themes
                'app-base': '#0c0c18',
                // Session badges on Stats
                'pomodoro':   '#f87171',
                'shortbreak': '#34d399',
                'longbreak':  '#818cf8',
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
