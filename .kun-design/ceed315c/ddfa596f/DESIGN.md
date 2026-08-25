# Design Notes: Logo

- Artifact id: `ddfa596f`
- Source HTML path: `.kun-design/ceed315c/ddfa596f/v2.html`
- Design notes file: `.kun-design/ceed315c/ddfa596f/DESIGN.md`
- Selected frame: 1280 x 597 canvas pixels
- Current version: v2
- Updated: 2026-08-25

## Page role
This is the OllamaHub brand-mark showcase. It establishes the primary logo, a compact horizontal lockup, and a monochrome one-color variant for use across the local gateway control center and client setup surfaces.

## Logo concept
The v2 mark is a neutral, angular protocol bridge rather than a radial hub. A horizontal relay core connects two square endpoint ports, while smaller top and bottom ports represent upstream and downstream protocol paths. The central plus-shaped cutout communicates routing and interoperability without a biological or organic silhouette. The geometry is intentionally orthogonal, compact, and legible at favicon and toolbar sizes.

## Visual direction and tokens
- Canvas: deep graphite `#0E141B`.
- Showcase surface: `#151D26`; variant surface: `#1D2833`; border: `#2C3945`.
- Primary text: `#DCE7EE`; secondary text: `#91A4B2`.
- Brand accent: cyan-teal `#16B8C4`; supporting protocol accent: blue `#4D8DFF`.
- Product surfaces use restrained 8px to 12px radii. The logo itself uses 4px port radii and a 12px central core radius, keeping the mark crisp rather than soft.
- Typography uses a bounded system UI scale; the wordmark has zero letter spacing and remains readable at small sizes.

## Responsive behavior and accessibility
The showcase uses a fluid 1280px maximum, a 557px minimum tile height for the selected frame, and a mobile breakpoint at 620px where the primary lockup stacks and variants become a single column. Inline SVG includes a title and description for the primary mark. Focus-visible rules and reduced-motion support are included even though the showcase has no animated interaction.

## Handoff notes
Use the central bridge mark as the primary application icon and keep the compact lockup for navigation, installers, and endpoint setup instructions. Recolor only through the brand accent and monochrome current-color variant; do not reintroduce radial spokes or organic silhouettes. The HTML is standalone with inline CSS and SVG only.

## Version history
- v2: replaced the radial three-node symbol with an angular horizontal protocol bridge and square endpoint ports.
- v1: original radial hub mark.
